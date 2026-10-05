using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Refidomsa.Api.Data;

public class SqlServerHealthCheck : IHealthCheck
{
    private readonly AppDbContext _db;

    public SqlServerHealthCheck(AppDbContext db)
    {
        _db = db;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (await _db.Database.CanConnectAsync(cancellationToken))
        {
            return HealthCheckResult.Healthy();
        }
        return HealthCheckResult.Unhealthy("SQL Server no esta disponible.");
    }
}
