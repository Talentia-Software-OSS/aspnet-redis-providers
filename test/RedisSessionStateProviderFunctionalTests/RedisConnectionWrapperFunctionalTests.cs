//
// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
//

using System;
using System.IO;
using System.Web.SessionState;
using Microsoft.Web.Redis.Tests;
using StackExchange.Redis;
using Xunit;
using System.Threading.Tasks;

namespace Microsoft.Web.Redis.FunctionalTests
{
    public class RedisConnectionWrapperFunctionalTests
    {
        private static int uniqueSessionNumber = 1;
        private static readonly object lockForUniqueSession = new object();

        // Use a shared RedisServer instance across all tests
        private static readonly RedisServer sharedRedisServer = new RedisServer();

        private RedisConnectionWrapper GetRedisConnectionWrapperWithUniqueSession()
        {
            return GetRedisConnectionWrapperWithUniqueSession(Utility.GetDefaultConfigUtility());
        }

        private RedisConnectionWrapper GetRedisConnectionWrapperWithUniqueSession(ProviderConfiguration pc)
        {
            string id;
            
            // Thread-safe increment of uniqueSessionNumber
            lock (lockForUniqueSession)
            {
                id = Guid.NewGuid().ToString();
                uniqueSessionNumber++;
            }
            
            // We no longer clean the connection before creating a new one
            // This allows tests to run in parallel and share the same connection
            RedisConnectionWrapper redisConn = new RedisConnectionWrapper(pc, id);
            return redisConn;
        }

        private void DisposeRedisConnectionWrapper(RedisConnectionWrapper redisConn)
        {
            redisConn.Release();
        }

        [Fact()]
        public async Task Set_ValidData_WithCustomSerializer()
        {
            // this also tests host:port config part
            ProviderConfiguration pc = Utility.GetDefaultConfigUtility();
            pc.ApplicationName = "APPTEST";
            pc.Port = 6379;

            using (RedisServer redisServer = new RedisServer())
            {
                RedisConnectionWrapper redisConn = GetRedisConnectionWrapperWithUniqueSession(pc);

                // Inserting data into redis server
                SessionStateItemCollection data = new SessionStateItemCollection();
                data["key"] = "value";
                data["key1"] = "value1";
                await redisConn.SetAsync(data, 900);

                // Get actual connection and get data blob from redis
                IDatabase actualConnection = GetRealRedisConnection(redisConn);

                RedisValue sessionDataFromRedis = actualConnection.StringGet(redisConn.Keys.DataKey);
                SessionStateItemCollection dataFromRedis = null;
                MemoryStream ms = new MemoryStream(sessionDataFromRedis);
                BinaryReader reader = new BinaryReader(ms);
                dataFromRedis = SessionStateItemCollection.Deserialize(reader);

                Assert.Equal("value", dataFromRedis["key"]);
                Assert.Equal("value1", dataFromRedis["key1"]);

                // remove data from redis
                actualConnection.KeyDelete(redisConn.Keys.DataKey);
                DisposeRedisConnectionWrapper(redisConn);
            }
        }

        [Fact()]
        public async Task Set_ValidData()
        {
            // this also tests host:port config part
            ProviderConfiguration pc = Utility.GetDefaultConfigUtility();
            pc.ApplicationName = "APPTEST";
            pc.Port = 6379;

            using (RedisServer redisServer = new RedisServer())
            {
                RedisConnectionWrapper redisConn = GetRedisConnectionWrapperWithUniqueSession(pc);

                // Inserting data into redis server
                SessionStateItemCollection data = new SessionStateItemCollection();
                data["key"] = "value";
                data["key1"] = "value1";
                await redisConn.SetAsync(data, 900);

                // Get actual connection and get data blob from redis
                IDatabase actualConnection = GetRealRedisConnection(redisConn);

                RedisValue sessionDataFromRedis = actualConnection.StringGet(redisConn.Keys.DataKey);
                SessionStateItemCollection dataFromRedis = null;
                MemoryStream ms = new MemoryStream(sessionDataFromRedis);
                BinaryReader reader = new BinaryReader(ms);
                dataFromRedis = SessionStateItemCollection.Deserialize(reader);

                Assert.Equal("value", dataFromRedis["key"]);
                Assert.Equal("value1", dataFromRedis["key1"]);

                // remove data from redis
                actualConnection.KeyDelete(redisConn.Keys.DataKey);
                DisposeRedisConnectionWrapper(redisConn);
            }
        }

        [Fact()]
        public async Task Set_NullData()
        {
            // this also tests host:port config part
            ProviderConfiguration pc = Utility.GetDefaultConfigUtility();
            pc.ApplicationName = "APPTEST";
            pc.Port = 6379;

            using (RedisServer redisServer = new RedisServer())
            {
                RedisConnectionWrapper redisConn = GetRedisConnectionWrapperWithUniqueSession(pc);

                // Inserting data into redis server
                SessionStateItemCollection data = new SessionStateItemCollection();
                data["key"] = "value";
                data["key1"] = null;
                await redisConn.SetAsync(data, 900);

                // Get actual connection and get data blob from redis
                IDatabase actualConnection = GetRealRedisConnection(redisConn);

                RedisValue sessionDataFromRedis = actualConnection.StringGet(redisConn.Keys.DataKey);
                SessionStateItemCollection dataFromRedis = null;
                MemoryStream ms = new MemoryStream(sessionDataFromRedis);
                BinaryReader reader = new BinaryReader(ms);
                dataFromRedis = SessionStateItemCollection.Deserialize(reader);

                Assert.Equal("value", dataFromRedis["key"]);
                Assert.Null(dataFromRedis["key1"]);

                // remove data from redis
                actualConnection.KeyDelete(redisConn.Keys.DataKey);
                DisposeRedisConnectionWrapper(redisConn);
            }
        }

        [Fact()]
        public async Task Set_ExpireData()
        {
            ProviderConfiguration pc = Utility.GetDefaultConfigUtility();
            using (RedisServer redisServer = new RedisServer())
            {
                RedisConnectionWrapper redisConn = GetRedisConnectionWrapperWithUniqueSession();
                // Inserting data into redis server that expires after 1 second
                SessionStateItemCollection data = new SessionStateItemCollection();
                data["key"] = "value";
                await redisConn.SetAsync(data, 1);

                // Wait for 2 seconds so that data will expire
                System.Threading.Thread.Sleep(1100);

                // Get actual connection and get data blob from redis
                IDatabase actualConnection = GetRealRedisConnection(redisConn);
                HashEntry[] sessionDataFromRedis = actualConnection.HashGetAll(redisConn.Keys.DataKey);

                // Check that data shoud not be there
                Assert.Empty(sessionDataFromRedis);
                DisposeRedisConnectionWrapper(redisConn);
            }
        }

        [Fact()]
        public async Task TryTakeWriteLockAndGetData_WithNullData()
        {
            ProviderConfiguration pc = Utility.GetDefaultConfigUtility();
            using (RedisServer redisServer = new RedisServer())
            {
                RedisConnectionWrapper redisConn = GetRedisConnectionWrapperWithUniqueSession();

                // Inserting data into redis server
                SessionStateItemCollection data = new SessionStateItemCollection();
                data["key"] = null;
                await redisConn.SetAsync(data, 900);

                DateTime lockTime = DateTime.Now;
                int lockTimeout = 900;
                var result = await redisConn.TryTakeWriteLockAndGetDataAsync(lockTime, lockTimeout);
                Assert.True(result.Success);
                Assert.Equal(lockTime.Ticks.ToString(), result.LockId.ToString());
                Assert.Single(result.Data);
                Assert.Null(result.Data["key"]);

                // Get actual connection and get data lock from redis
                IDatabase actualConnection = GetRealRedisConnection(redisConn);
                string lockValueFromRedis = actualConnection.StringGet(redisConn.Keys.LockKey);
                Assert.Equal(lockTime.Ticks.ToString(), lockValueFromRedis);

                // remove data and lock from redis
                actualConnection.KeyDelete(redisConn.Keys.DataKey);
                actualConnection.KeyDelete(redisConn.Keys.LockKey);
                DisposeRedisConnectionWrapper(redisConn);
            }
        }

        [Fact()]
        public async Task TryTakeWriteLockAndGetData_WriteLockWithoutAnyOtherLock()
        {
            ProviderConfiguration pc = Utility.GetDefaultConfigUtility();
            using (RedisServer redisServer = new RedisServer())
            {
                RedisConnectionWrapper redisConn = GetRedisConnectionWrapperWithUniqueSession();

                // Inserting data into redis server
                SessionStateItemCollection data = new SessionStateItemCollection();
                data["key"] = "value";
                await redisConn.SetAsync(data, 900);

                DateTime lockTime = DateTime.Now;
                int lockTimeout = 900;
                var result = await redisConn.TryTakeWriteLockAndGetDataAsync(lockTime, lockTimeout);
                Assert.True(result.Success);
                Assert.Equal(lockTime.Ticks.ToString(), result.LockId.ToString());
                Assert.Single(result.Data);
                Assert.Equal("value", result.Data["key"]);

                // Get actual connection and get data lock from redis
                IDatabase actualConnection = GetRealRedisConnection(redisConn);
                string lockValueFromRedis = actualConnection.StringGet(redisConn.Keys.LockKey);
                Assert.Equal(lockTime.Ticks.ToString(), lockValueFromRedis);

                // remove data and lock from redis
                actualConnection.KeyDelete(redisConn.Keys.DataKey);
                actualConnection.KeyDelete(redisConn.Keys.LockKey);
                DisposeRedisConnectionWrapper(redisConn);
            }
        }

        [Fact()]
        public async Task TryTakeWriteLockAndGetData_WriteLockWithOtherWriteLock()
        {
            ProviderConfiguration pc = Utility.GetDefaultConfigUtility();
            using (RedisServer redisServer = new RedisServer())
            {
                RedisConnectionWrapper redisConn = GetRedisConnectionWrapperWithUniqueSession();

                // Inserting data into redis server
                SessionStateItemCollection data = new SessionStateItemCollection();
                data["key"] = "value";
                await redisConn.SetAsync(data, 900);

                // Mannually add lock to redis
                DateTime lockTime = DateTime.Now;
                DateTime newlockTime = DateTime.Now.AddSeconds(1);
                IDatabase actualConnection = GetRealRedisConnection(redisConn);
                actualConnection.StringSet(redisConn.Keys.LockKey, DateTime.Now.Ticks.ToString());
                int lockTimeout = 900;
                var result = await redisConn.TryTakeWriteLockAndGetDataAsync(newlockTime, lockTimeout);
                Assert.False(result.Success);
                Assert.NotNull(result.LockId);
                Assert.Null(result.Data);

                // Verify lock remains unchanged
                string lockValueFromRedis = actualConnection.StringGet(redisConn.Keys.LockKey);
                Assert.NotEqual(newlockTime.Ticks.ToString(), lockValueFromRedis);

                // remove data and lock from redis
                actualConnection.KeyDelete(redisConn.Keys.DataKey);
                actualConnection.KeyDelete(redisConn.Keys.LockKey);
                DisposeRedisConnectionWrapper(redisConn);
            }
        }

        [Fact()]
        public async Task TryTakeWriteLockAndGetData_WriteLockWithOtherWriteLockWithSameLockId()
        {
            ProviderConfiguration pc = Utility.GetDefaultConfigUtility();
            using (RedisServer redisServer = new RedisServer())
            {
                RedisConnectionWrapper redisConn = GetRedisConnectionWrapperWithUniqueSession();

                // Inserting data into redis server
                SessionStateItemCollection data = new SessionStateItemCollection();
                data["key"] = "value";
                await redisConn.SetAsync(data, 900);

                // Mannually add lock to redis
                DateTime lockTime = DateTime.Now;
                IDatabase actualConnection = GetRealRedisConnection(redisConn);
                actualConnection.StringSet(redisConn.Keys.LockKey, lockTime.Ticks.ToString());
                int lockTimeout = 900;
                var result = await redisConn.TryTakeWriteLockAndGetDataAsync(lockTime, lockTimeout);
                Assert.False(result.Success);
                Assert.Equal(lockTime.Ticks.ToString(), result.LockId.ToString());
                Assert.Null(result.Data);

                // Verify lock remains unchanged
                string lockValueFromRedis = actualConnection.StringGet(redisConn.Keys.LockKey);
                Assert.Equal(lockTime.Ticks.ToString(), lockValueFromRedis);

                // remove data and lock from redis
                actualConnection.KeyDelete(redisConn.Keys.DataKey);
                actualConnection.KeyDelete(redisConn.Keys.LockKey);
                DisposeRedisConnectionWrapper(redisConn);
            }
        }

        [Fact()]
        public async Task TryCheckWriteLockAndGetData_WithoutAnyLock()
        {
            ProviderConfiguration pc = Utility.GetDefaultConfigUtility();
            using (RedisServer redisServer = new RedisServer())
            {
                RedisConnectionWrapper redisConn = GetRedisConnectionWrapperWithUniqueSession();

                // Inserting data into redis server
                SessionStateItemCollection data = new SessionStateItemCollection();
                data["key"] = "value";
                await redisConn.SetAsync(data, 900);

                var result = await redisConn.TryCheckWriteLockAndGetDataAsync();
                Assert.True(result.Success);
                Assert.Null(result.LockId);
                Assert.Single(result.Data);
                Assert.Equal("value", result.Data["key"]);

                // remove data from redis
                IDatabase actualConnection = GetRealRedisConnection(redisConn);
                actualConnection.KeyDelete(redisConn.Keys.DataKey);
                DisposeRedisConnectionWrapper(redisConn);
            }
        }

        [Fact()]
        public async Task TryTakeReadLockAndGetData_WithoutAnyLock()
        {
            ProviderConfiguration pc = Utility.GetDefaultConfigUtility();
            using (RedisServer redisServer = new RedisServer())
            {
                RedisConnectionWrapper redisConn = GetRedisConnectionWrapperWithUniqueSession();

                // Inserting data into redis server
                SessionStateItemCollection data = new SessionStateItemCollection();
                data["key"] = "value";
                await redisConn.SetAsync(data, 900);

                var result = await redisConn.TryCheckWriteLockAndGetDataAsync();
                Assert.True(result.Success);
                Assert.Null(result.LockId);
                Assert.Single(result.Data);
                Assert.Equal("value", result.Data["key"]);

                // Get actual connection
                // remove data from redis
                IDatabase actualConnection = GetRealRedisConnection(redisConn);
                actualConnection.KeyDelete(redisConn.Keys.DataKey);
                DisposeRedisConnectionWrapper(redisConn);
            }
        }

        [Fact()]
        public async Task TryTakeReadLockAndGetData_WithOtherWriteLock()
        {
            ProviderConfiguration pc = Utility.GetDefaultConfigUtility();
            using (RedisServer redisServer = new RedisServer())
            {
                RedisConnectionWrapper redisConn = GetRedisConnectionWrapperWithUniqueSession();

                // Inserting data into redis server
                SessionStateItemCollection data = new SessionStateItemCollection();
                data["key"] = "value";
                await redisConn.SetAsync(data, 900);

                int lockTimeout = 900;

                DateTime lockTime_1 = DateTime.Now;
                var result1 = await redisConn.TryTakeWriteLockAndGetDataAsync(lockTime_1, lockTimeout);
                Assert.True(result1.Success);
                Assert.Equal(lockTime_1.Ticks.ToString(), result1.LockId.ToString());
                Assert.Single(result1.Data);

                var result2 = await redisConn.TryCheckWriteLockAndGetDataAsync();
                Assert.False(result2.Success);
                Assert.Equal(lockTime_1.Ticks.ToString(), result2.LockId.ToString());
                Assert.Null(result2.Data);

                // Get actual connection
                // remove data and lock from redis
                IDatabase actualConnection = GetRealRedisConnection(redisConn);
                actualConnection.KeyDelete(redisConn.Keys.DataKey);
                actualConnection.KeyDelete(redisConn.Keys.LockKey);
                DisposeRedisConnectionWrapper(redisConn);
            }
        }

        [Fact()]
        public async Task TryTakeWriteLockAndGetData_ExpireWriteLock()
        {
            ProviderConfiguration pc = Utility.GetDefaultConfigUtility();
            // Set a configuration without RetryTimeout and RetryInterval parameters
            using (RedisServer redisServer = new RedisServer())
            {
                RedisConnectionWrapper redisConn = GetRedisConnectionWrapperWithUniqueSession(pc);

                // Inserting data into redis server
                SessionStateItemCollection data = new SessionStateItemCollection();
                data["key"] = "value";
                await redisConn.SetAsync(data, 900);

                // Manually add lock to redis that expires after 300 milliseconds
                DateTime lockTime = DateTime.Now;
                DateTime newlockTime = DateTime.Now.AddSeconds(1);
                IDatabase actualConnection = GetRealRedisConnection(redisConn);
                actualConnection.StringSet(redisConn.Keys.LockKey, lockTime.Ticks.ToString(), TimeSpan.FromMilliseconds(300));
                int lockTimeout = 900;

                // Wait for lock to expire
                System.Threading.Thread.Sleep(400);

                var result = await redisConn.TryTakeWriteLockAndGetDataAsync(newlockTime, lockTimeout);
                Assert.True(result.Success);
                Assert.Equal(newlockTime.Ticks.ToString(), result.LockId.ToString());
                Assert.Single(result.Data);
                Assert.Equal("value", result.Data["key"]);

                // Get actual connection and get data lock from redis
                string lockValueFromRedis = actualConnection.StringGet(redisConn.Keys.LockKey);
                Assert.Equal(newlockTime.Ticks.ToString(), lockValueFromRedis);

                // remove data and lock from redis
                actualConnection.KeyDelete(redisConn.Keys.DataKey);
                actualConnection.KeyDelete(redisConn.Keys.LockKey);
                DisposeRedisConnectionWrapper(redisConn);
            }
        }

        [Fact()]
        public async Task TryReleaseLockIfLockIdMatch_ValidWriteLockRelease()
        {
            ProviderConfiguration pc = Utility.GetDefaultConfigUtility();
            using (RedisServer redisServer = new RedisServer())
            {
                RedisConnectionWrapper redisConn = GetRedisConnectionWrapperWithUniqueSession();

                // Inserting data into redis server
                SessionStateItemCollection data = new SessionStateItemCollection();
                data["key"] = "value";
                await redisConn.SetAsync(data, 900);

                // Mannually add lock to redis
                DateTime lockTime = DateTime.Now;
                IDatabase actualConnection = GetRealRedisConnection(redisConn);
                actualConnection.StringSet(redisConn.Keys.LockKey, lockTime.Ticks.ToString());

                int lockTimeout = 900;

                // Release Lock
                await redisConn.ReleaseLockIfLockIdMatchAsync(lockTime.Ticks, lockTimeout);

                var lockValueFromRedis = actualConnection.StringGet(redisConn.Keys.LockKey);
                Assert.True(lockValueFromRedis.IsNull);

                // remove data from redis
                actualConnection.KeyDelete(redisConn.Keys.DataKey);
                DisposeRedisConnectionWrapper(redisConn);
            }
        }

        [Fact()]
        public async Task TryReleaseLockIfLockIdMatch_InvalidWriteLockRelease()
        {
            ProviderConfiguration pc = Utility.GetDefaultConfigUtility();
            using (RedisServer redisServer = new RedisServer())
            {
                RedisConnectionWrapper redisConn = GetRedisConnectionWrapperWithUniqueSession();

                // Inserting data into redis server
                SessionStateItemCollection data = new SessionStateItemCollection();
                data["key"] = "value";
                await redisConn.SetAsync(data, 900);

                // Mannually add lock to redis
                DateTime lockTime = DateTime.Now;
                IDatabase actualConnection = GetRealRedisConnection(redisConn);
                actualConnection.StringSet(redisConn.Keys.LockKey, lockTime.Ticks.ToString());

                int lockTimeout = 900;

                // Release Lock with invalid Lock id
                await redisConn.ReleaseLockIfLockIdMatchAsync(lockTime.AddMinutes(1).Ticks, lockTimeout);

                var lockValueFromRedis = actualConnection.StringGet(redisConn.Keys.LockKey);
                Assert.Equal(lockTime.Ticks.ToString(), lockValueFromRedis);

                // remove data and lock from redis
                actualConnection.KeyDelete(redisConn.Keys.DataKey);
                actualConnection.KeyDelete(redisConn.Keys.LockKey);
                DisposeRedisConnectionWrapper(redisConn);
            }
        }

        [Fact()]
        public async Task TryRemoveIfLockIdMatch_ValidLockIdAndRemove()
        {
            ProviderConfiguration pc = Utility.GetDefaultConfigUtility();
            using (RedisServer redisServer = new RedisServer())
            {
                RedisConnectionWrapper redisConn = GetRedisConnectionWrapperWithUniqueSession();

                // Inserting data into redis server
                SessionStateItemCollection data = new SessionStateItemCollection();
                data["key"] = "value";
                await redisConn.SetAsync(data, 900);

                // Mannually add lock to redis
                DateTime lockTime = DateTime.Now;
                IDatabase actualConnection = GetRealRedisConnection(redisConn);
                actualConnection.StringSet(redisConn.Keys.LockKey, lockTime.Ticks.ToString());

                // Remove
                await redisConn.RemoveAndReleaseLockAsync(lockTime.Ticks);

                var lockValueFromRedis = actualConnection.StringGet(redisConn.Keys.LockKey);
                Assert.True(lockValueFromRedis.IsNull);

                var sessionDataFromRedis = actualConnection.StringGet(redisConn.Keys.DataKey);
                Assert.True(sessionDataFromRedis.IsNull);

                DisposeRedisConnectionWrapper(redisConn);
            }
        }

        [Fact()]
        public async Task TryUpdateIfLockIdMatch_WithValidUpdateAndDelete()
        {
            ProviderConfiguration pc = Utility.GetDefaultConfigUtility();
            using (RedisServer redisServer = new RedisServer())
            {
                RedisConnectionWrapper redisConn = GetRedisConnectionWrapperWithUniqueSession();

                // Inserting data into redis server
                SessionStateItemCollection data1 = new SessionStateItemCollection();
                data1["key1"] = "value1";
                data1["key2"] = "value2";
                data1["key3"] = "value3";
                await redisConn.SetAsync(data1, 900);

                // Mannually add lock to redis
                DateTime lockTime = DateTime.Now;
                IDatabase actualConnection = GetRealRedisConnection(redisConn);
                actualConnection.StringSet(redisConn.Keys.LockKey, lockTime.Ticks.ToString());

                // Update
                SessionStateItemCollection data2 = new SessionStateItemCollection();
                data2["key1"] = "new-value1";
                data2["key2"] = "value2";
                data2.Remove("key3");
                await redisConn.UpdateAndReleaseLockAsync(lockTime.Ticks, data2, 900);

                var lockValueFromRedis = actualConnection.StringGet(redisConn.Keys.LockKey);
                Assert.True(lockValueFromRedis.IsNull);

                RedisValue sessionDataFromRedis = actualConnection.StringGet(redisConn.Keys.DataKey);
                SessionStateItemCollection dataFromRedis = null;
                MemoryStream ms = new MemoryStream(sessionDataFromRedis);
                BinaryReader reader = new BinaryReader(ms);
                dataFromRedis = SessionStateItemCollection.Deserialize(reader);

                Assert.Equal("new-value1", dataFromRedis["key1"]);
                Assert.Equal("value2", dataFromRedis["key2"]);
                Assert.Null(dataFromRedis["key3"]);

                // remove data from redis
                actualConnection.KeyDelete(redisConn.Keys.DataKey);
                DisposeRedisConnectionWrapper(redisConn);
            }
        }

        [Fact()]
        public async Task TryUpdateIfLockIdMatch_WithOnlyUpdateAndNoDelete()
        {
            ProviderConfiguration pc = Utility.GetDefaultConfigUtility();
            using (RedisServer redisServer = new RedisServer())
            {
                RedisConnectionWrapper redisConn = GetRedisConnectionWrapperWithUniqueSession();

                // Inserting data into redis server
                SessionStateItemCollection data1 = new SessionStateItemCollection();
                data1["key1"] = "value1";
                data1["key2"] = "value2";
                data1["key3"] = "value3";
                await redisConn.SetAsync(data1, 900);

                // Mannually add lock to redis
                DateTime lockTime = DateTime.Now;
                IDatabase actualConnection = GetRealRedisConnection(redisConn);
                actualConnection.StringSet(redisConn.Keys.LockKey, lockTime.Ticks.ToString());

                // Update
                SessionStateItemCollection data2 = new SessionStateItemCollection();
                data2["key1"] = "new-value1";
                data2["key2"] = "value2";
                data2["key3"] = "value3";
                await redisConn.UpdateAndReleaseLockAsync(lockTime.Ticks, data2, 900);

                var lockValueFromRedis = actualConnection.StringGet(redisConn.Keys.LockKey);
                Assert.True(lockValueFromRedis.IsNull);

                RedisValue sessionDataFromRedis = actualConnection.StringGet(redisConn.Keys.DataKey);
                SessionStateItemCollection dataFromRedis = null;
                MemoryStream ms = new MemoryStream(sessionDataFromRedis);
                BinaryReader reader = new BinaryReader(ms);
                dataFromRedis = SessionStateItemCollection.Deserialize(reader);

                Assert.Equal("new-value1", dataFromRedis["key1"]);
                Assert.Equal("value2", dataFromRedis["key2"]);
                Assert.Equal("value3", dataFromRedis["key3"]);

                // remove data from redis
                actualConnection.KeyDelete(redisConn.Keys.DataKey);
                DisposeRedisConnectionWrapper(redisConn);
            }
        }

        [Fact()]
        public async Task TryUpdateIfLockIdMatch_WithNoUpdateAndOnlyDelete()
        {
            ProviderConfiguration pc = Utility.GetDefaultConfigUtility();
            using (RedisServer redisServer = new RedisServer())
            {
                RedisConnectionWrapper redisConn = GetRedisConnectionWrapperWithUniqueSession();

                // Inserting data into redis server
                SessionStateItemCollection data1 = new SessionStateItemCollection();
                data1["key1"] = "value1";
                data1["key2"] = "value2";
                data1["key3"] = "value3";
                await redisConn.SetAsync(data1, 900);

                // Mannually add lock to redis
                DateTime lockTime = DateTime.Now;
                IDatabase actualConnection = GetRealRedisConnection(redisConn);
                actualConnection.StringSet(redisConn.Keys.LockKey, lockTime.Ticks.ToString());

                // Update
                SessionStateItemCollection data2 = new SessionStateItemCollection();
                data2["key1"] = "value1";
                data2["key2"] = "value2";
                data2.Remove("key3");
                await redisConn.UpdateAndReleaseLockAsync(lockTime.Ticks, data2, 900);

                var lockValueFromRedis = actualConnection.StringGet(redisConn.Keys.LockKey);
                Assert.True(lockValueFromRedis.IsNull);

                RedisValue sessionDataFromRedis = actualConnection.StringGet(redisConn.Keys.DataKey);
                SessionStateItemCollection dataFromRedis = null;
                MemoryStream ms = new MemoryStream(sessionDataFromRedis);
                BinaryReader reader = new BinaryReader(ms);
                dataFromRedis = SessionStateItemCollection.Deserialize(reader);

                Assert.Equal("value1", dataFromRedis["key1"]);
                Assert.Equal("value2", dataFromRedis["key2"]);
                Assert.Null(dataFromRedis["key3"]);

                // remove data from redis
                actualConnection.KeyDelete(redisConn.Keys.DataKey);
                DisposeRedisConnectionWrapper(redisConn);
            }
        }

        [Fact()]
        public async Task TryUpdateIfLockIdMatch_ExpiryTime_OnValidData()
        {
            ProviderConfiguration pc = Utility.GetDefaultConfigUtility();
            using (RedisServer redisServer = new RedisServer())
            {
                RedisConnectionWrapper redisConn = GetRedisConnectionWrapperWithUniqueSession();

                // Inserting data into redis server
                SessionStateItemCollection data1 = new SessionStateItemCollection();
                data1["key1"] = "value1";
                data1["key2"] = "value2";
                data1["key3"] = "value3";
                await redisConn.SetAsync(data1, 900);

                // Mannually add lock to redis
                DateTime lockTime = DateTime.Now;
                IDatabase actualConnection = GetRealRedisConnection(redisConn);
                actualConnection.StringSet(redisConn.Keys.LockKey, lockTime.Ticks.ToString());

                // Update
                SessionStateItemCollection data2 = new SessionStateItemCollection();
                data2.Remove("key1");
                data2.Remove("key2");
                data2.Remove("key3");
                await redisConn.UpdateAndReleaseLockAsync(lockTime.Ticks, data2, 2);

                // Wait for 3 seconds to check the data set is getting expired
                System.Threading.Thread.Sleep(3000);

                var lockValueFromRedis = actualConnection.StringGet(redisConn.Keys.LockKey);
                Assert.True(lockValueFromRedis.IsNull);

                HashEntry[] sessionDataFromRedis = actualConnection.HashGetAll(redisConn.Keys.DataKey);
                Assert.Empty(sessionDataFromRedis);

                DisposeRedisConnectionWrapper(redisConn);
            }
        }

        [Fact()]
        public async Task TryUpdateAndReleaseLockIfLockIdMatch_LargeLockTime_ExpireManuallyTest()
        {
            ProviderConfiguration pc = Utility.GetDefaultConfigUtility();
            using (RedisServer redisServer = new RedisServer())
            {
                RedisConnectionWrapper redisConn = GetRedisConnectionWrapperWithUniqueSession();

                // Inserting data into redis server
                SessionStateItemCollection data1 = new SessionStateItemCollection();
                data1["key1"] = "value1";
                await redisConn.SetAsync(data1, 900);

                // Mannually add lock to redis
                DateTime lockTime = DateTime.Now;
                IDatabase actualConnection = GetRealRedisConnection(redisConn);
                actualConnection.StringSet(redisConn.Keys.LockKey, lockTime.Ticks.ToString());

                // Update
                SessionStateItemCollection data2 = new SessionStateItemCollection();
                data2["key1"] = "new-value";
                await redisConn.UpdateAndReleaseLockAsync(lockTime.Ticks, data2, 1);

                // Wait for 3 seconds for data to expire
                System.Threading.Thread.Sleep(3000);

                var sessionDataFromRedis = actualConnection.StringGet(redisConn.Keys.DataKey);
                Assert.True(sessionDataFromRedis.IsNull);

                DisposeRedisConnectionWrapper(redisConn);
            }
        }

        [Fact()]
        public async Task TryRemoveIfLockIdMatch_NullLockId()
        {
            ProviderConfiguration pc = Utility.GetDefaultConfigUtility();
            using (RedisServer redisServer = new RedisServer())
            {
                RedisConnectionWrapper redisConn = GetRedisConnectionWrapperWithUniqueSession();

                // Inserting data into redis server
                SessionStateItemCollection data = new SessionStateItemCollection();
                data["key"] = "value";
                await redisConn.SetAsync(data, 900);

                // Mannually add lock to redis
                DateTime lockTime = DateTime.Now;
                IDatabase actualConnection = GetRealRedisConnection(redisConn);
                actualConnection.StringSet(redisConn.Keys.LockKey, lockTime.Ticks.ToString());

                // Remove
                await redisConn.RemoveAndReleaseLockAsync(null);

                var lockValueFromRedis = actualConnection.StringGet(redisConn.Keys.LockKey);
                Assert.Equal(lockTime.Ticks.ToString(), lockValueFromRedis);

                var sessionDataFromRedis = actualConnection.StringGet(redisConn.Keys.DataKey);
                Assert.False(sessionDataFromRedis.IsNull);

                // remove data and lock from redis
                actualConnection.KeyDelete(redisConn.Keys.DataKey);
                actualConnection.KeyDelete(redisConn.Keys.LockKey);
                DisposeRedisConnectionWrapper(redisConn);
            }
        }

        [Fact()]
        public async Task TryUpdateIfLockIdMatch_LockIdNull()
        {
            ProviderConfiguration pc = Utility.GetDefaultConfigUtility();
            using (RedisServer redisServer = new RedisServer())
            {
                RedisConnectionWrapper redisConn = GetRedisConnectionWrapperWithUniqueSession();

                // Inserting data into redis server
                SessionStateItemCollection data1 = new SessionStateItemCollection();
                data1["key1"] = "value1";
                data1["key2"] = "value2";
                data1["key3"] = "value3";
                await redisConn.SetAsync(data1, 900);

                // Mannually add lock to redis
                DateTime lockTime = DateTime.Now;
                IDatabase actualConnection = GetRealRedisConnection(redisConn);
                actualConnection.StringSet(redisConn.Keys.LockKey, lockTime.Ticks.ToString());

                // Update
                SessionStateItemCollection data2 = new SessionStateItemCollection();
                data2["key1"] = "new-value1";
                data2["key2"] = "value2";
                data2.Remove("key3");
                await redisConn.UpdateAndReleaseLockAsync(null, data2, 900);

                var lockValueFromRedis = actualConnection.StringGet(redisConn.Keys.LockKey);
                Assert.Equal(lockTime.Ticks.ToString(), lockValueFromRedis);

                RedisValue sessionDataFromRedis = actualConnection.StringGet(redisConn.Keys.DataKey);
                SessionStateItemCollection dataFromRedis = null;
                MemoryStream ms = new MemoryStream(sessionDataFromRedis);
                BinaryReader reader = new BinaryReader(ms);
                dataFromRedis = SessionStateItemCollection.Deserialize(reader);

                Assert.Equal("value1", dataFromRedis["key1"]);
                Assert.Equal("value2", dataFromRedis["key2"]);
                Assert.Equal("value3", dataFromRedis["key3"]);

                // remove data from redis
                actualConnection.KeyDelete(redisConn.Keys.DataKey);
                actualConnection.KeyDelete(redisConn.Keys.LockKey);
                DisposeRedisConnectionWrapper(redisConn);
            }
        }

        [Fact()]
        public async Task TryCheckWriteLockAndGetData_WithOtherWriteLock()
        {
            ProviderConfiguration pc = Utility.GetDefaultConfigUtility();
            using (RedisServer redisServer = new RedisServer())
            {
                RedisConnectionWrapper redisConn = GetRedisConnectionWrapperWithUniqueSession();

                // Inserting data into redis server
                SessionStateItemCollection data = new SessionStateItemCollection();
                data["key"] = "value";
                await redisConn.SetAsync(data, 900);

                // Mannually add lock to redis
                DateTime lockTime = DateTime.Now;
                IDatabase actualConnection = GetRealRedisConnection(redisConn);
                actualConnection.StringSet(redisConn.Keys.LockKey, lockTime.Ticks.ToString());

                var result = await redisConn.TryCheckWriteLockAndGetDataAsync();
                Assert.False(result.Success);
                Assert.Equal(lockTime.Ticks.ToString(), result.LockId.ToString());
                Assert.Null(result.Data);

                // remove data and lock from redis
                actualConnection.KeyDelete(redisConn.Keys.DataKey);
                actualConnection.KeyDelete(redisConn.Keys.LockKey);
                DisposeRedisConnectionWrapper(redisConn);
            }
        }

        private IDatabase GetRealRedisConnection(RedisConnectionWrapper redisConn)
        {
            return RedisConnectionWrapper.sharedConnection.GetMultiplexerForTesting().Database;
        }
    }
}