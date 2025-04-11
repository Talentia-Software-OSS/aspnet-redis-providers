//
// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Web.SessionState;
using StackExchange.Redis;

namespace Microsoft.Web.Redis
{
    internal class RedisConnectionWrapper : ICacheConnection
    {
        internal static RedisSharedConnection sharedConnection { get; private set; }
        private static readonly object lockForSharedConnection = new object();
        private static int connectionRefCount = 0;

        public KeyGenerator Keys { set; get; }

        internal IRedisClientConnection redisConnection;
        private ProviderConfiguration configuration;

        private LoadedLuaScript UpdateExpiryTimeLoadScript;
        private LoadedLuaScript SetLoadedScript;
        private LoadedLuaScript WriteLockAndGetDataLoadedScript;
        private LoadedLuaScript ReadLockAndGetDataLoadedScript;
        private LoadedLuaScript ReleaseWriteLockIfLockMatchLoadedScript;
        private LoadedLuaScript RemoveSessionLoadedScript;

        public RedisConnectionWrapper(ProviderConfiguration configuration, string id)
        {
            this.configuration = configuration;
            Keys = new KeyGenerator(id, configuration.ApplicationName);

            // only single object of RedisSharedConnection will be created and then reused
            lock (lockForSharedConnection)
            {
                if (sharedConnection == null)
                {
                    sharedConnection = new RedisSharedConnection(configuration);
                }
                // Increment ref count for better tracking in concurrent scenarios
                connectionRefCount++;
            }
            redisConnection = new StackExchangeClientConnection(configuration, sharedConnection);

            PrepareLoadedScripts();
        }

        private void PrepareLoadedScripts()
        {
            UpdateExpiryTimeLoadScript = redisConnection.LoadLuaScript(UpdateExpiryTimePreparedScript);
            SetLoadedScript = redisConnection.LoadLuaScript(SetScriptPrepared);
            WriteLockAndGetDataLoadedScript = redisConnection.LoadLuaScript(WriteLockAndGetDataPreparedScript);
            ReadLockAndGetDataLoadedScript = redisConnection.LoadLuaScript(ReadLockAndGetDataPrepared);
            ReleaseWriteLockIfLockMatchLoadedScript = redisConnection.LoadLuaScript(ReleaseWriteLockIfLockMatchPreparedScript);
            RemoveSessionLoadedScript = redisConnection.LoadLuaScript(RemoveSessionPreparedScript);

        }

        // This method will be called by tests to ensure proper cleanup
        public static void CleanupConnection()
        {
            lock (lockForSharedConnection)
            {
                // Only dispose when explicitly requested and ref count is 0
                if (sharedConnection != null)
                {
                    sharedConnection.Dispose();
                    sharedConnection = null;
                    connectionRefCount = 0;
                }
            }
        }

        internal static void InternalSetSharedConnection(RedisSharedConnection connection)
        {
            lock (lockForSharedConnection)
            {
                if (sharedConnection != null)
                {
                    sharedConnection.Dispose();
                }
                sharedConnection = connection;
            }
        }

        // Release connection reference
        public void Release()
        {
            lock (lockForSharedConnection)
            {
                if (connectionRefCount > 0)
                {
                    connectionRefCount--;
                }

                if (connectionRefCount == 0 && sharedConnection != null)
                {
                    // We're the last one using this connection, let's clean up
                    sharedConnection.Dispose();
                    sharedConnection = null;
                }
            }
        }


        public Task<TimeSpan> GetLockAgeAsync(object lockId)
        {
            // This method does not use redis but we'll make it async for API consistency
            string lockDateTimeTicksFromLockId = lockId.ToString();
            long lockTimeTicks;
            if (long.TryParse(lockDateTimeTicksFromLockId, out lockTimeTicks))
            {
                return Task.FromResult(DateTime.Now.Subtract(new DateTime(lockTimeTicks)));
            }
            else
            { //lock id is not valid so release item exclusive should be called so make lock age very large
                return Task.FromResult(DateTime.Now.Subtract(new DateTime()));
            }
        }

        /*-------Start of UpdateExpiryTime operation-----------------------------------------------------------------------------------------------------------------------------------------------*/

        // KEYS[1] = = data-id, internal-id
        // ARGV[1] = session-timeout
        // this order should not change LUA script depends on it
        // if data doesn't exists then do nothing
        private static readonly string updateExpiryTimeScript = (@"
                local dataExists = redis.call('EXISTS', KEYS[1])
                if dataExists == 0 then
                    return 1;
                end

                local SessionTimeout = redis.call('GET', KEYS[2])
                if SessionTimeout ~= false then
                    redis.call('EXPIRE',KEYS[1], SessionTimeout)
                    redis.call('EXPIRE',KEYS[2], SessionTimeout)
                else
                    redis.call('EXPIRE',KEYS[1],ARGV[1])
                    redis.call('SET', KEYS[2], ARGV[1])
                    redis.call('EXPIRE',KEYS[2],ARGV[1])
                end
                return 1"
                );

        private const string UpdateExpiryTimeScriptConstant = (@"
                local dataExists = redis.call('EXISTS', @dataKey)
                if dataExists == 0 then
                    return 1;
                end

                local SessionTimeout = redis.call('GET', @internalKey)
                if SessionTimeout ~= false then
                    redis.call('EXPIRE',@dataKey, SessionTimeout)
                    redis.call('EXPIRE',@internalKey, SessionTimeout)
                else
                    redis.call('EXPIRE', @dataKey,@timeToExpireInSeconds)
                    redis.call('SET',@internalKey, @timeToExpireInSeconds)
                    redis.call('EXPIRE',@internalKey,@timeToExpireInSeconds)
                end
                return 1"
            );

        public static readonly LuaScript UpdateExpiryTimePreparedScript = LuaScript.Prepare(UpdateExpiryTimeScriptConstant);



        public async Task UpdateExpiryTimeAsync(int timeToExpireInSeconds)
        {
            //string[] keyArgs = new string[] { Keys.DataKey, Keys.InternalKey };
            //object[] valueArgs = new object[1];
            //valueArgs[0] = timeToExpireInSeconds;
            //await redisConnection.EvalAsync(updateExpiryTimeScript, keyArgs, valueArgs);
            //string[] keyArgs = new string[] { Keys.DataKey, Keys.InternalKey };
            //object[] valueArgs = new object[1];
            //valueArgs[0] = timeToExpireInSeconds;
            var input = new
            {
                dataKey = Keys.DataKey,
                internalKey = Keys.InternalKey,
                timeToExpireInSeconds = timeToExpireInSeconds.ToString()
            };

            await redisConnection.EvalAsync(UpdateExpiryTimeLoadScript, input);
        }

        /*-------End of UpdateExpiryTime operation-----------------------------------------------------------------------------------------------------------------------------------------------*/

        /*-------Start of Set operation-----------------------------------------------------------------------------------------------------------------------------------------------*/

        // KEYS[1] = = data-id, internal-id
        // ARGV[1] = serialized session state, ARGV[2] = session-timeout
        // this order should not change LUA script depends on it
        private static readonly string setScript = (@"
                redis.call('SET', KEYS[1], ARGV[1])
                redis.call('EXPIRE',KEYS[1],ARGV[2])
                redis.call('SET', KEYS[2], ARGV[2])
                redis.call('EXPIRE',KEYS[2],ARGV[2])
                return 1"
                );

        private static readonly string SetScriptConstant = (@"
                redis.call('SET', @DataKey, @SerializedSessionStateItemCollection)
                redis.call('EXPIRE',@DataKey,@SessionTimeout)
                redis.call('SET',@InternalKey, @SessionTimeout)
                redis.call('EXPIRE',@InternalKey,@SessionTimeout)
                return 1"
            );

        public static readonly LuaScript SetScriptPrepared = LuaScript.Prepare(SetScriptConstant);


        private bool SetPrepare(ISessionStateItemCollection data, int sessionTimeout, out object arg)
        {
            arg = null;
            try
            {
                byte[] serializedSessionStateItemCollection = SerializeSessionStateItemCollection(data);

                arg = new
                {
                    DataKey = Keys.DataKey,
                    InternalKey = Keys.InternalKey,
                    SerializedSessionStateItemCollection = serializedSessionStateItemCollection,
                    SessionTimeout = sessionTimeout
                };

                return true;
            }
            catch
            {
                return false;
            }
        }

        internal byte[] SerializeSessionStateItemCollection(ISessionStateItemCollection sessionStateItemCollection)
        {
            if (sessionStateItemCollection is null)
            {
                return null;
            }
            MemoryStream ms = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(ms);
            ((SessionStateItemCollection)sessionStateItemCollection).Serialize(writer);
            writer.Close();
            return ms.ToArray();
        }

        public async Task SetAsync(ISessionStateItemCollection data, int sessionTimeout)
        {
            if (SetPrepare(data, sessionTimeout, out var arg))
            {
                await redisConnection.EvalAsync(SetLoadedScript, arg);
            }
        }

        /*-------End of Set operation-----------------------------------------------------------------------------------------------------------------------------------------------*/

        /*-------Start of Lock set operation-----------------------------------------------------------------------------------------------------------------------------------------------*/

        // KEYS = { write-lock-id, data-id, internal-id }
        // ARGV = { write-lock-value-that-we-want-to-set, request-timout }
        // lockValue = 1) (Initially) write lock value that we want to set (ARGV[1]) if we get lock successfully this will return as retArray[1]
        //             2) If another write lock exists than its lock value from cache
        // retArray = {lockValue , session data if lock was taken successfully, session timeout value if exists, wheather lock was taken or not}
        private static readonly string writeLockAndGetDataConstatn = (@"
                 local retArray = {}
                local lockValue = @ExpectedLockId
                local locked = redis.call('SETNX',@LockKey,@ExpectedLockId)
                local IsLocked = true

                if locked == 0 then
                    lockValue = redis.call('GET',@LockKey)
                else
                    redis.call('EXPIRE',@LockKey,@LockTimeout)
                    IsLocked = false
                end

                retArray[1] = lockValue
                if lockValue == @ExpectedLockId then retArray[2] = redis.call('GET',@DataKey) else retArray[2] = '' end

                local SessionTimeout = redis.call('GET',@InternalKey)
                if SessionTimeout ~= false then
                    retArray[3] = SessionTimeout
                    redis.call('EXPIRE',@DataKey, SessionTimeout)
                    redis.call('EXPIRE',@InternalKey, SessionTimeout)
                else
                    retArray[3] = '-1'
                end

                retArray[4] = IsLocked
                return retArray
                ");

        public static readonly LuaScript WriteLockAndGetDataPreparedScript = LuaScript.Prepare(writeLockAndGetDataConstatn);


        public async Task<(bool Success, object LockId, ISessionStateItemCollection Data, int SessionTimeout)> TryTakeWriteLockAndGetDataAsync(DateTime lockTime, int lockTimeout)
        {
            string expectedLockId = lockTime.Ticks.ToString();
            string[] keyArgs = new string[] { Keys.LockKey, Keys.DataKey, Keys.InternalKey };
            object[] valueArgs = new object[] { expectedLockId, lockTimeout };

            var args = new
            {
                LockKey = Keys.LockKey,
                DataKey = Keys.DataKey,
                InternalKey = Keys.InternalKey,
                ExpectedLockId = expectedLockId,
                LockTimeout = lockTimeout.ToString()
            };

            object rowDataFromRedis = await redisConnection.EvalAsync(WriteLockAndGetDataLoadedScript, args);
                //await redisConnection.EvalAsync(writeLockAndGetDataConstatn, keyArgs, valueArgs);

            object lockId = redisConnection.GetLockId(rowDataFromRedis);
            int sessionTimeout = redisConnection.GetSessionTimeout(rowDataFromRedis);
            bool isLocked = redisConnection.IsLocked(rowDataFromRedis);
            ISessionStateItemCollection data = null;
            bool success = false;

            if (!isLocked && lockId.ToString().Equals(expectedLockId))
            {
                success = true;
                data = redisConnection.GetSessionData(rowDataFromRedis);
            }

            return (success, lockId, data, sessionTimeout);
        }

        // KEYS = { write-lock-id, data-id, internal-id }
        // ARGV = { }
        // lockValue = 1) (Initially) read lock value that we want to set (ARGV[1]) if we get lock successfully this will return as retArray[1]
        //             3) If write lock exists than its lock value from cache
        // retArray = {lockValue , session data if lock does not exist}
        private static readonly string readLockAndGetDataScript = (@"
                    local retArray = {}
                    local lockValue = ''
                    local writeLockValue = redis.call('GET',KEYS[1])
                    if writeLockValue ~= false then
                       lockValue = writeLockValue
                    end
                    retArray[1] = lockValue
                    if lockValue == '' then retArray[2] = redis.call('GET',KEYS[2]) else retArray[2] = '' end

                    local SessionTimeout = redis.call('GET', KEYS[3])
                    if SessionTimeout ~= false then
                        retArray[3] = SessionTimeout
                        redis.call('EXPIRE',KEYS[2], SessionTimeout)
                        redis.call('EXPIRE',KEYS[3], SessionTimeout)
                    else
                        retArray[3] = '-1'
                    end
                    return retArray
                    ");

        private const string ReadLockAndGetDataConst = (@"
                 local retArray = {}
                 local lockValue = ''
                 local writeLockValue = redis.call('GET',@LockKey)
                 if writeLockValue ~= false then
                    lockValue = writeLockValue
                 end
                 retArray[1] = lockValue
                 if lockValue == '' then retArray[2] = redis.call('GET',@DataKey) else retArray[2] = '' end

                 local SessionTimeout = redis.call('GET', @InternalKey)
                 if SessionTimeout ~= false then
                     retArray[3] = SessionTimeout
                     redis.call('EXPIRE',@DataKey, SessionTimeout)
                     redis.call('EXPIRE',@InternalKey, SessionTimeout)
                 else
                     retArray[3] = '-1'
                 end
                 return retArray
                 ");

        public static readonly LuaScript ReadLockAndGetDataPrepared = LuaScript.Prepare(ReadLockAndGetDataConst);


        public async Task<(bool Success, object LockId, ISessionStateItemCollection Data, int SessionTimeout)> TryCheckWriteLockAndGetDataAsync()
        {
            string[] keyArgs = new string[] { Keys.LockKey, Keys.DataKey, Keys.InternalKey };
            object[] valueArgs = new object[] { };

            var args = new
            {
                LockKey = Keys.LockKey,
                DataKey = Keys.DataKey,
                InternalKey = Keys.InternalKey
            };

            object rowDataFromRedis = await redisConnection.EvalAsync(ReadLockAndGetDataLoadedScript, args);

            object lockId = redisConnection.GetLockId(rowDataFromRedis);
            int sessionTimeout = redisConnection.GetSessionTimeout(rowDataFromRedis);
            ISessionStateItemCollection data = null;
            bool success = false;

            if (lockId.ToString().Equals(""))
            {
                // If lockId = "" means no lock exists and we got data from store.
                lockId = null;
                success = true;
                data = redisConnection.GetSessionData(rowDataFromRedis);
            }

            return (success, lockId, data, sessionTimeout);
        }

        /*-------End of Lock set operation-----------------------------------------------------------------------------------------------------------------------------------------------*/

        /*-------Start of Lock release operation-----------------------------------------------------------------------------------------------------------------------------------------------*/
        public async Task ReleaseLockIfLockIdMatchAsync(object lockId, int sessionTimeout)
        {
            string[] keyArgs = { Keys.LockKey, Keys.DataKey, Keys.InternalKey };
            object[] valueArgs = { lockId, sessionTimeout };
            var args = new
            {
                LockKey = Keys.LockKey,
                DataKey = Keys.DataKey,
                InternalKey = Keys.InternalKey,
                ExpectedLockId = lockId.ToString(),
                SessionTimeout = sessionTimeout.ToString()
            };
            await redisConnection.EvalAsync(ReleaseWriteLockIfLockMatchLoadedScript, args);
        }

        // KEYS[1] = write-lock-id, KEYS[2] = data-id, KEYS[3] = internal-id
        // ARGV = { write-lock-value }, ARGV[2] = session time out
        private static readonly string releaseWriteLockIfLockMatchScript = (@"
                local writeLockValueFromCache = redis.call('GET',KEYS[1])
                if writeLockValueFromCache == ARGV[1] then
                    redis.call('DEL',KEYS[1])
                end
                local SessionTimeout = redis.call('GET', KEYS[3])
                if SessionTimeout ~= false then
                    redis.call('EXPIRE',KEYS[2], SessionTimeout)
                    redis.call('EXPIRE',KEYS[3], SessionTimeout)
                else
                    redis.call('EXPIRE',KEYS[2],ARGV[2])
                end
                return 1
                ");

        private static readonly string ReleaseWriteLockIfLockMatchConst = (@"
                local writeLockValueFromCache = redis.call('GET',@LockKey)
                if writeLockValueFromCache == @ExpectedLockId then
                    redis.call('DEL',@LockKey)
                end
                local SessionTimeout = redis.call('GET', @InternalKey)
                if SessionTimeout ~= false then
                    redis.call('EXPIRE',@DataKey, SessionTimeout)
                    redis.call('EXPIRE',@InternalKey, SessionTimeout)
                else
                    redis.call('EXPIRE',@DataKey,@SessionTimeout)
                end
                return 1
                ");

        public static readonly LuaScript ReleaseWriteLockIfLockMatchPreparedScript = LuaScript.Prepare(ReleaseWriteLockIfLockMatchConst);


        /*-------End of Lock release operation-----------------------------------------------------------------------------------------------------------------------------------------------*/

        // KEYS = { write-lock-id, data-id, internal-id}
        // ARGV = { write-lock-value }
        private static readonly string removeSessionScript = (@"
                if ARGV[1] ~= '' then
                    local lockValue = redis.call('GET',KEYS[1])
                    if lockValue ~=  ARGV[1] then
                        return 1
                    end
                end
                redis.call('DEL',KEYS[2])
                redis.call('DEL',KEYS[3])
                redis.call('DEL',KEYS[1])
                ");

        private static readonly string RemoveSessionConst = (@"
                if @ExpectedLockId ~= '' then
                    local lockValue = redis.call('GET',@LockKey)
                    if lockValue ~=  @ExpectedLockId then
                        return 1
                    end
                end
                redis.call('DEL',@DataKey)
                redis.call('DEL',@InternalKey)
                redis.call('DEL',@LockKey)
                ");

        public static readonly LuaScript RemoveSessionPreparedScript = LuaScript.Prepare(RemoveSessionConst);

        public async Task RemoveAndReleaseLockAsync(object lockId)
        {
            // If lockId is null, don't perform the remove operation
            if (lockId == null)
            {
                return;
            }

            string[] keyArgs = { Keys.LockKey, Keys.DataKey, Keys.InternalKey };
            object[] valueArgs = { lockId.ToString() };

            var args = new
            {
                LockKey = Keys.LockKey,
                DataKey = Keys.DataKey,
                InternalKey = Keys.InternalKey,
                ExpectedLockId = lockId.ToString()
            };

            await redisConnection.EvalAsync(RemoveSessionLoadedScript, args);
        }

        /*-------Start of TryUpdate operation-----------------------------------------------------------------------------------------------------------------------------------------------*/

        // KEYS[1] = write-lock-id, KEYS[2] = data-id, KEYS[3] = internal-id
        // ARGV[1] = write-lock-value, ARGV[2] = session time out,
        // ARGV[3] = number of items removed, ARGV[4] = number of items removed start index in ARGV, ARGV[5] = number of items removed end index in ARGV,
        // ARGV[6] = number of items updated, ARGV[7] = number of items updated start index in ARGV, ARGV[8] = number of items updated end index in ARGV,
        // ARGV[9...] = actual data
        // this order should not change LUA script depends on it
        private static readonly string removeAndUpdateSessionDataScript = (@"
                if ARGV[1] ~= '' then
                    local writeLockValueFromCache = redis.call('GET',KEYS[1])
                    if writeLockValueFromCache ~= ARGV[1] then
                        return 1
                    end
                end
                if tonumber(ARGV[6]) ~= 0 then redis.call('SET', KEYS[2], ARGV[10]) end
                redis.call('EXPIRE',KEYS[2],ARGV[2])
                redis.call('SET', KEYS[3], ARGV[2])
                redis.call('EXPIRE',KEYS[3],ARGV[2])
                redis.call('DEL',KEYS[1])");

        private bool TryUpdateAndReleaseLockPrepare(object lockId, ISessionStateItemCollection data, int sessionTimeout, out string[] keyArgs, out object[] valueArgs)
        {
            keyArgs = null;
            valueArgs = null;
            if (data != null)
            {
                List<object> list = new List<object>();
                int noOfItemsRemoved = 0;
                int noOfItemsUpdated = 0;
                byte[] serializedSessionStateItemCollection = SerializeSessionStateItemCollection(data);
                list.Add("SessionState");
                list.Add(serializedSessionStateItemCollection);
                noOfItemsUpdated = 1;

                keyArgs = new string[] { Keys.LockKey, Keys.DataKey, Keys.InternalKey };
                valueArgs = new object[list.Count + 8]; // this +8 is for first wight values in ARGV that we will add now
                valueArgs[0] = lockId?.ToString() ?? "";
                valueArgs[1] = sessionTimeout;
                valueArgs[2] = noOfItemsRemoved;
                valueArgs[3] = 9; // In Lua index starts from 1 so first item deleted will be 9th.
                valueArgs[4] = noOfItemsRemoved + 8; // index for last removed item
                valueArgs[5] = noOfItemsUpdated;
                valueArgs[6] = noOfItemsRemoved + 9; // first item updated will be next to last item removed
                valueArgs[7] = list.Count + 8; // index for last item in list in LUA

                var args = new
                {
                    LockKey = Keys.LockKey,
                    DataKey = Keys.DataKey,
                    InternalKey = Keys.InternalKey,
                    ExpectedLockId = lockId?.ToString() ?? "",
                    SessionTimeout = sessionTimeout,
                    NoOfItemsRemoved = noOfItemsRemoved,
                    FirstItemDelete = 9,
                    LastItemDelete = noOfItemsRemoved + 8,
                    NoOfItemsUpdated = noOfItemsUpdated,
                    FirstItemUpdate = noOfItemsRemoved + 9,
                    LastItemUpdate = list.Count + 8
                };



                // if nothing is changed in session then also execute update script to update session timeout
                if (list.Count != 0)
                {
                    list.CopyTo(valueArgs, 8);
                }
                return true;
            }
            return false;
        }

        public async Task UpdateAndReleaseLockAsync(object lockId, ISessionStateItemCollection data, int sessionTimeout)
        {
            // If lockId is null, don't perform the update operation
            if (lockId == null)
            {
                return;
            }

            string[] keyArgs;
            object[] valueArgs;
            if (TryUpdateAndReleaseLockPrepare(lockId, data, sessionTimeout, out keyArgs, out valueArgs))
            {
                await redisConnection.EvalAsync(removeAndUpdateSessionDataScript, keyArgs, valueArgs);
            }
        }

        /*-------End of TryUpdateIfLockIdMatch operation-----------------------------------------------------------------------------------------------------------------------------------------------*/
    }
}