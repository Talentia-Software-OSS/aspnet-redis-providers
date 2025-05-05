using System;
using System.Threading.Tasks;
using System.Web.SessionState;
using Microsoft.Web.Redis;

namespace Microsoft.Web.Redis.Tests
{
    /// <summary>
    /// A testable mock implementation of ICacheConnection
    /// </summary>
    public class RedisConnectionMock : ICacheConnection
    {
        public Microsoft.Web.Redis.KeyGenerator Keys { get; set; }
        
        public ISessionStateItemCollection SessionData { get; set; }
        
        public object LockId { get; set; }
        
        public int SessionTimeout { get; set; }
        
        public bool LockSuccess { get; set; }
        
        public RedisConnectionMock()
        {
            Keys = new Microsoft.Web.Redis.KeyGenerator("test-session-id", "testapp");
            SessionData = new SessionStateItemCollection();
            LockId = Guid.NewGuid().ToString();
            SessionTimeout = 30 * 60; // 30 minutes in seconds
            LockSuccess = true;
        }
        
        public Task<TimeSpan> GetLockAgeAsync(object lockId)
        {
            return Task.FromResult(TimeSpan.FromMinutes(1));
        }
        
        public Task ReleaseLockIfLockIdMatchAsync(object lockId, int sessionTimeout)
        {
            return Task.CompletedTask;
        }
        
        public Task RemoveAndReleaseLockAsync(object lockId)
        {
            return Task.CompletedTask;
        }
        
        public Task SetAsync(ISessionStateItemCollection data, int sessionTimeout)
        {
            SessionData = data;
            SessionTimeout = sessionTimeout;
            return Task.CompletedTask;
        }
        
        public Task<(bool Success, object LockId, ISessionStateItemCollection Data, int SessionTimeout)> TryCheckWriteLockAndGetDataAsync()
        {
            return Task.FromResult((LockSuccess, LockId, SessionData, SessionTimeout));
        }
        
        public Task<(bool Success, object LockId, ISessionStateItemCollection Data, int SessionTimeout)> TryTakeWriteLockAndGetDataAsync(DateTime lockTime, int lockTimeout)
        {
            return Task.FromResult((LockSuccess, LockId, SessionData, SessionTimeout));
        }
        
        public Task UpdateAndReleaseLockAsync(object lockId, ISessionStateItemCollection data, int sessionTimeout)
        {
            SessionData = data;
            SessionTimeout = sessionTimeout;
            return Task.CompletedTask;
        }
        
        public Task UpdateExpiryTimeAsync(int timeToExpireInSeconds)
        {
            SessionTimeout = timeToExpireInSeconds;
            return Task.CompletedTask;
        }
    }
} 