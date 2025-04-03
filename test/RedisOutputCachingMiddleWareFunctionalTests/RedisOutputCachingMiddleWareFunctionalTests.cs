using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Web.Redis.FunctionalTests;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Microsoft.Web.Redis.FunctionalTests
{
    public class RedisOutputCachingMiddleWareFunctionalTests
    {
        private readonly ITestOutputHelper _output;
        
        public RedisOutputCachingMiddleWareFunctionalTests(ITestOutputHelper output)
        {
            _output = output;
        }
        
        private string GetUnixTimeSeconds()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        }

        [Fact]
        public async Task TestWithoutCacheAsync()
        {
            if(RedisServer.IsRedisRunning())
            {
                RedisServer.KillRedisServers();
            }
            
            _output.WriteLine("Starting test with no Redis server...");
            // Wait to ensure Redis is really gone
            Thread.Sleep(1000);
            
            // This test is expected to have different responses (no caching)
            bool isResponseCurrent = await ResponseIsCurrentAysnc();
            Assert.True(isResponseCurrent);
        }

        [Fact]
        public async Task TestWithCacheAsync()
        {
            // Make sure there's no Redis server running from previous tests
            RedisServer.KillRedisServers();
            Thread.Sleep(1000);
            
            _output.WriteLine("Starting test with Redis server...");
            // This test is expected to have the same response (cached)
            RedisServer server = new RedisServer();
            
            try
            {
                // Wait for Redis to be fully ready
                _output.WriteLine($"Redis server is running: {RedisServer.IsRedisRunning()}");
                Thread.Sleep(1000);
                
                bool isResponseCurrent = await ResponseIsCurrentAysnc();
                _output.WriteLine($"Test completed, isResponseCurrent: {isResponseCurrent}");
                Assert.False(isResponseCurrent);
            }
            finally
            {
                server.Dispose();
                _output.WriteLine("Redis server disposed");
            }
        }

        [Fact]
        public async Task TtlTestLessAsync()
        {
            // Make sure there's no Redis server running from previous tests
            RedisServer.KillRedisServers();
            Thread.Sleep(1000);
            
            _output.WriteLine("Starting TTL test with short expiry...");
            // This test is expected to have different responses (TTL expired)
            RedisServer server = new RedisServer();
            
            try
            {
                // Wait for Redis to be fully ready
                _output.WriteLine($"Redis server is running: {RedisServer.IsRedisRunning()}");
                Thread.Sleep(1000);
                
                bool isResponseCurrent = await ResponseIsCurrentAysnc(1);
                _output.WriteLine($"Test completed, isResponseCurrent: {isResponseCurrent}");
                Assert.True(isResponseCurrent);
            }
            finally
            {
                server.Dispose();
                _output.WriteLine("Redis server disposed");
            }
        }

        [Fact]
        public async Task TtlTestGreaterAsync()
        {
            // Make sure there's no Redis server running from previous tests
            RedisServer.KillRedisServers();
            Thread.Sleep(1000);
            
            _output.WriteLine("Starting TTL test with long expiry...");
            // This test is expected to have the same response (cached, within TTL)
            RedisServer server = new RedisServer();
            
            try
            {
                // Wait for Redis to be fully ready
                _output.WriteLine($"Redis server is running: {RedisServer.IsRedisRunning()}");
                Thread.Sleep(1000);
                
                bool isResponseCurrent = await ResponseIsCurrentAysnc(7);
                _output.WriteLine($"Test completed, isResponseCurrent: {isResponseCurrent}");
                Assert.False(isResponseCurrent);
            }
            finally
            {
                server.Dispose();
                _output.WriteLine("Redis server disposed");
            }
        }

        private async Task<bool> ResponseIsCurrentAysnc(int ttl = int.MaxValue)
        {
            _output.WriteLine($"Setting up test with TTL: {ttl}");
            
            using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddControllersWithViews();
                    })
                    .Configure(app =>
                    {
                        _output.WriteLine("Configuring middleware");
                        app.UseMiddleware<RedisOutputCache>("localhost", ttl);
                        app.Run(async context =>
                        {
                            var time = GetUnixTimeSeconds();
                            await context.Response.WriteAsync(time);
                        });
                    });
            })
            .StartAsync();

            // First request - record the response
            _output.WriteLine("Making first request");
            var firstResponse = await host.GetTestClient().GetAsync("/");
            var firstResponseBody = await firstResponse.Content.ReadAsStringAsync();
            _output.WriteLine($"First response: {firstResponseBody}");
            
            // Sleep to ensure time moves forward
            _output.WriteLine("Sleeping for 4 seconds");
            Thread.Sleep(4000);
            
            // Second request - check if cached
            _output.WriteLine("Making second request");
            var secondResponse = await host.GetTestClient().GetAsync("/");
            var secondResponseBody = await secondResponse.Content.ReadAsStringAsync();
            _output.WriteLine($"Second response: {secondResponseBody}");
            
            // Compare responses - if they're the same, we got a cached value
            bool responsesSame = firstResponseBody == secondResponseBody;
            _output.WriteLine($"Responses are same: {responsesSame}, TTL: {ttl}");
            
            // For the purpose of these tests:
            // "Current" means the response has the current time (responses are different)
            // "Not current" means the response is cached (responses are the same)
            // 
            // When Redis is running with sufficient TTL, responses should be the same (cached)
            // When Redis isn't running or TTL is very low, responses should be different (not cached)
            return !responsesSame; // If responses are different, it's current
        }
    }
}