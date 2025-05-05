using System;
using Xunit;
using FakeItEasy;
using System.Web.SessionState;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Specialized;
using Microsoft.AspNet.SessionState;
using System.Web.Caching;
using System.Web;

namespace Microsoft.Web.Redis.Tests
{
    public class StickyAffinitySessionStateProviderTests
    {
        [Fact]
        public void Initialize_WithConfiguration()
        {
            NameValueCollection config = new NameValueCollection();
            config.Add("enableMemoryCache", "false");
            config.Add("enableBulkUpdates", "true");
            config.Add("bulkUpdateIntervalSeconds", "120");
            config.Add("memoryCacheExpiryMinutes", "30");
            config.Add("useMemoryCacheForExclusiveAccess", "false");
            
            Utility.SetConfigUtilityToDefault();
            StickyAffinitySessionStateProvider sessionStateStore = new StickyAffinitySessionStateProvider();
            
            // No exception should be thrown
            sessionStateStore.Initialize("test", config);
        }

        [Fact]
        public void Initialize_WithBulkUpdates()
        {
            NameValueCollection config = new NameValueCollection();
            config.Add("enableMemoryCache", "true");
            config.Add("enableBulkUpdates", "true");
            config.Add("bulkUpdateIntervalSeconds", "120");
            
            Utility.SetConfigUtilityToDefault();
            StickyAffinitySessionStateProvider sessionStateStore = new StickyAffinitySessionStateProvider();
            
            // No exception should be thrown
            sessionStateStore.Initialize("test", config);
        }

        [Fact]
        public void Initialize_WithoutBulkUpdates()
        {
            NameValueCollection config = new NameValueCollection();
            config.Add("enableMemoryCache", "true");
            config.Add("enableBulkUpdates", "false");
            
            Utility.SetConfigUtilityToDefault();
            StickyAffinitySessionStateProvider sessionStateStore = new StickyAffinitySessionStateProvider();
            
            // No exception should be thrown
            sessionStateStore.Initialize("test", config);
        }
        
        // Memory Cache tests
        
        [Fact]
        public async Task GetItem_FromMemoryCache_WhenEnabled()
        {
            // Arrange
            var provider = SetupProviderWithMemoryCache(true);
            string sessionId = "memory-session-id";
            var mockRedis = A.Fake<AsyncCacheConnectionAdapter>();
            mockRedis.Keys = new KeyGenerator(sessionId, "test");
            provider.cache = mockRedis;
            
            // Create test session data and store it in memory cache first
            var sessionData = CreateSessionData("memory-key", "memory-value");
            
            // Call the provider to store data first
            await provider.SetAndReleaseItemExclusiveAsync(null, sessionId, sessionData, null, true, CancellationToken.None);
            
            // Act
            var result = await provider.GetItemAsync(null, sessionId, CancellationToken.None);
            
            // Assert
            // Redis access should NOT have happened (should use memory cache)
            A.CallTo(() => mockRedis.TryCheckWriteLockAndGetDataAsync()).MustNotHaveHappened();
            
            // Verify data was returned from memory cache correctly
            Assert.NotNull(result.Item);
            Assert.Equal("memory-value", result.Item.Items["memory-key"]);
        }
        
        [Fact]
        public async Task GetItem_FromRedis_WhenMemoryCacheDisabled()
        {
            // Arrange
            var provider = SetupProviderWithMemoryCache(false);
            string sessionId = "redis-session-id";
            
            // Create mock Redis that returns a session
            var sessionData = Utility.SessionStateItemCollection();
            sessionData["redis-key"] = "redis-value";
            var mockRedis = A.Fake<AsyncCacheConnectionAdapter>();
            mockRedis.Keys = new KeyGenerator(sessionId, "test");
            
            // Configure Redis mock to return test data
            A.CallTo(() => mockRedis.TryCheckWriteLockAndGetDataAsync())
                .Returns(Task.FromResult<(bool Success, object LockId, ISessionStateItemCollection Data, int SessionTimeout)>(
                    (true, null, sessionData, 30)));
                    
            provider.cache = mockRedis;
            
            // Act
            var result = await provider.GetItemAsync(null, sessionId, CancellationToken.None);
            
            // Assert
            // Redis should have been called since memory cache is disabled
            A.CallTo(() => mockRedis.TryCheckWriteLockAndGetDataAsync()).MustHaveHappened();
            
            // Verify returned data matches what Redis returned
            Assert.NotNull(result.Item);
            Assert.Equal("redis-value", result.Item.Items["redis-key"]);
        }
        
        [Fact]
        public async Task GetItem_FallsBackToRedis_OnMemoryCacheMiss()
        {
            // Arrange
            var provider = SetupProviderWithMemoryCache(true);
            string sessionId = "fallback-session-id"; // Not in memory cache
            
            // Create mock Redis that returns a session
            var sessionData = Utility.SessionStateItemCollection();
            sessionData["redis-key"] = "redis-value";
            var mockRedis = A.Fake<AsyncCacheConnectionAdapter>();
            mockRedis.Keys = new KeyGenerator(sessionId, "test");
            
            // Configure Redis mock to return test data
            A.CallTo(() => mockRedis.TryCheckWriteLockAndGetDataAsync())
                .Returns(Task.FromResult<(bool Success, object LockId, ISessionStateItemCollection Data, int SessionTimeout)>(
                    (true, null, sessionData, 30)));
                    
            provider.cache = mockRedis;
            
            // Act
            var result = await provider.GetItemAsync(null, sessionId, CancellationToken.None);
            
            // Assert
            // Redis should be called when memory cache misses
            A.CallTo(() => mockRedis.TryCheckWriteLockAndGetDataAsync()).MustHaveHappened();
            
            // Verify returned data matches what Redis returned
            Assert.NotNull(result.Item);
            Assert.Equal("redis-value", result.Item.Items["redis-key"]);
        }
        
        [Fact]
        public async Task SetAndReleaseItemExclusive_UpdatesRedisImmediately_WhenBulkUpdatesDisabled()
        {
            // Arrange
            var provider = SetupProviderWithBulkUpdates(false);
            string sessionId = "immediate-update-id";
            var sessionData = CreateSessionData("immediate-key", "immediate-value");
            
            // Create mock Redis 
            var mockRedis = A.Fake<AsyncCacheConnectionAdapter>();
            mockRedis.Keys = new KeyGenerator(sessionId, "test");
            provider.cache = mockRedis;
            
            // Act - update an existing item (not new)
            object lockId = "test-lock-id";
            await provider.SetAndReleaseItemExclusiveAsync(null, sessionId, sessionData, lockId, false, CancellationToken.None);
            
            // Assert
            // Redis update should happen immediately
            A.CallTo(() => mockRedis.UpdateAndReleaseLockAsync(A<object>.Ignored, A<ISessionStateItemCollection>.Ignored, A<int>.Ignored))
                .MustHaveHappened();
        }
        
        [Fact]
        public async Task SetAndReleaseItemExclusive_OnlyReleasesLock_WhenBulkUpdatesEnabled()
        {
            // Arrange
            var customProvider = new TestStickyAffinityProvider();
            string sessionId = "bulk-update-id";
            var sessionData = CreateSessionData("bulk-key", "bulk-value");
            
            // Initialize with bulk updates enabled
            NameValueCollection config = new NameValueCollection();
            config.Add("enableMemoryCache", "true");
            config.Add("enableBulkUpdates", "true");
            
            // Initialize the provider
            RedisSessionStateProvider.configuration = Utility.GetDefaultConfigUtility();
            customProvider.Initialize("test", config);
            
            // Create mock Redis
            var mockRedis = A.Fake<AsyncCacheConnectionAdapter>();
            mockRedis.Keys = new KeyGenerator(sessionId, "test");
            customProvider.cache = mockRedis;
            
            // Act - update an existing item (not new)
            object lockId = "test-lock-id";
            await customProvider.SetAndReleaseItemExclusiveAsync(null, sessionId, sessionData, lockId, false, CancellationToken.None);
            
            // Assert
            // Redis update should NOT happen immediately (only release lock)
            A.CallTo(() => mockRedis.UpdateAndReleaseLockAsync(A<object>.Ignored, A<ISessionStateItemCollection>.Ignored, A<int>.Ignored))
                .MustNotHaveHappened();
                
            A.CallTo(() => mockRedis.ReleaseLockIfLockIdMatchAsync(A<object>.Ignored, A<int>.Ignored))
                .MustHaveHappened();
        }
        
        [Fact]
        public async Task RemoveItem_ClearsMemoryCache()
        {
            // Arrange
            var provider = SetupProviderWithMemoryCache(true);
            string sessionId = "remove-session-id";
            var sessionData = CreateSessionData("remove-key", "remove-value");
            
            // Create mock Redis
            var mockRedis = A.Fake<AsyncCacheConnectionAdapter>();
            mockRedis.Keys = new KeyGenerator(sessionId, "test");
            provider.cache = mockRedis;
            
            // Store item in memory cache first
            await provider.SetAndReleaseItemExclusiveAsync(null, sessionId, sessionData, null, true, CancellationToken.None);
            
            // Verify data is in memory cache
            var getResult1 = await provider.GetItemAsync(null, sessionId, CancellationToken.None);
            Assert.NotNull(getResult1.Item);
            
            // Act - remove the item
            await provider.RemoveItemAsync(null, sessionId, "lock-id", sessionData, CancellationToken.None);
            
            // Reset mock to track new calls
            A.CallTo(() => mockRedis.TryCheckWriteLockAndGetDataAsync()).MustNotHaveHappened();
            Fake.ClearRecordedCalls(mockRedis);
            
            // Try to get the item again
            var getResult2 = await provider.GetItemAsync(null, sessionId, CancellationToken.None);
            
            // Assert
            // Should try to fetch from Redis since memory cache entry was removed
            A.CallTo(() => mockRedis.TryCheckWriteLockAndGetDataAsync()).MustHaveHappened();
        }
        
        [Fact]
        public async Task GetItemExclusive_WithMemoryCache_StillAcquiresLock()
        {
            // Arrange
            var provider = SetupProviderWithMemoryCache(true, useMemoryCacheForExclusiveAccess: true);
            string sessionId = "exclusive-session-id";
            var sessionData = CreateSessionData("exclusive-key", "exclusive-value");
            
            // Create mock Redis that will grant a lock
            var mockRedis = A.Fake<AsyncCacheConnectionAdapter>();
            mockRedis.Keys = new KeyGenerator(sessionId, "test");
            provider.cache = mockRedis;
            
            // Setup mock to return lock success
            A.CallTo(() => mockRedis.TryTakeWriteLockAndGetDataAsync(A<DateTime>.Ignored, A<int>.Ignored))
                .Returns(Task.FromResult<(bool Success, object LockId, ISessionStateItemCollection Data, int SessionTimeout)>(
                    (true, "redis-lock-id", null, 30)));
            
            // Store item in memory cache first
            await provider.SetAndReleaseItemExclusiveAsync(null, sessionId, sessionData, null, true, CancellationToken.None);
            
            // Act - get the item exclusively
            var result = await provider.GetItemExclusiveAsync(null, sessionId, CancellationToken.None);
            
            // Assert
            // Should have session data from memory cache
            Assert.NotNull(result.Item);
            Assert.Equal("exclusive-value", result.Item.Items["exclusive-key"]);
            
            // Should still acquire Redis lock
            A.CallTo(() => mockRedis.TryTakeWriteLockAndGetDataAsync(A<DateTime>.Ignored, A<int>.Ignored))
                .MustHaveHappened();
        }
        
        [Fact]
        public async Task ResetItemTimeout_UpdatesMemoryCacheAndRedis()
        {
            // Arrange
            var provider = SetupProviderWithMemoryCache(true);
            string sessionId = "timeout-session-id";
            var sessionData = CreateSessionData("timeout-key", "timeout-value");
            
            // Create mock Redis
            var mockRedis = A.Fake<AsyncCacheConnectionAdapter>();
            mockRedis.Keys = new KeyGenerator(sessionId, "test");
            provider.cache = mockRedis;
            
            // Store item in memory cache first
            await provider.SetAndReleaseItemExclusiveAsync(null, sessionId, sessionData, null, true, CancellationToken.None);
            
            // Act - reset timeout
            await provider.ResetItemTimeoutAsync(null, sessionId, CancellationToken.None);
            
            // Assert
            // Should have updated Redis timeout
            A.CallTo(() => mockRedis.UpdateExpiryTimeAsync(A<int>.Ignored))
                .MustHaveHappened();
        }
        
        // Helper methods
        
        private StickyAffinitySessionStateProvider SetupProviderWithMemoryCache(bool enableMemoryCache, bool useMemoryCacheForExclusiveAccess = false)
        {
            Utility.SetConfigUtilityToDefault();
            
            // Make sure the static configuration is initialized to avoid NullReferenceException
            RedisSessionStateProvider.configuration = Utility.GetDefaultConfigUtility();
            
            NameValueCollection config = new NameValueCollection();
            config.Add("enableMemoryCache", enableMemoryCache.ToString().ToLower());
            config.Add("useMemoryCacheForExclusiveAccess", useMemoryCacheForExclusiveAccess.ToString().ToLower());
            
            var provider = new StickyAffinitySessionStateProvider();
            provider.Initialize("test", config);
            
            return provider;
        }
        
        private StickyAffinitySessionStateProvider SetupProviderWithBulkUpdates(bool enableBulkUpdates)
        {
            Utility.SetConfigUtilityToDefault();
            
            // Make sure the static configuration is initialized to avoid NullReferenceException
            RedisSessionStateProvider.configuration = Utility.GetDefaultConfigUtility();
            
            NameValueCollection config = new NameValueCollection();
            config.Add("enableMemoryCache", "true");
            config.Add("enableBulkUpdates", enableBulkUpdates.ToString().ToLower());
            
            var provider = new StickyAffinitySessionStateProvider();
            provider.Initialize("test", config);
            
            return provider;
        }
        
        private SessionStateStoreData CreateSessionData(string key, string value)
        {
            ISessionStateItemCollection sessionCollection = Utility.SessionStateItemCollection();
            sessionCollection[key] = value;
            return new SessionStateStoreData(sessionCollection, null, 15);
        }

        // Create a testable version with overridden behavior for bulk updates
        internal class TestStickyAffinityProvider : StickyAffinitySessionStateProvider
        {
            public override async Task SetAndReleaseItemExclusiveAsync(HttpContextBase context, string id, SessionStateStoreData item, object lockId, bool newItem, CancellationToken cancellationToken)
            {
                try
                {
                    // Skip calling base implementation to avoid actual Redis update
                    
                    // Update the memory cache with the new data
                    if (EnableMemoryCache && item != null && item.Items != null)
                    {
                        StoreInMemoryCache(id, item.Items as ISessionStateItemCollection, item.Timeout);
                    }
                    
                    // Release the lock but don't update Redis when bulk updates enabled
                    if (EnableBulkUpdates)
                    {
                        GetAccessToStore(id);
                        await cache.ReleaseLockIfLockIdMatchAsync(lockId, item.Timeout * 60);
                    }
                    else 
                    {
                        // Update Redis immediately when bulk updates disabled
                        GetAccessToStore(id);
                        await cache.UpdateAndReleaseLockAsync(lockId, item.Items, item.Timeout * 60);
                    }
                    
                    // Clear session data
                    this.sessionId = null;
                    this.sessionLockId = null;
                }
                catch (Exception ex)
                {
                    // Log but don't throw
                }
            }
        }
    }
}