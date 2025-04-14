using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Web.Redis;
using StackExchange.Redis;

namespace Microsoft.Web.Redis
{
    public class ConnectionMultiplexerPool : IDisposable
    {
        private readonly List<ConnectionMultiplexerWrapper> _pool;
        private readonly int _poolSize;
        private readonly Random _random = new Random();

        internal ConnectionMultiplexerPool(ProviderConfiguration configuration, ConfigurationOptions configOption)
        {
            _poolSize = configuration.ConnectionPoolSize;
            _pool = new List<ConnectionMultiplexerWrapper>(_poolSize);
            for (var i = 0; i < configuration.ConnectionPoolSize; i++)
            {
                _pool.Add(new ConnectionMultiplexerWrapper(configuration, configOption, id: i));
            }
        }

        /// <summary>
        /// Get a connection multiplexer from the pool.
        /// </summary>
        /// <returns></returns>
        public ConnectionMultiplexerWrapper GetPooledMultiplexer(string sessionId = null)
        {
            var indexToPick = GetPoolIndex(sessionId);
            return _pool[indexToPick];
        }

        private int GetPoolIndex(string sessionId = null)
        {
            if (_poolSize == 1) return 0; // If only one connection always use that one

            if (sessionId != null) // If sessionId is provided, use it to determine the pool index ( keep the same multiplexer for the same sessionId )
            {
                //compute a simple hash and use it to determine the pool index
                int hash = 0;
                foreach (var c in sessionId)
                {
                    hash += c;
                }

                return hash % _poolSize;
            }
            //testing scenarios
            return _random.Next(_poolSize);
        }

        public void Dispose()
        {
            foreach (var multiplexerWrapper in _pool)
            {
                multiplexerWrapper.Dispose();
            }
        }
    }
}
