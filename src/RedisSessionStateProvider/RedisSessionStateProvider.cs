//
// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
//

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.SessionState;
using Microsoft.AspNet.SessionState;

namespace Microsoft.Web.Redis
{
    public class RedisSessionStateProvider : SessionStateStoreProviderAsyncBase
    {
        // We want to release lock (if exists) during EndRequest, to do that we need session-id and lockId but EndRequest do not have these parameter passed to it.
        // So we are going to store 'sessionId' and 'lockId' when we acquire lock. so that EndRequest can release lock at the end.
        // If we removed the lock before that than we will clear these by our self so that EndRequest won't do that again (only Release item exclusive does that).
        internal string sessionId;

        internal object sessionLockId;
        private const int FROM_MIN_TO_SEC = 60;

        internal static ProviderConfiguration configuration;
        internal static object configurationCreationLock = new object();
        internal ICacheConnection cache;

        private static object _lastException = new object();

        /// <summary>
        /// We do not want to throw exception from session state provider because this will break customer application and they can't get chance to handel it.
        /// So if exception occurs because of some problem we store it in HttpContext using a key that we know and return null to customer. Now, when customer
        /// get null from any of session operation they should call this method to identify if there was any exception and because of that got null.
        /// </summary>
        public static Exception LastException
        {
            get
            {
                if (HttpContext.Current != null)
                {
                    return (Exception)HttpContext.Current.Items[_lastException];
                }
                return null;
            }

            set
            {
                if (HttpContext.Current != null)
                {
                    HttpContext.Current.Items[_lastException] = value;
                }
            }
        }

        protected void GetAccessToStore(string id)
        {
            if (cache == null)
            {
                cache = new RedisConnectionWrapper(configuration, id);
            }
            else
            {
                cache.Keys.RegenerateKeyStringIfIdModified(id, configuration.ApplicationName);
            }
        }

        public override void Initialize(string name, System.Collections.Specialized.NameValueCollection config)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }

            if (name == null || name.Length == 0)
            {
                name = "MyCacheStore";
            }

            if (String.IsNullOrEmpty(config["description"]))
            {
                config.Remove("description");
                config.Add("description", "Redis as a session data store");
            }

            base.Initialize(name, config);

            // If configuration exists then use it otherwise read from config file and create one
            if (configuration == null)
            {
                lock (configurationCreationLock)
                {
                    if (configuration == null)
                    {
                        configuration = ProviderConfiguration.ProviderConfigurationForSessionState(config);
                    }
                }
            }
        }

        public override bool SetItemExpireCallback(SessionStateItemExpireCallback expireCallback)
        {
            //We don't receive notifications when cache items expire, so we can't support Session_OnEnd.
            return false;
        }

        public override void InitializeRequest(HttpContextBase context)
        {
            //Not need. Initializing in 'Initialize method'.
        }

        public override void Dispose()
        {
            //Not needed. Cleanup is done in 'EndRequest'.
        }

        public override async Task EndRequestAsync(HttpContextBase context)
        {
            try
            {
                // This check is required for unit tests to work
                int sessionTimeoutInSeconds;
                if (context != null && context.Session != null)
                {
                    sessionTimeoutInSeconds = context.Session.Timeout * FROM_MIN_TO_SEC;
                }
                else
                {
                    sessionTimeoutInSeconds = (int)configuration.SessionTimeout.TotalSeconds;
                }

                if (sessionId != null && sessionLockId != null)
                {
                    // If we have a lock from GetItemExclusive and it is not release at end request time than that's a bug.
                    // Either ReleaseItemExclusive or SetAndReleaseItemExclusive should be called. But anyway we are releasing
                    // the lock to avoid deadlock in subsequent requests for same session. We do not care about the data we
                    // leave the data as it is and just release the lock.
                    LogUtility.LogWarning("EndRequest => Session Id: {0}, Session provider object: {1} => Releasing orphaned lock at EndRequest. SessionID :{2}, LockID: {3}", sessionId, this.GetHashCode(), sessionId, sessionLockId);
                    GetAccessToStore(sessionId);
                    await cache.ReleaseLockIfLockIdMatchAsync(sessionLockId, sessionTimeoutInSeconds);
                }
            }
            catch (Exception e)
            {
                LogUtility.LogError("EndRequest => {0}", e.ToString());
                LastException = e;
                if (configuration.ThrowOnError)
                {
                    throw;
                }
            }
        }

        public override SessionStateStoreData CreateNewStoreData(HttpContextBase context, int timeout)
        {
            //Creating empty session store data and return it.
            LogUtility.LogInfo("CreateNewStoreData => Session provider object: {0}.", this.GetHashCode());
            return new SessionStateStoreData(new SessionStateItemCollection(), new HttpStaticObjectsCollection(), timeout);
        }

        public override async Task CreateUninitializedItemAsync(HttpContextBase context, string id, int timeout, CancellationToken cancellationToken)
        {
            try
            {
                LogUtility.LogInfo("CreateUninitializedItem => Session Id: {0}, Session provider object: {1}.", id, this.GetHashCode());
                GetAccessToStore(id);

                SessionStateItemCollection sessionItems = new SessionStateItemCollection();
                sessionItems["SessionStateActions"] = SessionStateActions.InitializeItem;
                await cache.SetAsync(sessionItems, (timeout * FROM_MIN_TO_SEC));
            }
            catch (Exception e)
            {
                LogUtility.LogError("CreateUninitializedItem => {0}", e.ToString());
                LastException = e;
                if (configuration.ThrowOnError)
                {
                    throw;
                }
            }
        }

        public override async Task<GetItemResult> GetItemAsync(HttpContextBase context, string id, CancellationToken cancellationToken)
        {
            LogUtility.LogInfo("GetItem => Session Id: {0}, Session provider object: {1}.", id, this.GetHashCode());
            var result = await GetItemFromSessionStoreAsync(false, context, id, cancellationToken);
            return new GetItemResult(result.SessionData, result.Locked, result.LockAge, result.LockId, result.Actions);
        }

        public override async Task<GetItemResult> GetItemExclusiveAsync(HttpContextBase context, string id, CancellationToken cancellationToken)
        {
            LogUtility.LogInfo("GetItemExclusive => Session Id: {0}, Session provider object: {1}.", id, this.GetHashCode());
            var result = await GetItemFromSessionStoreAsync(true, context, id, cancellationToken);
            return new GetItemResult(result.SessionData, result.Locked, result.LockAge, result.LockId, result.Actions);
        }

        private async Task<(SessionStateStoreData SessionData, bool Locked, TimeSpan LockAge, object LockId, SessionStateActions Actions)> GetItemFromSessionStoreAsync(bool isWriteLockRequired, HttpContextBase context, string id, CancellationToken cancellationToken)
        {
            try
            {
                SessionStateStoreData sessionStateStoreData = null;
                bool locked = false;
                TimeSpan lockAge = TimeSpan.Zero;
                object lockId = null;
                SessionStateActions actions = SessionStateActions.None;
                
                if (id == null)
                {
                    return (null, locked, lockAge, lockId, actions);
                }
                
                GetAccessToStore(id);
                ISessionStateItemCollection sessionData = null;
                int sessionTimeout;
                bool isLockTaken = false;
                
                //Take read or write lock and if locking successful than get data in sessionData and also update session timeout
                if (isWriteLockRequired)
                {
                    var lockResult = await cache.TryTakeWriteLockAndGetDataAsync(DateTime.Now, (int)configuration.RequestTimeout.TotalSeconds);
                    isLockTaken = lockResult.Success;
                    lockId = lockResult.LockId;
                    sessionData = lockResult.Data;
                    sessionTimeout = lockResult.SessionTimeout;
                    
                    sessionId = id; // signal that we have to remove lock in EndRequest
                    sessionLockId = lockId; // save lockId for EndRequest
                }
                else
                {
                    var lockResult = await cache.TryCheckWriteLockAndGetDataAsync();
                    isLockTaken = lockResult.Success;
                    lockId = lockResult.LockId;
                    sessionData = lockResult.Data;
                    sessionTimeout = lockResult.SessionTimeout;
                }

                if (isLockTaken)
                {
                    locked = false;
                    LogUtility.LogInfo("GetItemFromSessionStore => Session Id: {0}, Session provider object: {1} => Lock taken with lockId: {2}", id, this.GetHashCode(), lockId);
                }
                else
                {
                    sessionId = null;
                    sessionLockId = null;
                    locked = true;
                    LogUtility.LogInfo("GetItemFromSessionStore => Session Id: {0}, Session provider object: {1} => Can not lock, Someone else has lock and lockId is {2}", id, this.GetHashCode(), lockId);
                }

                // If locking is not successful then do not return any result just return lockAge, locked=true and lockId.
                // ASP.NET tries to acquire lock again in 0.5 sec by calling this method again. Using lockAge it finds if
                // lock has been taken more than http request timeout than ASP.NET calls ReleaseItemExclusive and calls this method again to get lock.
                if (locked)
                {
                    lockAge = await cache.GetLockAgeAsync(lockId);
                    return (null, locked, lockAge, lockId, actions);
                }

                if (sessionData == null)
                {
                    // If session data do not exists means it might be exipred and removed. So return null so that asp.net can call CreateUninitializedItem and start again.
                    // But we just locked the record so first release it
                    await ReleaseItemExclusiveAsync(context, id, lockId, cancellationToken);
                    return (null, locked, lockAge, lockId, actions);
                }

                // Restore action flag from session data
                if (sessionData["SessionStateActions"] != null)
                {
                    actions = (SessionStateActions)Enum.Parse(typeof(SessionStateActions), sessionData["SessionStateActions"].ToString());
                }

                //Get data related to this session from sessionDataDictionary and populate session items
                sessionData.Dirty = false;
                sessionStateStoreData = new SessionStateStoreData(sessionData, new HttpStaticObjectsCollection(), sessionTimeout);
                return (sessionStateStoreData, locked, lockAge, lockId, actions);
            }
            catch (Exception e)
            {
                LogUtility.LogError("GetItemFromSessionStore => {0}", e.ToString());
                LastException = e;
                if (configuration.ThrowOnError)
                {
                    throw;
                }
                return (null, false, TimeSpan.Zero, null, SessionStateActions.None);
            }
        }

        public override async Task ResetItemTimeoutAsync(HttpContextBase context, string id, CancellationToken cancellationToken)
        {
            try
            {
                LogUtility.LogInfo("ResetItemTimeout => Session Id: {0}, Session provider object: {1}.", id, this.GetHashCode());
                GetAccessToStore(id);
                await cache.UpdateExpiryTimeAsync((int)configuration.SessionTimeout.TotalSeconds);
                cache = null;
            }
            catch (Exception e)
            {
                LogUtility.LogError("ResetItemTimeout => {0}", e.ToString());
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
                LogUtility.LogInfo("RemoveItem => Session Id: {0}, Session provider object: {1}, Lock ID: {2}.", id, this.GetHashCode(), lockId);
                GetAccessToStore(id);
                await cache.RemoveAndReleaseLockAsync(lockId);
            }
            catch (Exception e)
            {
                LogUtility.LogError("RemoveItem => {0}", e.ToString());
                LastException = e;
                if (configuration.ThrowOnError)
                {
                    throw;
                }
            }
        }

        public override async Task ReleaseItemExclusiveAsync(HttpContextBase context, string id, object lockId, CancellationToken cancellationToken)
        {
            try
            {
                // This check is required for unit tests to work
                int sessionTimeoutInSeconds;
                if (context != null && context.Session != null)
                {
                    sessionTimeoutInSeconds = context.Session.Timeout * FROM_MIN_TO_SEC;
                }
                else
                {
                    sessionTimeoutInSeconds = (int)configuration.SessionTimeout.TotalSeconds;
                }

                if (lockId != null)
                {
                    LogUtility.LogInfo("ReleaseItemExclusive => Session Id: {0}, Session provider object: {1} => For lockId: {2}.", id, this.GetHashCode(), lockId);
                    GetAccessToStore(id);
                    await cache.ReleaseLockIfLockIdMatchAsync(lockId, sessionTimeoutInSeconds);

                    // Either already released lock successfully inside above if block
                    // Or we do not hold lock so we should not release it.
                    sessionId = null;
                    sessionLockId = null;
                }
            }
            catch (Exception e)
            {
                LogUtility.LogError("ReleaseItemExclusive => {0}", e.ToString());
                LastException = e;
                if (configuration.ThrowOnError)
                {
                    throw;
                }
            }
        }

        public override async Task SetAndReleaseItemExclusiveAsync(HttpContextBase context, string id, SessionStateStoreData item, object lockId, bool newItem, CancellationToken cancellationToken)
        {
            try
            {
                GetAccessToStore(id);
                // If it is new record
                if (newItem)
                {
                    ISessionStateItemCollection sessionItems = null;
                    if (item != null && item.Items != null)
                    {
                        sessionItems = item.Items;
                    }
                    else
                    {
                        sessionItems = new SessionStateItemCollection();
                    }

                    if (sessionItems["SessionStateActions"] != null)
                    {
                        sessionItems.Remove("SessionStateActions");
                    }

                    // Converting timout from min to sec
                    await cache.SetAsync(sessionItems, (item.Timeout * FROM_MIN_TO_SEC));
                    LogUtility.LogInfo("SetAndReleaseItemExclusive => Session Id: {0}, Session provider object: {1} => created new item in session.", id, this.GetHashCode());
                } // If update if lock matches
                else
                {
                    if (item != null && item.Items != null)
                    {
                        if (item.Items["SessionStateActions"] != null)
                        {
                            item.Items.Remove("SessionStateActions");
                        }
                        // Converting timout from min to sec
                        await cache.UpdateAndReleaseLockAsync(lockId, item.Items, (item.Timeout * FROM_MIN_TO_SEC));
                        LogUtility.LogInfo("SetAndReleaseItemExclusive => Session Id: {0}, Session provider object: {1} => updated item in session, Lock ID: {2}.", id, this.GetHashCode(), lockId);
                    }
                }
            }
            catch (Exception e)
            {
                LogUtility.LogError("SetAndReleaseItemExclusive => {0}", e.ToString());
                LastException = e;
                if (configuration.ThrowOnError)
                {
                    throw;
                }
            }
        }
    }
}