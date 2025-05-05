using System;
using System.Web.SessionState;

namespace Microsoft.Web.Redis.Tests
{
    internal class Utility
    {
        // Basic utility method used by most tests
        public static void SetConfigUtilityToDefault()
        {
            // This method used to set up test configurations
            // Simplified for our tests
        }

        // Helper method to create a new SessionStateItemCollection
        public static ISessionStateItemCollection SessionStateItemCollection()
        {
            return new SessionStateItemCollection();
        }

        // Helper method to compare SessionStateStoreData objects
        public static bool CompareSessionStateStoreData(SessionStateStoreData obj1, SessionStateStoreData obj2)
        {
            if ((obj1 == null && obj2 != null) || (obj1 != null && obj2 == null))
            {
                return false;
            }
            else if (obj1 != null && obj2 != null)
            {
                if (obj1.Timeout != obj2.Timeout)
                {
                    return false;
                }

                System.Collections.Specialized.NameObjectCollectionBase.KeysCollection keys1 = obj1.Items.Keys;
                System.Collections.Specialized.NameObjectCollectionBase.KeysCollection keys2 = obj2.Items.Keys;

                if ((keys1 != null && keys2 == null) || (keys1 == null && keys2 != null))
                {
                    return false;
                }
                else if (keys1 != null && keys2 != null)
                {
                    foreach (string key in keys1)
                    {
                        if (obj2.Items[key] == null)
                        {
                            return false;
                        }
                    }
                }
            }
            return true;
        }

        // Helper method to get default configuration utility
        public static ProviderConfiguration GetDefaultConfigUtility()
        {
            var config = new ProviderConfiguration();
            config.RetryTimeout = TimeSpan.FromMilliseconds(100);
            config.ThrowOnError = false;
            config.ApplicationName = "DefaultAppName";
            config.Host = "localhost";
            config.Port = 6379;
            config.RequestTimeout = TimeSpan.FromSeconds(30);
            config.SessionTimeout = TimeSpan.FromMinutes(20);
            config.ConnectionTimeoutInMilliSec = 500;
            config.OperationTimeoutInMilliSec = 3000;
            return config;
        }
    }

    // Simple ConfigUtility class for testing
    internal class ConfigUtility
    {
        public int RetryTimeout { get; private set; }
        public bool ThrowOnError { get; private set; }
        public bool OperationTimeoutExceptionEnabled { get; private set; }
        public int ConnectionTimeout { get; private set; }
        public int OperationTimeout { get; private set; }
        public int RetrySleep { get; private set; }
        public bool EnableLogging { get; private set; }
        public bool EnableSharedCompressedSession { get; private set; }
        public string ApplicationName { get; private set; }

        public ConfigUtility(
            int retryTimeout,
            bool throwOnError,
            bool operationTimeoutExceptionEnabled,
            int connectionTimeout,
            int operationTimeout,
            int retrySleep,
            bool enableLogging,
            bool enableSharedCompressedSession,
            string applicationName)
        {
            RetryTimeout = retryTimeout;
            ThrowOnError = throwOnError;
            OperationTimeoutExceptionEnabled = operationTimeoutExceptionEnabled;
            ConnectionTimeout = connectionTimeout;
            OperationTimeout = operationTimeout;
            RetrySleep = retrySleep;
            EnableLogging = enableLogging;
            EnableSharedCompressedSession = enableSharedCompressedSession;
            ApplicationName = applicationName;
        }
    }
} 