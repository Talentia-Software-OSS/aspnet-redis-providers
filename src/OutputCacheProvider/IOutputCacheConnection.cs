//
// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
//

using System;
using System.Threading.Tasks;

namespace Microsoft.Web.Redis
{
    internal interface IOutputCacheConnection
    {
        Task SetAsync(string key, object entry, DateTime utcExpiry);        
        Task<object> AddAsync(string key, object entry, DateTime utcExpiry);
        Task<object> GetAsync(string key);
        Task RemoveAsync(string key);   
    }
}
