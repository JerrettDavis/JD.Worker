var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.JD_Worker_Orchestrator_ApiService>("apiservice")
    .WithHttpHealthCheck("/health");

var hostWorkerA = builder.AddProject<Projects.JD_Worker_Cli>("worker-host-a")
    .WithArgs("run", "--config", "worker-host-a.yaml")
    .WithEnvironment("JD_WORKER_API_URL", apiService.GetEndpoint("http"))
    .WithReference(apiService)
    .WaitFor(apiService);

var hostWorkerB = builder.AddProject<Projects.JD_Worker_Cli>("worker-host-b")
    .WithArgs("run", "--config", "worker-host-b.yaml")
    .WithEnvironment("JD_WORKER_API_URL", apiService.GetEndpoint("http"))
    .WithReference(apiService)
    .WaitFor(apiService);

var dockerWorker = builder.AddProject<Projects.JD_Worker_Cli>("worker-docker")
    .WithArgs("run", "--config", "worker-docker.yaml")
    .WithEnvironment("JD_WORKER_API_URL", apiService.GetEndpoint("http"))
    .WithReference(apiService)
    .WaitFor(apiService);

builder.AddProject<Projects.JD_Worker_Orchestrator_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();
