//
// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
//

using System;
using System.Diagnostics;
using System.IO;
using System.Web.SessionState;
using StackExchange.Redis;
using System.Threading.Tasks;

namespace Microsoft.Web.Redis
{
    internal class StackExchangeClientConnection : IRedisClientConnection
    {
        private ProviderConfiguration _configuration;
        private RedisSharedConnection _sharedConnection;

        public StackExchangeClientConnection(ProviderConfiguration configuration, RedisSharedConnection sharedConnection)
        {
            _configuration = configuration;
            _sharedConnection = sharedConnection;
        }

        // This is used just by tests
        public IDatabase RealConnection
        {
            get { return _sharedConnection.Multiplexer.Database; }
        }

        public ConnectionMultiplexerWrapper Multiplexer => _sharedConnection.Multiplexer;


        public async Task<bool> ExpiryAsync(string key, int timeInSeconds)
        {
            TimeSpan timeSpan = new TimeSpan(0, 0, timeInSeconds);
            RedisKey redisKey = key;
            return (bool)await RetryLogicAsync(Multiplexer, async (mx) => await  mx.Database.KeyExpireAsync(redisKey, timeSpan));
        }

        public async Task<object> EvalAsync(LoadedLuaScript script, object args)
        {
            return await RetryLogicAsync(Multiplexer, async (mx) => await script.EvaluateAsync(Multiplexer.Database,args));
        }

        public async Task<object> EvalAsync(string script, string[] keyArgs, object[] valueArgs)
        {
            RedisKey[] redisKeyArgs = new RedisKey[keyArgs.Length];
            RedisValue[] redisValueArgs = new RedisValue[valueArgs.Length];

            int i = 0;
            foreach (string key in keyArgs)
            {
                redisKeyArgs[i] = key;
                i++;
            }

            i = 0;
            foreach (object val in valueArgs)
            {
                if (val.GetType() == typeof(byte[]))
                {
                    // User data is always in bytes
                    redisValueArgs[i] = (byte[])val;
                }
                else
                {
                    // Internal data like session timeout and indexes are stored as strings
                    redisValueArgs[i] = val.ToString();
                }
                i++;
            }
            return await RetryLogicAsync(Multiplexer, async (mx) => await mx.Database.ScriptEvaluateAsync(script, redisKeyArgs, redisValueArgs));
        }


        private async Task<object> OperationExecutorAsync(ConnectionMultiplexerWrapper multiplexer, Func<ConnectionMultiplexerWrapper, Task<object>> redisOperation)
        {
            try
            {
                return await redisOperation(multiplexer);
            }
            catch (ObjectDisposedException)
            {
                // Try once as this can be caused by force reconnect by closing multiplexer
                return await redisOperation(multiplexer);
            }
            catch (RedisConnectionException)
            {
                multiplexer.ForceReconnect();
                return await redisOperation(multiplexer);
            }
            catch (Exception e)
            {
                if (e.Message.Contains("NOSCRIPT"))
                {
                    // Second call should pass if it was script not found issue
                    return await redisOperation(multiplexer);
                }
                throw;
            }
        }

        /// <summary>
        /// Async version of RetryLogic
        /// If retry timout is provide than we will retry first time after 20 ms and after that every 1 sec till retry timout is expired or we get value.
        /// </summary>
        private async Task<object> RetryLogicAsync(ConnectionMultiplexerWrapper connection, Func<ConnectionMultiplexerWrapper, Task<object>> redisOperation)
        {
            int timeToSleepBeforeRetryInMiliseconds = 20;
            DateTime startTime = DateTime.Now;
            while (true)
            {
                try
                {
                    return await OperationExecutorAsync(connection, redisOperation);
                }
                catch (Exception e)
                {
                    TimeSpan passedTime = DateTime.Now - startTime;
                    if (_configuration.RetryTimeout < passedTime)
                    {
                        LogUtility.LogError($"Exception: {e.Message}");
                        throw;
                    }
                    else
                    {
                        int remainingTimeout = (int)(_configuration.RetryTimeout.TotalMilliseconds - passedTime.TotalMilliseconds);
                        // if remaining time is less than 1 sec than wait only for that much time and than give a last try
                        if (remainingTimeout < timeToSleepBeforeRetryInMiliseconds)
                        {
                            timeToSleepBeforeRetryInMiliseconds = remainingTimeout;
                        }
                    }

                    // First time try after 20 msec after that try after 1 second
                    await Task.Delay(timeToSleepBeforeRetryInMiliseconds);
                    timeToSleepBeforeRetryInMiliseconds = 1000;
                }
            }
        }

        public int GetSessionTimeout(object rowDataFromRedis)
        {
            RedisResult rowDataAsRedisResult = (RedisResult)rowDataFromRedis;
            RedisResult[] lockScriptReturnValueArray = (RedisResult[])rowDataAsRedisResult;
            Debug.Assert(lockScriptReturnValueArray != null);
            Debug.Assert(lockScriptReturnValueArray[2] != null);
            int sessionTimeout = (int)lockScriptReturnValueArray[2];
            if (sessionTimeout == -1)
            {
                sessionTimeout = (int)_configuration.SessionTimeout.TotalSeconds;
            }
            // converting seconds to minutes
            sessionTimeout = sessionTimeout / 60;
            return sessionTimeout;
        }

        public bool IsLocked(object rowDataFromRedis)
        {
            RedisResult rowDataAsRedisResult = (RedisResult)rowDataFromRedis;
            RedisResult[] lockScriptReturnValueArray = (RedisResult[])rowDataAsRedisResult;
            Debug.Assert(lockScriptReturnValueArray != null);
            Debug.Assert(lockScriptReturnValueArray[3] != null);
            return (bool)lockScriptReturnValueArray[3];
        }

        public LoadedLuaScript LoadLuaScript(LuaScript script)
        {
            throw new NotImplementedException();
        }

        public string GetLockId(object rowDataFromRedis)
        {
            RedisResult rowDataAsRedisResult = (RedisResult)rowDataFromRedis;
            RedisResult[] lockScriptReturnValueArray = (RedisResult[])rowDataAsRedisResult;
            Debug.Assert(lockScriptReturnValueArray != null);
            return (string)lockScriptReturnValueArray[0];
        }

        public ISessionStateItemCollection GetSessionData(object rowDataFromRedis)
        {
            RedisResult rowDataAsRedisResult = (RedisResult)rowDataFromRedis;
            RedisResult[] lockScriptReturnValueArray = (RedisResult[])rowDataAsRedisResult;
            Debug.Assert(lockScriptReturnValueArray != null);

            SessionStateItemCollection sessionData = null;
            if (lockScriptReturnValueArray.Length > 1 && lockScriptReturnValueArray[1] != null)
            {
                RedisResult data = lockScriptReturnValueArray[1];
                var serializedSessionStateItemCollection = data;

                if (serializedSessionStateItemCollection != null)
                {
                    sessionData = DeserializeSessionStateItemCollection(serializedSessionStateItemCollection);
                }
            }
            return sessionData;
        }

        internal SessionStateItemCollection DeserializeSessionStateItemCollection(RedisResult serializedSessionStateItemCollection)
        {
            try
            {
                MemoryStream ms = new MemoryStream((byte[])serializedSessionStateItemCollection);
                BinaryReader reader = new BinaryReader(ms);
                return SessionStateItemCollection.Deserialize(reader);
            }
            catch
            {
                return null;
            }
        }

        public async Task SetAsync(string key, byte[] data, DateTime utcExpiry)
        {
            RedisKey redisKey = key;
            RedisValue redisValue = data;
            TimeSpan timeSpanForExpiry = utcExpiry - DateTime.UtcNow;
            await OperationExecutorAsync(Multiplexer,async (mx) =>
            {
                await mx.Database.StringSetAsync(redisKey, redisValue, timeSpanForExpiry);
                return (object)true; // Return value needed for the method signature
            });
        }

        public async Task<byte[]> GetAsync(string key)
        {
            RedisKey redisKey = key;
            RedisValue redisValue = (RedisValue)await OperationExecutorAsync(Multiplexer,async (mx) => await mx.Database.StringGetAsync(redisKey));
            return (byte[])redisValue;
        }

        public async Task RemoveAsync(string key)
        {
            RedisKey redisKey = key;
            await OperationExecutorAsync(Multiplexer,async (mx) =>
            {
                await mx.Database.KeyDeleteAsync(redisKey);
                return (object)true; // Return value needed for the method signature
            });
        }

        public byte[] GetOutputCacheDataFromResult(object rowDataFromRedis)
        {
            RedisResult rowDataAsRedisResult = (RedisResult)rowDataFromRedis;
            return (byte[])rowDataAsRedisResult;
        }
    }
}
