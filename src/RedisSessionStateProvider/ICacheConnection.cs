//
// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
//

using System;
using System.Threading.Tasks;
using System.Web.SessionState;

namespace Microsoft.Web.Redis
{
    internal interface ICacheConnection
    {
        KeyGenerator Keys { get; set; }
        Task SetAsync(ISessionStateItemCollection data, int sessionTimeout);
        Task UpdateExpiryTimeAsync(int timeToExpireInSeconds);
        Task<(bool Success, object LockId, ISessionStateItemCollection Data, int SessionTimeout)> TryTakeWriteLockAndGetDataAsync(DateTime lockTime, int lockTimeout);
        Task<(bool Success, object LockId, ISessionStateItemCollection Data, int SessionTimeout)> TryCheckWriteLockAndGetDataAsync();
        Task ReleaseLockIfLockIdMatchAsync(object lockId, int sessionTimeout);
        Task RemoveAndReleaseLockAsync(object lockId);
        Task UpdateAndReleaseLockAsync(object lockId, ISessionStateItemCollection data, int sessionTimeout);
        Task<TimeSpan> GetLockAgeAsync(object lockId);
    }
}
