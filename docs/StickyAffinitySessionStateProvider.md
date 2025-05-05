# StickyAffinitySessionStateProvider

The `StickyAffinitySessionStateProvider` extends the standard `RedisSessionStateProvider` with in-memory caching capabilities, offering improved performance and resilience for ASP.NET session state management in environments with sticky sessions (session affinity).

## Benefits

- **Reduced Redis Read Operations**: Frequently accessed session data is served from the memory cache, reducing Redis read traffic.
- **Optional Batched Write Operations**: Session updates can be batched and sent to Redis on a configurable timer, improving performance for write-heavy applications.
- **Improved Availability**: Session data remains available in the local memory cache even during brief Redis outages.
- **Transparent Fallback**: If data isn't in the memory cache, it automatically falls back to Redis (just like the standard provider).
- **Configurable Behavior**: Memory cache usage, bulk update mode, and timers are all configurable.

## How It Works

1. **Reading Session Data**: 
   - First checks the in-memory cache for the session data
   - If found, returns it directly without accessing Redis
   - If not found, falls back to Redis and caches the result for future reads
   - Session affinity ensures subsequent reads for the same session will hit the memory cache

2. **Writing Session Data**:
   - Always updates the in-memory cache first
   - If bulk updates are disabled, immediately updates Redis
   - If bulk updates are enabled, adds the session ID to a queue and releases the Redis lock
   - A timer periodically processes the queue and updates Redis with the latest data

3. **Exclusive Access**:
   - Can be configured to use memory cache for `GetItemExclusive` calls
   - When enabled, it gets data from memory cache but still acquires a Redis lock
   - This maintains distributed locking while avoiding Redis reads

4. **Removing Session Data**:
   - Removes from both memory cache and Redis

## Configuration Options

| Option | Default | Description |
|--------|---------|-------------|
| `enableMemoryCache` | `true` | Enables or disables the memory cache functionality |
| `enableBulkUpdates` | `false` | When enabled, Redis writes are batched and processed on a timer |
| `bulkUpdateIntervalSeconds` | `60` | How often the bulk update timer processes pending updates |
| `memoryCacheExpiryMinutes` | `20` | How long items remain in the memory cache before expiring |
| `useMemoryCacheForExclusiveAccess` | `true` | Whether to use memory cache data for exclusive access operations |

## Configuration Example

```xml
<sessionState mode="Custom" customProvider="StickyAffinitySessionStateProvider">
  <providers>
    <add name="StickyAffinitySessionStateProvider" 
         type="Microsoft.Web.Redis.StickyAffinitySessionStateProvider" 
         connectionString="localhost:6379,ssl=false" 
         applicationName="MyApp" 
         databaseId="0" 
         throwOnError="true"
         
         <!-- StickyAffinity provider specific settings -->
         enableMemoryCache="true"
         enableBulkUpdates="true" 
         bulkUpdateIntervalSeconds="60"
         memoryCacheExpiryMinutes="20"
         useMemoryCacheForExclusiveAccess="true"
    />
  </providers>
</sessionState>
```

## Usage Recommendations

1. **Sticky Session Environments**: This provider is specifically optimized for environments where session affinity (sticky sessions) is enabled, ensuring requests for a session are routed to the same server.

2. **Read-Heavy Applications**: The memory cache significantly improves performance for applications that read session data more often than they write it.

3. **Write-Heavy Applications**: Consider enabling bulk updates when session data is frequently updated by multiple requests in a short time period.

4. **Large Sessions**: For applications with very large session objects, be conscious of memory usage on the web server.

## Performance Considerations

- **Memory Usage**: The memory cache stores session data in the web server's memory. Monitor memory usage, especially for large session objects.

- **Bulk Updates**: While batching writes can improve performance, it introduces a delay between when data is written to the memory cache and when it's persisted to Redis. Consider this when determining the appropriate update interval.

- **Session Affinity**: This provider works best in environments with session affinity enabled. Without session affinity, different servers might have different versions of the session data in their memory caches.

## Implementation Notes

- The provider is thread-safe and uses concurrent collections for memory caching.
- Expired cache entries are automatically removed via a background cleanup timer.
- When Redis is unavailable, the memory cache will continue to serve data until it expires.
- The `useMemoryCacheForExclusiveAccess` option allows you to balance between performance and data consistency in write operations.
- Redis locks are still acquired even when using memory cache data to ensure compatibility with distributed scenarios.
- Data is automatically cached in memory after being retrieved from Redis, ensuring subsequent reads are faster. 