using System;
using Xunit;
using FakeItEasy;
using System.Web.SessionState;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Specialized;
using Microsoft.AspNet.SessionState;
using System.Web.Caching;

namespace Microsoft.Web.Redis.Tests
{
    public class StickyAffinitySessionStateProviderTests
    {
        [Fact]
        public void Initialize_WithConfiguration()
        {
            NameValueCollection config = new NameValueCollection();
            config.Add("enableMemoryCache", "false");
            config.Add("enableBulkUpdates", "true");
            config.Add("bulkUpdateIntervalSeconds", "120");
            config.Add("memoryCacheExpiryMinutes", "30");
            config.Add("useMemoryCacheForExclusiveAccess", "false");
            
            Utility.SetConfigUtilityToDefault();
            StickyAffinitySessionStateProvider sessionStateStore = new StickyAffinitySessionStateProvider();
            
            // No exception should be thrown
            sessionStateStore.Initialize("test", config);
        }

        [Fact]
        public void Initialize_WithBulkUpdates()
        {
            NameValueCollection config = new NameValueCollection();
            config.Add("enableMemoryCache", "true");
            config.Add("enableBulkUpdates", "true");
            config.Add("bulkUpdateIntervalSeconds", "120");
            
            Utility.SetConfigUtilityToDefault();
            StickyAffinitySessionStateProvider sessionStateStore = new StickyAffinitySessionStateProvider();
            
            // No exception should be thrown
            sessionStateStore.Initialize("test", config);
        }

        [Fact]
        public void Initialize_WithoutBulkUpdates()
        {
            NameValueCollection config = new NameValueCollection();
            config.Add("enableMemoryCache", "true");
            config.Add("enableBulkUpdates", "false");
            
            Utility.SetConfigUtilityToDefault();
            StickyAffinitySessionStateProvider sessionStateStore = new StickyAffinitySessionStateProvider();
            
            // No exception should be thrown
            sessionStateStore.Initialize("test", config);
        }
    }
}