//
// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
//

using System;
using StackExchange.Redis;
using System.Reflection;
using System.Security.Authentication;

namespace Microsoft.Web.Redis
{
    internal class RedisSharedConnection : IDisposable
    {
        private ConfigurationOptions _configOption;
        private ConnectionMultiplexerPool _connectionPool;

        // Used for mocking in testing
        internal RedisSharedConnection()
        { }

        public RedisSharedConnection(ProviderConfiguration configuration)
        {
            _configOption = new ConfigurationOptions();

            // If connection string is given then use it otherwise use individual options
            if (!string.IsNullOrEmpty(configuration.ConnectionString))
            {
                _configOption = ConfigurationOptions.Parse(configuration.ConnectionString);
                // Setting explicitly 'abortconnect' to false. It will overwrite customer provided value for 'abortconnect'
                // As it doesn't make sense to allow to customer to set it to true as we don't give them access to ConnectionMultiplexer
                // in case of failure customer can not create ConnectionMultiplexer so right choice is to automatically create it by providing AbortOnConnectFail = false
                _configOption.AbortOnConnectFail = false;
            }
            else
            {
                if (configuration.Port == 0)
                {
                    _configOption.EndPoints.Add(configuration.Host);
                }
                else
                {
                    _configOption.EndPoints.Add(configuration.Host + ":" + configuration.Port);
                }
                _configOption.Password = configuration.AccessKey;
                _configOption.Ssl = configuration.UseSsl;
                _configOption.SslProtocols = SslProtocols.None;
                _configOption.AbortOnConnectFail = false;

                if (configuration.ConnectionTimeoutInMilliSec != 0)
                {
                    _configOption.ConnectTimeout = configuration.ConnectionTimeoutInMilliSec;
                }

                if (configuration.OperationTimeoutInMilliSec != 0)
                {
                    _configOption.SyncTimeout = configuration.OperationTimeoutInMilliSec; //async timeout follows sync timeout if no explicit value set
                }
            }
            if(configuration.SocketThreads != ProviderConfiguration.DefaultSocketThreads || configuration.SocketManagerOptions != ProviderConfiguration.DefaultSocketManagerOptions)
            {
                LogUtility.LogInfo($"Using custom socket manager to connect to Redis. SocketThreads: {configuration.SocketThreads}, SocketManagerOptions: {configuration.SocketManagerOptions}");
                _configOption.SocketManager = new SocketManager("SessionStateSocketManager", configuration.SocketThreads, (SocketManager.SocketManagerOptions)configuration.SocketManagerOptions);
            }

            // Embed Provider Identity in Connection ClientName
            if (String.IsNullOrWhiteSpace(_configOption.ClientName))
            {
                AssemblyName provider = Assembly.GetExecutingAssembly().GetName();
                _configOption.ClientName = $"{_configOption.Defaults.ClientName}({provider.Name}-v{provider.Version})";
            }

            _connectionPool = new ConnectionMultiplexerPool(configuration, _configOption);
        }

        public ConnectionMultiplexerWrapper GetMultiplexerBoundToSession(string sessionId) => _connectionPool.GetPooledMultiplexer(sessionId);

        public ConnectionMultiplexerWrapper GetMultiplexerForTesting() => _connectionPool.GetPooledMultiplexer(null);

        public void Dispose()
        {
           _connectionPool.Dispose(); ;
        }
    }
}
