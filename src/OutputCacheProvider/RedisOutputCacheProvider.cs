//
// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
//

using System;
using System.Threading.Tasks;
using System.Web.Caching;

namespace Microsoft.Web.Redis
{

    public class RedisOutputCacheProvider : OutputCacheProviderAsync
    {
        internal static ProviderConfiguration configuration;
        internal static object configurationCreationLock = new object();
        internal IOutputCacheConnection cache;
        
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
                        configuration = ProviderConfiguration.ProviderConfigurationForOutputCache(config);
                    }
                }
            }
        }

        public override object Get(string key)
        {
            try
            {
                GetAccessToCacheStore();
                return cache.GetAsync(key).GetAwaiter().GetResult();
            }
            catch(Exception e)
            {
                LogUtility.LogError("Error in Get: " + e.Message);
            }
            return null;
        }

        public override async Task<object> GetAsync(string key)
        {
            try
            {
                GetAccessToCacheStore();
                return await cache.GetAsync(key);
            }
            catch(Exception e)
            {
                LogUtility.LogError("Error in GetAsync: " + e.Message);
                return null;
            }
        }

        public override object Add(string key, object entry, DateTime utcExpiry)
        {
            try
            {
                GetAccessToCacheStore();
                return cache.AddAsync(key, entry, utcExpiry).GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                LogUtility.LogError("Error in Add: " + e.Message);
            }
            return null;
        }

        public override async Task<object> AddAsync(string key, object entry, DateTime utcExpiry)
        {
            try
            {
                GetAccessToCacheStore();
                return await cache.AddAsync(key, entry, utcExpiry);
            }
            catch (Exception e)
            {
                LogUtility.LogError("Error in AddAsync: " + e.Message);
                return null;
            }
        }

        public override void Set(string key, object entry, DateTime utcExpiry)
        {
            try
            {
                GetAccessToCacheStore();
                cache.SetAsync(key, entry, utcExpiry).GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                LogUtility.LogError("Error in Set: " + e.Message);
            }
        }

        public override async Task SetAsync(string key, object entry, DateTime utcExpiry)
        {
            try
            {
                GetAccessToCacheStore();
                await cache.SetAsync(key, entry, utcExpiry);
            }
            catch (Exception e)
            {
                LogUtility.LogError("Error in SetAsync: " + e.Message);
            }
        }

        public override void Remove(string key)
        {
            try
            {
                GetAccessToCacheStore();
                cache.RemoveAsync(key).GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                LogUtility.LogError("Error in Remove: " + e.Message);
            }
        }

        public override async Task RemoveAsync(string key)
        {
            try
            {
                GetAccessToCacheStore();
                await cache.RemoveAsync(key);
            }
            catch (Exception e)
            {
                LogUtility.LogError("Error in RemoveAsync: " + e.Message);
            }
        }

        private void GetAccessToCacheStore()
        {
            if (cache == null)
            {
                cache = new RedisOutputCacheConnectionWrapper(configuration);
            }
        }
    }
}