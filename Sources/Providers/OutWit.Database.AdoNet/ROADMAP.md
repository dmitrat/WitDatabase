# OutWit.Database.AdoNet - Version 2.0 Roadmap

**Last Updated:** 2026-01-20

This document outlines planned features for version 2.0 of OutWit.Database.AdoNet.

---

## Version 2.0 - Planned Features

### Priority 1: High Value

| Feature | Description |
|---------|-------------|
| Connection Pool Monitoring | Metrics for pool usage, wait times |
| Batch Commands | DbBatch support for multiple commands |
| Command Timeout Cancellation | Proper cancellation token integration |

### Priority 2: Enhancements

| Feature | Description |
|---------|-------------|
| Connection Resiliency | Auto-retry on transient failures |
| Health Checks | IHealthCheck implementation for ASP.NET |
| Diagnostics | DiagnosticSource events for APM integration |
| Connection String Encryption | Encrypt sensitive parts of connection string |

---

## Implementation Details

### Connection Pool Monitoring (Priority 1)

```csharp
public class ConnectionPoolMetrics
{
    public int TotalConnections { get; }
    public int AvailableConnections { get; }
    public int InUseConnections { get; }
    public long TotalConnectionsCreated { get; }
    public long TotalConnectionsDisposed { get; }
    public TimeSpan AverageWaitTime { get; }
    public TimeSpan MaxWaitTime { get; }
}

public static class ConnectionPool
{
    public static ConnectionPoolMetrics GetMetrics(string connectionString);
    public static event EventHandler<ConnectionPoolEventArgs> ConnectionAcquired;
    public static event EventHandler<ConnectionPoolEventArgs> ConnectionReleased;
}
```

### Batch Commands (Priority 1)

Support for DbBatch (ADO.NET 6.0+):

```csharp
public class WitDbBatch : DbBatch
{
    public override DbBatchCommandCollection BatchCommands { get; }
    
    public override int ExecuteNonQuery();
    public override DbDataReader ExecuteReader();
    public override object? ExecuteScalar();
    
    // Async variants
}

public class WitDbBatchCommand : DbBatchCommand
{
    public override string CommandText { get; set; }
    public override CommandType CommandType { get; set; }
    public override DbParameterCollection Parameters { get; }
}
```

### Health Checks (Priority 2)

ASP.NET Core health check integration:

```csharp
public class WitDbHealthCheck : IHealthCheck
{
    private readonly string _connectionString;
    private readonly string? _query;
    
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new WitDbConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            
            if (_query != null)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = _query;
                await cmd.ExecuteScalarAsync(cancellationToken);
            }
            
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(exception: ex);
        }
    }
}

// Extension method
public static IHealthChecksBuilder AddWitDb(
    this IHealthChecksBuilder builder,
    string connectionString,
    string? query = null,
    string? name = null);
```

### Diagnostics (Priority 2)

```csharp
public static class WitDbDiagnostics
{
    public static readonly DiagnosticListener Listener = 
        new DiagnosticListener("OutWit.Database.AdoNet");
    
    // Events:
    // - ConnectionOpening / ConnectionOpened
    // - CommandExecuting / CommandExecuted
    // - TransactionStarting / TransactionStarted
    // - TransactionCommitting / TransactionCommitted
}
```

---

## See Also

- [README.md](README.md) - Project documentation
- [ROADMAP.md](../../../ROADMAP.md) - Main project roadmap
