using System;
using System.Threading;
using StackExchange.Redis;

namespace Microsoft.Web.Redis
{
    public class ConnectionMultiplexerWrapper : IDisposable
    {
        private static TimeSpan _reconnectFrequency = TimeSpan.FromSeconds(60);
        private static TimeSpan _reconnectErrorThreshold = TimeSpan.FromSeconds(30);

        private readonly ConfigurationOptions _configOption;
        private readonly ProviderConfiguration _configuration;
        private Lazy<ConnectionMultiplexer> _redisMultiplexer;
        private DateTimeOffset _firstErrorTime = DateTimeOffset.MinValue;
        private DateTimeOffset _previousErrorTime = DateTimeOffset.MinValue;
        private DateTimeOffset _lastReconnectTime;
        private readonly int _id;
        private readonly object reconnectLock = new object();
        private readonly ReaderWriterLockSlim _multiplexerLock = new ReaderWriterLockSlim();

        internal ConnectionMultiplexerWrapper(ProviderConfiguration configuration, ConfigurationOptions configOption,
            int id)
        {
            _configuration = configuration;
            _configOption = configOption;
            _lastReconnectTime = DateTimeOffset.UtcNow;
            _id = id;
            CreateMultiplexer();
        }

        public IDatabase Database
        {
            get
            {
                _multiplexerLock.EnterReadLock();
                try
                {
                    return _redisMultiplexer.Value.GetDatabase(_configOption.DefaultDatabase ?? _configuration.DatabaseId);
                }
                finally
                {
                    _multiplexerLock.ExitReadLock();
                }
            }
        }

        public void ForceReconnect()
        {
            var previousReconnect = _lastReconnectTime;
            var elapsedSinceLastReconnect = DateTimeOffset.UtcNow - previousReconnect;

            // If multiple threads call ForceReconnect at the same time, we only want to honor one of them. 
            if (elapsedSinceLastReconnect > _reconnectFrequency)
            {
                lock (reconnectLock)
                {
                    var utcNow = DateTimeOffset.UtcNow;
                    elapsedSinceLastReconnect = utcNow - _lastReconnectTime;

                    if (elapsedSinceLastReconnect < _reconnectFrequency)
                    {
                        return; // Some other thread made it through the check and the lock, so nothing to do. 
                    }

                    if (_firstErrorTime == DateTimeOffset.MinValue)
                    {
                        // We got error first time after last reconnect
                        _firstErrorTime = utcNow;
                        _previousErrorTime = utcNow;
                        return;
                    }

                    var elapsedSinceFirstError = utcNow - _firstErrorTime;
                    var elapsedSinceMostRecentError = utcNow - _previousErrorTime;
                    _previousErrorTime = utcNow;

                    if ((elapsedSinceFirstError >= _reconnectErrorThreshold) &&
                        (elapsedSinceMostRecentError <= _reconnectErrorThreshold))
                    {
                        LogUtility.LogInfo($"Multiplexer {_id} ForceReconnect: now: {utcNow.ToString()}");
                        LogUtility.LogInfo(
                            $"Multiplexer {_id} ForceReconnect: elapsedSinceLastReconnect: {elapsedSinceLastReconnect.ToString()}, ReconnectFrequency: {_reconnectFrequency.ToString()}");
                        LogUtility.LogInfo(
                            $"Multiplexer {_id} ForceReconnect: elapsedSinceFirstError: {elapsedSinceFirstError.ToString()}, elapsedSinceMostRecentError: {elapsedSinceMostRecentError.ToString()}, ReconnectErrorThreshold: {_reconnectErrorThreshold.ToString()}");

                        _firstErrorTime = DateTimeOffset.MinValue;
                        _previousErrorTime = DateTimeOffset.MinValue;

                        _multiplexerLock.EnterWriteLock();
                        try
                        {
                            CloseMultiplexer();
                            CreateMultiplexer();
                        }
                        finally
                        {
                            _multiplexerLock.ExitWriteLock();
                        }
                    }
                }
            }
        }

        private void CreateMultiplexer()
        {
            if (LogUtility.logger == null)
            {
                _redisMultiplexer = new Lazy<ConnectionMultiplexer>(() => ConnectionMultiplexer.Connect(_configOption));
            }
            else
            {
                _redisMultiplexer = new Lazy<ConnectionMultiplexer>(() => ConnectionMultiplexer.Connect(_configOption, LogUtility.logger));
            }
            _lastReconnectTime = DateTimeOffset.UtcNow;
        }

        private void CloseMultiplexer()
        {
            if (_redisMultiplexer.Value != null)
            {
                try
                {
                    _redisMultiplexer.Value.Close();
                }
                catch (Exception)
                {
                    // Example error condition: if accessing old.Value causes a connection attempt and that fails. 
                }
            }
        }

        public void Dispose()
        {
            if (_redisMultiplexer != null && _redisMultiplexer.IsValueCreated)
            {
                try
                {
                    _redisMultiplexer.Value.Close();
                }
                catch (Exception)
                {
                    // Ignore exceptions during disposal
                }
            }
        }
    }
}
