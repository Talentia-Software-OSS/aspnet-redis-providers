using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Web.Redis;
using StackExchange.Redis;

namespace Microsoft.Web.Redis
{
    public class ConnectionMultiplexerPool
    {
        private readonly List<ConnectionMultiplexerWrapper> _pool;
        private int _poolSize;
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
        public ConnectionMultiplexerWrapper GetPooledMultiplexer()
        {
            var indexToPick = _random.Next(_poolSize - 1);
            return _pool[indexToPick];
            //ConnectionMultiplexerWrapper connection;
            //while (!_pool.TryDequeue(out connection)) continue;
            //_pool.Enqueue(connection);
            //return connection;

        }
      
    }
}
