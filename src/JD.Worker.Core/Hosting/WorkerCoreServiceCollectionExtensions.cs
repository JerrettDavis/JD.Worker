using JD.Worker.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace JD.Worker.Core;

public static class WorkerCoreServiceCollectionExtensions
{
    public static IServiceCollection AddWorkerCore(this IServiceCollection services)
    {
        services.AddSingleton<JobStateMachine>();
        services.AddSingleton<JobStateService>();
        services.AddSingleton<JobLeaseService>();
        services.AddSingleton<WorkerStatusTracker>();
        services.AddSingleton<IJobStore, InMemoryJobStore>();
        services.AddSingleton<ILogStore, InMemoryLogStore>();
        services.AddSingleton<IWorkerRegistry, InMemoryWorkerRegistry>();
        services.AddSingleton<IJobScheduler, InMemoryJobScheduler>();
        services.AddSingleton<IJobExecutor, NoopJobExecutor>();
        services.AddSingleton<SecretRedactor>();
        services.AddSingleton(new WorkspaceManagerOptions());
        services.AddSingleton<WorkspaceManager>();
        services.AddSingleton<IStepRunner, ShellStepRunner>();
        services.AddSingleton<IStepRunner, ProcessStepRunner>();
        services.AddSingleton<IStepRunner, DockerStepRunner>();
        services.AddSingleton<StepExecutor>();
        services.AddSingleton<IJobService, JobService>();
        return services;
    }
}
