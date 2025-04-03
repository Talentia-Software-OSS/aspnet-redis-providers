using Microsoft.Web.Redis.Tests;
using Xunit;

namespace Microsoft.Web.Redis.FunctionalTests
{
    public class StackExchangeClientConnectionFunctionalTests
    {
        // Use a shared RedisServer instance across all tests
        private static readonly RedisServer sharedRedisServer = new RedisServer();

        [Fact()]
        public void Constructor_DatabaseIdFromConfigurationProperty()
        {
            int databaseId = 7;
            ProviderConfiguration configuration = Utility.GetDefaultConfigUtility();
            configuration.DatabaseId = databaseId;

            StackExchangeClientConnection connection = GetStackExchangeClientConnection(configuration);

            Assert.Equal(databaseId, connection.RealConnection.Database);
        }

        [Fact()]
        public void Constructor_DatabaseIdFromConnectionString()
        {
            int databaseId = 3;
            ProviderConfiguration configuration = Utility.GetDefaultConfigUtility();
            configuration.ConnectionString = string.Format("localhost, defaultDatabase={0}", databaseId);

            StackExchangeClientConnection connection = GetStackExchangeClientConnection(configuration);

            Assert.Equal(databaseId, connection.RealConnection.Database);
        }

        [Fact()]
        public void Constructor_DatabaseIdFromConfigurationPropertyWhenNotSetInConnectionString()
        {
            int databaseId = 5;
            ProviderConfiguration configuration = Utility.GetDefaultConfigUtility();
            configuration.DatabaseId = databaseId;
            configuration.ConnectionString = string.Format("localhost");

            StackExchangeClientConnection connection = GetStackExchangeClientConnection(configuration);

            Assert.Equal(databaseId, connection.RealConnection.Database);
        }

        private StackExchangeClientConnection GetStackExchangeClientConnection(ProviderConfiguration configuration)
        {
            var sharedConnection = new RedisSharedConnection(configuration);
            try
            {
                return new StackExchangeClientConnection(configuration, sharedConnection);
            }
            catch
            {
                // Make sure to dispose the shared connection if connection creation fails
                sharedConnection.Dispose();
                throw;
            }
        }
    }
}