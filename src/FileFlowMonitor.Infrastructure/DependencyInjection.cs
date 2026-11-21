using FileFlowMonitor.Application.Abstractions;
using FileFlowMonitor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FileFlowMonitor.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<FileFlowDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IMonitoringRuleRepository, MonitoringRuleRepository>();
        return services;
    }
}
