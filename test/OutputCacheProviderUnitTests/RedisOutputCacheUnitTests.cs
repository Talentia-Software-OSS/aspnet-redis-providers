//
// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
//

using System;
using Xunit;
using FakeItEasy;
using System.Collections.Specialized;
using System.Threading.Tasks;

namespace Microsoft.Web.Redis.UnitTests
{
    public class RedisOutputCacheUnitTests
    {
        [Fact]
        public void TryGet()
        {
            var fake = A.Fake<IOutputCacheConnection>();
            A.CallTo(() => fake.GetAsync("key1")).Returns(Task.FromResult<object>(new ArgumentException("foo")));
            RedisOutputCacheProvider cache = new RedisOutputCacheProvider();
            cache.cache = fake;
            var obj = cache.Get("key1");
            Assert.IsType<ArgumentException>(obj);
        }

        [Fact]
        public void TryAdd()
        {
            var fake = A.Fake<IOutputCacheConnection>();
            DateTime utcExpiry = DateTime.Now;
            A.CallTo(() => fake.AddAsync("key1", "object", utcExpiry)).Returns(Task.FromResult<object>(new ArgumentException("foo")));
            RedisOutputCacheProvider cache = new RedisOutputCacheProvider();
            cache.cache = fake;
            var obj = cache.Add("key1", "object", utcExpiry);
            Assert.IsType<ArgumentException>(obj);
        }
        [Fact]
        public void TrySet()
        {
            var fake = A.Fake<IOutputCacheConnection>();
            A.CallTo(() => fake.SetAsync("key1", "object", A<DateTime>.Ignored)).Returns(Task.CompletedTask);
            DateTime utcExpiry = DateTime.Now;
            RedisOutputCacheProvider cache = new RedisOutputCacheProvider();
            cache.cache = fake;
            cache.Set("key1", "object", DateTime.Now);
            A.CallTo(() => fake.SetAsync("key1", "object", A<DateTime>.Ignored)).MustHaveHappened();
        }
        [Fact]
        public void TryRemove()
        {
            var fake = A.Fake<IOutputCacheConnection>();
            A.CallTo(() => fake.RemoveAsync("key1")).Returns(Task.CompletedTask);
            DateTime utcExpiry = DateTime.Now;
            RedisOutputCacheProvider cache = new RedisOutputCacheProvider();
            cache.cache = fake;
            cache.Remove("key1");
            A.CallTo(() => fake.RemoveAsync("key1")).MustHaveHappened();
        }
        [Fact]
        public void TryInitialize()
        {
            var fake = A.Fake<IOutputCacheConnection>();
            RedisOutputCacheProvider cache = new RedisOutputCacheProvider();
            cache.cache = fake;
            NameValueCollection config = new NameValueCollection();
            config.Add("host", "localhost");
            config.Add("port", "1234");
            config.Add("accessKey", "hello world");
            config.Add("ssl", "true");
            config.Add("redisSerializerType", "Microsoft.Web.Redis.BinarySerializer");
            cache.Initialize("name", config);

            Assert.Equal("localhost", RedisOutputCacheProvider.configuration.Host);
            Assert.Equal(1234, RedisOutputCacheProvider.configuration.Port);
            Assert.Equal("hello world", RedisOutputCacheProvider.configuration.AccessKey);
            Assert.True(RedisOutputCacheProvider.configuration.UseSsl);
        }
    }
}
