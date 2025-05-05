//
// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
//

using System;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.Caching;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.SessionState;
using Microsoft.AspNet.SessionState;

namespace Microsoft.Web.Redis
{
    /// <summary>
    /// Session state provider optimized for environments with sticky sessions (session affinity).
    /// Uses memory caching to reduce Redis reads and optionally batches Redis writes.
    /// This provider assumes that all requests for a single session will be served by the same web server
    /// due to session affinity, while still supporting failover to other servers via Redis persistence.
    /// </summary>
    public class StickyAffinitySessionStateProvider : RedisSessionStateProvider
    {
        private static readonly ObjectCache SessionCache = new MemoryCache("StickyAffinitySessionStateProvider");
        private static readonly CacheItemPolicy DefaultPolicy = new CacheItemPolicy { SlidingExpiration = TimeSpan.FromMinutes(20) };
        
        private static Timer _bulkUpdateTimer;
        private static ConcurrentDictionary<string, DateTime> _pendingUpdates = new ConcurrentDictionary<string, DateTime>();
        
        protected bool EnableMemoryCache = true;
        protected bool EnableBulkUpdates = false;
        protected int BulkUpdateIntervalSeconds = 60;
        protected int MemoryCacheExpiryMinutes = 20; // Default session timeout in ASP.NET
        protected bool UseMemoryCacheForExclusiveAccess = true; // Use memory cache for GetItemExclusive calls

        public override void Initialize(string name, NameValueCollection config)
        {
            base.Initialize(name, config);
            
            // Read configuration options specific to StickyAffinitySessionStateProvider
            EnableMemoryCache = GetBoolConfig(config, "enableMemoryCache", true);
            EnableBulkUpdates = GetBoolConfig(config, "enableBulkUpdates", false);
            BulkUpdateIntervalSeconds = GetIntConfig(config, "bulkUpdateIntervalSeconds", 60);
            MemoryCacheExpiryMinutes = GetIntConfig(config, "memoryCacheExpiryMinutes", 20);
            UseMemoryCacheForExclusiveAccess = GetBoolConfig(config, "useMemoryCacheForExclusiveAccess", true);
            
            // Update default cache policy with configured expiry
            DefaultPolicy.SlidingExpiration = TimeSpan.FromMinutes(MemoryCacheExpiryMinutes);
            
            // Initialize bulk update timer if enabled
            if (EnableBulkUpdates && _bulkUpdateTimer == null)
            {
                lock (configurationCreationLock)
                {
                    if (_bulkUpdateTimer == null)
                    {
                        _bulkUpdateTimer = new Timer(ProcessBulkUpdates, null, 
                            TimeSpan.FromSeconds(BulkUpdateIntervalSeconds), 
                            TimeSpan.FromSeconds(BulkUpdateIntervalSeconds));
                    }
                }
            }
            
            LogUtility.LogInfo("StickyAffinitySessionStateProvider initialized with: enableMemoryCache={0}, enableBulkUpdates={1}, " +
                "bulkUpdateIntervalSeconds={2}, memoryCacheExpiryMinutes={3}, useMemoryCacheForExclusiveAccess={4}", 
                EnableMemoryCache, EnableBulkUpdates, BulkUpdateIntervalSeconds, MemoryCacheExpiryMinutes, UseMemoryCacheForExclusiveAccess);
        }

        protected bool GetBoolConfig(NameValueCollection config, string key, bool defaultValue)
        {
            string value = config[key];
            if (string.IsNullOrEmpty(value))
                return defaultValue;
                
            bool result;
            if (bool.TryParse(value, out result))
                return result;
            
            return defaultValue;
        }
        
        protected int GetIntConfig(NameValueCollection config, string key, int defaultValue)
        {
            string value = config[key];
            if (string.IsNullOrEmpty(value))
                return defaultValue;
                
            int result;
            if (int.TryParse(value, out result))
                return result;
            
            return defaultValue;
        }
        
        private void ProcessBulkUpdates(object state)
        {
            if (_pendingUpdates.Count > 0)
            {
                string[] ids;
                lock (_pendingUpdates)
                {
                    // Get list of IDs to process
                    ids = _pendingUpdates.Keys.ToArray();
                    // Clear the pending updates collection
                    _pendingUpdates.Clear();
                }
                
                LogUtility.LogInfo("StickyAffinity ProcessBulkUpdates => Processing {0} pending updates", ids.Length);
                
                foreach (string id in ids)
                {
                    try
                    {
                        // Get the session data from memory cache
                        string cacheKey = GetCacheKey(id);
                        var sessionData = SessionCache.Get(cacheKey) as ISessionStateItemCollection;
                        
                        if (sessionData != null)
                        {
                            // Get a Redis connection for this session
                            GetAccessToStore(id);
                            
                            // Store in Redis
                            // Converting timeout from minutes to seconds
                            int timeoutInSeconds = MemoryCacheExpiryMinutes * 60;
                            
                            // Call SetAsync asynchronously but don't wait for it
                            var _ = cache.SetAsync(sessionData, timeoutInSeconds);
                            
                            LogUtility.LogInfo("StickyAffinity ProcessBulkUpdates => Updated Redis for Id: {0}", id);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogUtility.LogError("StickyAffinity ProcessBulkUpdates => Error processing Id: {0}, Error: {1}", id, ex.ToString());
                    }
                }
            }
        }
        
        private void AddToPendingUpdates(string id)
        {
            if (EnableBulkUpdates)
            {
                // Only add or update the timestamp for this ID
                _pendingUpdates[id] = DateTime.UtcNow;
                LogUtility.LogInfo("StickyAffinity AddToPendingUpdates => Added/Updated session Id: {0} for bulk update", id);
            }
        }
        
        private string GetCacheKey(string sessionId)
        {
            return "Session_" + sessionId;
        }
        
        public override async Task<GetItemResult> GetItemAsync(HttpContextBase context, string id, CancellationToken cancellationToken)
        {
            try
            {
                if (EnableMemoryCache)
                {
                    LogUtility.LogInfo("StickyAffinity GetItem => Checking memory cache for Session Id: {0}", id);
                    
                    string cacheKey = GetCacheKey(id);
                    var sessionItems = SessionCache.Get(cacheKey) as ISessionStateItemCollection;
                    
                    if (sessionItems != null)
                    {
                        // Return from memory cache
                        LogUtility.LogInfo("StickyAffinity GetItem => Session found in memory cache for Id: {0}", id);
                        
                        // Use lockless read for cached data
                        bool locked = false;
                        TimeSpan lockAge = TimeSpan.Zero;
                        object lockId = null;
                        SessionStateActions actions = SessionStateActions.None;
                        
                        var sessionData = new SessionStateStoreData(
                            sessionItems, 
                            new HttpStaticObjectsCollection(),
                            MemoryCacheExpiryMinutes);
                        
                        return new GetItemResult(sessionData, locked, lockAge, lockId, actions);
                    }
                }
                
                // Cache miss or memory cache disabled, use Redis
                LogUtility.LogInfo("StickyAffinity GetItem => Session not found in memory cache, using Redis for Id: {0}", id);
                GetItemResult result = await base.GetItemAsync(context, id, cancellationToken);
                
                // If we got valid data from Redis, cache it for future reads
                if (EnableMemoryCache && result.Item != null && result.Item.Items != null)
                {
                    StoreInMemoryCache(id, result.Item.Items as ISessionStateItemCollection, result.Item.Timeout);
                    LogUtility.LogInfo("StickyAffinity GetItem => Cached Redis data in memory for Id: {0}", id);
                }
                
                return result;
            }
            catch (Exception e)
            {
                LogUtility.LogError("StickyAffinity GetItem => {0}", e.ToString());
                LastException = e;
                if (configuration.ThrowOnError)
                {
                    throw;
                }
                return null;
            }
        }

        // Helper method to store data in the memory cache        
        protected void StoreInMemoryCache(string id, ISessionStateItemCollection sessionItems, int timeoutMinutes)
        {
            if (sessionItems != null)
            {
                string cacheKey = GetCacheKey(id);
                var policy = new CacheItemPolicy
                {
                    SlidingExpiration = TimeSpan.FromMinutes(timeoutMinutes)
                };
                
                SessionCache.Set(cacheKey, sessionItems, policy);
            }
        }
        
        public override async Task<GetItemResult> GetItemExclusiveAsync(HttpContextBase context, string id, CancellationToken cancellationToken)
        {
            try
            {
                if (EnableMemoryCache && UseMemoryCacheForExclusiveAccess)
                {
                    LogUtility.LogInfo("StickyAffinity GetItemExclusive => Checking memory cache for Session Id: {0}", id);
                    
                    string cacheKey = GetCacheKey(id);
                    var sessionItems = SessionCache.Get(cacheKey) as ISessionStateItemCollection;
                    
                    if (sessionItems != null)
                    {
                        // We still need to acquire a lock in Redis to maintain compatibility with multiple servers
                        // and to support failover scenarios, but we'll use the data from memory if available
                        GetAccessToStore(id);
                        
                        // Try to acquire a lock in Redis
                        DateTime lockTime = DateTime.Now;
                        int lockTimeout = (int)configuration.RequestTimeout.TotalSeconds;
                        
                        // We'll use TryTakeWriteLockAndGetData but discard the data since we have it in memory
                        var lockResult = await cache.TryTakeWriteLockAndGetDataAsync(lockTime, lockTimeout);
                        bool lockAcquired = lockResult.Success;
                        object lockId = lockResult.LockId;
                        
                        if (!lockAcquired)
                        {
                            // If we couldn't acquire the lock, fallback to base implementation
                            LogUtility.LogInfo("StickyAffinity GetItemExclusive => Could not acquire lock, using Redis for Id: {0}", id);
                            return await base.GetItemExclusiveAsync(context, id, cancellationToken);
                        }
                        
                        // We have the data in memory and acquired the lock in Redis
                        LogUtility.LogInfo("StickyAffinity GetItemExclusive => Using memory cache data with Redis lock for Id: {0}", id);
                        
                        sessionId = id; // For EndRequest cleanup
                        sessionLockId = lockId;
                        
                        // Return memory cached data with the Redis lock
                        var sessionData = new SessionStateStoreData(
                            sessionItems, 
                            new HttpStaticObjectsCollection(),
                            MemoryCacheExpiryMinutes);
                        
                        bool locked = false; // We got the lock
                        TimeSpan lockAge = TimeSpan.Zero;
                        SessionStateActions actions = SessionStateActions.None;
                        
                        return new GetItemResult(sessionData, locked, lockAge, lockId, actions);
                    }
                }
                
                // Cache miss, memory cache disabled, or exclusive memory cache disabled - use Redis
                LogUtility.LogInfo("StickyAffinity GetItemExclusive => Using Redis for Id: {0}", id);
                GetItemResult result = await base.GetItemExclusiveAsync(context, id, cancellationToken);
                
                // If we got valid data from Redis and we're not locked, store it in the memory cache
                if (EnableMemoryCache && !result.Locked && result.Item != null && result.Item.Items != null)
                {
                    StoreInMemoryCache(id, result.Item.Items as ISessionStateItemCollection, result.Item.Timeout);
                    LogUtility.LogInfo("StickyAffinity GetItemExclusive => Cached Redis data in memory for Id: {0}", id);
                }
                
                return result;
            }
            catch (Exception e)
            {
                LogUtility.LogError("StickyAffinity GetItemExclusive => {0}", e.ToString());
                LastException = e;
                if (configuration.ThrowOnError)
                {
                    throw;
                }
                return null;
            }
        }
        
        public override async Task SetAndReleaseItemExclusiveAsync(HttpContextBase context, string id, SessionStateStoreData item, object lockId, bool newItem, CancellationToken cancellationToken)
        {
            try
            {
                LogUtility.LogInfo("StickyAffinity SetAndReleaseItemExclusive => Session Id: {0}, Session provider object: {1}.", id, this.GetHashCode());
                
                if (EnableBulkUpdates && !newItem)
                {
                    // When bulk updates enabled, we only need to release the lock in Redis
                    // and store the data for a later bulk update
                    GetAccessToStore(id);
                    await cache.ReleaseLockIfLockIdMatchAsync(lockId, item.Timeout * 60);
                    
                    // Update the memory cache with the new data
                    if (EnableMemoryCache && item != null && item.Items != null)
                    {
                        StoreInMemoryCache(id, item.Items as ISessionStateItemCollection, item.Timeout);
                        LogUtility.LogInfo("StickyAffinity SetAndReleaseItemExclusive => Updated memory cache for Id: {0}", id);
                    }
                    
                    // Add to pending updates
                    AddToPendingUpdates(id);
                }
                else
                {
                    // For new items or when bulk updates disabled, use the standard behavior
                    await base.SetAndReleaseItemExclusiveAsync(context, id, item, lockId, newItem, cancellationToken);
                    
                    // Update the memory cache with the new data
                    if (EnableMemoryCache && item != null && item.Items != null)
                    {
                        StoreInMemoryCache(id, item.Items as ISessionStateItemCollection, item.Timeout);
                        LogUtility.LogInfo("StickyAffinity SetAndReleaseItemExclusive => Updated memory cache for Id: {0}", id);
                    }
                }
                
                // Clear session data to prevent multiple releases
                sessionId = null;
                sessionLockId = null;
            }
            catch (Exception e)
            {
                LogUtility.LogError("StickyAffinity SetAndReleaseItemExclusive => {0}", e.ToString());
                LastException = e;
                if (configuration.ThrowOnError)
                {
                    throw;
                }
            }
        }
        
        public override async Task RemoveItemAsync(HttpContextBase context, string id, object lockId, SessionStateStoreData item, CancellationToken cancellationToken)
        {
            try
            {
                // Remove from memory cache first
                if (EnableMemoryCache)
                {
                    string cacheKey = GetCacheKey(id);
                    SessionCache.Remove(cacheKey);
                    LogUtility.LogInfo("StickyAffinity RemoveItem => Removed from memory cache for Id: {0}", id);
                }
                
                // Remove from Redis
                await base.RemoveItemAsync(context, id, lockId, item, cancellationToken);
            }
            catch (Exception e)
            {
                LogUtility.LogError("StickyAffinity RemoveItem => {0}", e.ToString());
                LastException = e;
                if (configuration.ThrowOnError)
                {
                    throw;
                }
            }
        }
        
        public override async Task ResetItemTimeoutAsync(HttpContextBase context, string id, CancellationToken cancellationToken)
        {
            try
            {
                // With MemoryCache, sliding expiration will automatically handle timeout resets
                // when the item is accessed, but we'll update Redis as well
                await base.ResetItemTimeoutAsync(context, id, cancellationToken);
            }
            catch (Exception e)
            {
                LogUtility.LogError("StickyAffinity ResetItemTimeout => {0}", e.ToString());
                LastException = e;
                if (configuration.ThrowOnError)
                {
                    throw;
                }
            }
        }
        
        public override void Dispose()
        {
            try
            {
                // Process any remaining updates before shutting down
                if (_pendingUpdates.Count > 0)
                {
                    ProcessBulkUpdates(null);
                }
                
                // Dispose the bulk update timer if it exists
                if (_bulkUpdateTimer != null)
                {
                    _bulkUpdateTimer.Dispose();
                    _bulkUpdateTimer = null;
                }
            }
            finally
            {
                base.Dispose();
            }
        }
    }
} 