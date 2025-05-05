using System;
using System.Threading.Tasks;
using System.Web.SessionState;
using Microsoft.Web.Redis;

namespace Microsoft.Web.Redis.Tests
{
    /// <summary>
    /// Adapter class for testing that implements ICacheConnection
    /// </summary>
    internal class AsyncCacheConnectionAdapter : ICacheConnection
    {
        public Microsoft.Web.Redis.KeyGenerator Keys { get; set; }

        public virtual Task<TimeSpan> GetLockAgeAsync(object lockId)
        {
            return Task.FromResult(TimeSpan.Zero);
        }

        public virtual Task ReleaseLockIfLockIdMatchAsync(object lockId, int sessionTimeout)
        {
            return Task.CompletedTask;
        }

        public virtual Task RemoveAndReleaseLockAsync(object lockId)
        {
            return Task.CompletedTask;
        }

        public virtual Task SetAsync(ISessionStateItemCollection data, int sessionTimeout)
        {
            return Task.CompletedTask;
        }

        public virtual Task<(bool Success, object LockId, ISessionStateItemCollection Data, int SessionTimeout)> TryCheckWriteLockAndGetDataAsync()
        {
            return Task.FromResult<(bool Success, object LockId, ISessionStateItemCollection Data, int SessionTimeout)>((false, null, null, 0));
        }

        public virtual Task<(bool Success, object LockId, ISessionStateItemCollection Data, int SessionTimeout)> TryTakeWriteLockAndGetDataAsync(DateTime lockTime, int lockTimeout)
        {
            return Task.FromResult<(bool Success, object LockId, ISessionStateItemCollection Data, int SessionTimeout)>((false, null, null, 0));
        }

        public virtual Task UpdateAndReleaseLockAsync(object lockId, ISessionStateItemCollection data, int sessionTimeout)
        {
            return Task.CompletedTask;
        }

        public virtual Task UpdateExpiryTimeAsync(int timeToExpireInSeconds)
        {
            return Task.CompletedTask;
        }
    }
} 