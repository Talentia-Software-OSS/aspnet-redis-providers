//
// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
//

using Microsoft.Web.Redis.Tests;
using StackExchange.Redis;
using System;
using System.Threading.Tasks;
using Xunit;
using System.Threading;
using Microsoft.AspNet.SessionState;

namespace Microsoft.Web.Redis.FunctionalTests
{
    public class RedisSessionStateProviderFunctionalTests
    {
        // Use a shared RedisServer instance across all tests
        private static readonly RedisServer sharedRedisServer = new RedisServer();
        private static readonly object lockForConfiguration = new object();

        private string SetupRedisSessionStateProvider()
        {
            // We don't call CleanupConnection() anymore to allow test parallelization
            // Instead, we just make sure the configuration is set
            lock (lockForConfiguration)
            {
                RedisSessionStateProvider.configuration = Utility.GetDefaultConfigUtility();
            }
            return Guid.NewGuid().ToString();
        }

        private void CleanupResources(RedisConnectionWrapper connectionWrapper)
        {
            // Release the connection wrapper rather than cleaning it up
            if (connectionWrapper != null)
            {
                connectionWrapper.Release();
            }
        }

        [Fact()]
        public async Task SessionWriteCycle_Valid()
        {
            string sessionId = SetupRedisSessionStateProvider();

            // Inserting empty session with "SessionStateActions.InitializeItem" flag into redis server
            RedisSessionStateProvider ssp = new RedisSessionStateProvider();
            await ssp.CreateUninitializedItemAsync(null, sessionId, (int)RedisSessionStateProvider.configuration.SessionTimeout.TotalMinutes, CancellationToken.None);

            // Get write lock and session from cache
            GetItemResult data = await ssp.GetItemExclusiveAsync(null, sessionId, CancellationToken.None);

            // Get actual connection and verify lock and session timeout
            IDatabase actualConnection = GetRealRedisConnection();
            Assert.Equal(data.LockId.ToString(), actualConnection.StringGet(ssp.cache.Keys.LockKey).ToString());
            Assert.Equal(((int)RedisSessionStateProvider.configuration.SessionTimeout.TotalSeconds).ToString(), actualConnection.StringGet(ssp.cache.Keys.InternalKey).ToString());

            // setting data as done by any normal session operation
            data.Item.Items["key"] = "value";

            // session update
            await ssp.SetAndReleaseItemExclusiveAsync(null, sessionId, data.Item, data.LockId, false, CancellationToken.None);
            Assert.False(actualConnection.StringGet(ssp.cache.Keys.DataKey).IsNull);

            // reset sessions timeout
            await ssp.ResetItemTimeoutAsync(null, sessionId, CancellationToken.None);

            // End request
            await ssp.EndRequestAsync(null);

            // Clean up resources
            CleanupResources(ssp.cache as RedisConnectionWrapper);
        }

        [Fact()]
        public async Task SessionReadCycle_Valid()
        {
            string sessionId = SetupRedisSessionStateProvider();

            // Inserting empty session with "SessionStateActions.InitializeItem" flag into redis server
            RedisSessionStateProvider ssp = new RedisSessionStateProvider();
            await ssp.CreateUninitializedItemAsync(null, sessionId, (int)RedisSessionStateProvider.configuration.SessionTimeout.TotalMinutes, CancellationToken.None);

            // Get write lock and session from cache
            GetItemResult data = await ssp.GetItemAsync(null, sessionId, CancellationToken.None);

            // Get actual connection and verify lock and session timeout
            IDatabase actualConnection = GetRealRedisConnection();
            Assert.True(actualConnection.StringGet(ssp.cache.Keys.LockKey).IsNull);
            Assert.Equal(((int)RedisSessionStateProvider.configuration.SessionTimeout.TotalSeconds).ToString(), actualConnection.StringGet(ssp.cache.Keys.InternalKey).ToString());

            // reset sessions timeout
            await ssp.ResetItemTimeoutAsync(null, sessionId, CancellationToken.None);

            // End request
            await ssp.EndRequestAsync(null);

            // Clean up resources
            CleanupResources(ssp.cache as RedisConnectionWrapper);
        }

        [Fact()]
        public async Task SessionTimoutChangeFromGlobalAspx()
        {
            string sessionId = SetupRedisSessionStateProvider();

            // Inserting empty session with "SessionStateActions.InitializeItem" flag into redis server
            RedisSessionStateProvider ssp = new RedisSessionStateProvider();
            await ssp.CreateUninitializedItemAsync(null, sessionId, (int)RedisSessionStateProvider.configuration.SessionTimeout.TotalMinutes, CancellationToken.None);

            // Get write lock and session from cache
            GetItemResult data = await ssp.GetItemExclusiveAsync(null, sessionId, CancellationToken.None);

            // Get actual connection and varify lock and session timeout
            IDatabase actualConnection = GetRealRedisConnection();
            Assert.Equal(data.LockId.ToString(), actualConnection.StringGet(ssp.cache.Keys.LockKey).ToString());
            Assert.Equal(((int)RedisSessionStateProvider.configuration.SessionTimeout.TotalSeconds).ToString(), actualConnection.StringGet(ssp.cache.Keys.InternalKey).ToString());

            // setting data as done by any normal session operation
            data.Item.Items["key"] = "value";
            data.Item.Timeout = 5;

            // session update
            await ssp.SetAndReleaseItemExclusiveAsync(null, sessionId, data.Item, data.LockId, false, CancellationToken.None);
            Assert.Equal("300", actualConnection.StringGet(ssp.cache.Keys.InternalKey).ToString());

            // reset sessions timoue
            await ssp.ResetItemTimeoutAsync(null, sessionId, CancellationToken.None);

            // End request
            await ssp.EndRequestAsync(null);

            // Verify that GetItemExclusive returns timeout from redis
            GetItemResult data_1 = await ssp.GetItemExclusiveAsync(null, sessionId, CancellationToken.None);
            Assert.Equal(5, data.Item.Timeout);

            // Clean up resources
            CleanupResources(ssp.cache as RedisConnectionWrapper);
        }

        [Fact()]
        public async Task ReleaseItemExclusiveWithNullLockId()
        {
            string sessionId = SetupRedisSessionStateProvider();
            RedisSessionStateProvider ssp = new RedisSessionStateProvider();
            await ssp.ReleaseItemExclusiveAsync(null, sessionId, null, CancellationToken.None);
            CleanupResources(ssp.cache as RedisConnectionWrapper);
        }

        [Fact()]
        public async Task RemoveItemWithNullLockId()
        {
            string sessionId = SetupRedisSessionStateProvider();
            RedisSessionStateProvider ssp = new RedisSessionStateProvider();
            await ssp.RemoveItemAsync(null, sessionId, null, null, CancellationToken.None);
            CleanupResources(ssp.cache as RedisConnectionWrapper);
        }

        private IDatabase GetRealRedisConnection()
        {
            return RedisConnectionWrapper.sharedConnection.Connection;
        }

        [Fact(Skip = "Only used to evaluate performance")]
        public async Task TestThroughputAsync()
        {
            // Test to compare efficiency between code changes; reads and writes 10000 items to Redis
            string sessionId = SetupRedisSessionStateProvider();

            // Inserting empty session with "SessionStateActions.InitializeItem" flag into redis server
            RedisSessionStateProvider ssp = new RedisSessionStateProvider();
            await ssp.CreateUninitializedItemAsync(null, sessionId, (int)RedisSessionStateProvider.configuration.SessionTimeout.TotalMinutes, CancellationToken.None);

            // Get write lock and session from cache
            GetItemResult data = await ssp.GetItemExclusiveAsync(null, sessionId, CancellationToken.None);

            // Get actual connection and varify lock and session timeout
            IDatabase actualConnection = GetRealRedisConnection();
            Assert.Equal(data.LockId.ToString(), actualConnection.StringGet(ssp.cache.Keys.LockKey).ToString());
            Assert.Equal(((int)RedisSessionStateProvider.configuration.SessionTimeout.TotalSeconds).ToString(), actualConnection.HashGet(ssp.cache.Keys.InternalKey, "SessionTimeout").ToString());

            var watch = new System.Diagnostics.Stopwatch();

            watch.Start();

            for (int i = 0; i < 10000; i++)
            {
                data.Item.Items["key" + i.ToString()] = "value" + i.ToString();

                // session update
                await ssp.SetAndReleaseItemExclusiveAsync(null, sessionId, data.Item, data.LockId, false, CancellationToken.None);
            }

            for (int i = 0; i < 10000; i++)
            {
                var result = data.Item.Items["key" + i.ToString()];
                Assert.Equal("value" + i.ToString(), result);
            }

            watch.Stop();

            Console.WriteLine($"Execution Time: {watch.ElapsedMilliseconds} ms");

            // reset sessions timoue
            await ssp.ResetItemTimeoutAsync(null, sessionId, CancellationToken.None);

            // End request
            await ssp.EndRequestAsync(null);

            // Clean up resources
            CleanupResources(ssp.cache as RedisConnectionWrapper);
        }
    }
}