using JD.Worker.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JD.Worker.Core;

public static class WorkerRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddWorkerRuntime(this IServiceCollection services, WorkerConfig config)
    {
        services.AddWorkerCore();
        services.AddSingleton(config);
        services.AddSingleton<IConnectorRegistry, InMemoryConnectorRegistry>();
        services.AddHttpClient();

        var workspaceRoot = config.Worker.Workspace?.Root;
        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            services.AddSingleton(new WorkspaceManagerOptions { RootPath = workspaceRoot });
        }

        services.AddSingleton<IJobExecutor, JobExecutor>();
        services.AddHostedService<JobSchedulerService>();
        services.AddHostedService<ConnectorListenerService>();
        services.AddHostedService<WorkerHeartbeatService>();
        return services;
    }
}
