//
// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
//

using System;
using System.Threading.Tasks;
using System.Web.SessionState;

namespace Microsoft.Web.Redis
{
    internal interface IRedisClientConnection
    {
        Task<bool> ExpiryAsync(string key, int timeInSeconds);
        Task<object> EvalAsync(string script, string[] keyArgs, object[] valueArgs);
        Task SetAsync(string key, byte[] data, DateTime utcExpiry);
        Task<byte[]> GetAsync(string key);
        Task RemoveAsync(string key);

        ISessionStateItemCollection GetSessionData(object rowDataFromRedis);
        byte[] GetOutputCacheDataFromResult(object rowDataFromRedis);
        string GetLockId(object rowDataFromRedis);
        int GetSessionTimeout(object rowDataFromRedis);
        bool IsLocked(object rowDataFromRedis);
    }
}
