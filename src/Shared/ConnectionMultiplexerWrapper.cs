using System;
using System.Threading;
using Microsoft.Web.Redis;
using StackExchange.Redis;

namespace Microsoft.Web.Redis
{
    public class ConnectionMultiplexerWrapper
    {
        private static TimeSpan ReconnectFrequency = TimeSpan.FromSeconds(60);
        private static TimeSpan ReconnectErrorThreshold = TimeSpan.FromSeconds(30);

        private ConfigurationOptions _configOption;
        private ProviderConfiguration _configuration;
        private Lazy<ConnectionMultiplexer> _redisMultiplexer;
        private DateTimeOffset _lastReconnectTime = DateTimeOffset.MinValue;
        private DateTimeOffset _firstErrorTime = DateTimeOffset.MinValue;
        private DateTimeOffset _previousErrorTime = DateTimeOffset.MinValue;
        private int _id;
        private object reconnectLock = new object();
        private ReaderWriterLockSlim _multiplexerLock = new ReaderWriterLockSlim();

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
            if (elapsedSinceLastReconnect > ReconnectFrequency)
            {
                lock (reconnectLock)
                {
                    var utcNow = DateTimeOffset.UtcNow;
                    elapsedSinceLastReconnect = utcNow - _lastReconnectTime;

                    if (elapsedSinceLastReconnect < ReconnectFrequency)
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

                    if ((elapsedSinceFirstError >= ReconnectErrorThreshold) &&
                        (elapsedSinceMostRecentError <= ReconnectErrorThreshold))
                    {
                        LogUtility.LogInfo($"Multiplexer {_id} ForceReconnect: now: {utcNow.ToString()}");
                        LogUtility.LogInfo(
                            $"Multiplexer {_id} ForceReconnect: elapsedSinceLastReconnect: {elapsedSinceLastReconnect.ToString()}, ReconnectFrequency: {ReconnectFrequency.ToString()}");
                        LogUtility.LogInfo(
                            $"Multiplexer {_id} ForceReconnect: elapsedSinceFirstError: {elapsedSinceFirstError.ToString()}, elapsedSinceMostRecentError: {elapsedSinceMostRecentError.ToString()}, ReconnectErrorThreshold: {ReconnectErrorThreshold.ToString()}");

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

    }

}
