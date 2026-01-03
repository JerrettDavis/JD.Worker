# JD.Worker Orchestrator

JD.Worker combines a .NET 10 orchestrator, dashboard, and configurable worker fleet powered by Aspire to coordinate and execute job flows with pluggable connectors and step runners.

## Highlights

- **Aspire orchestrator**: hosts the API, Web UI, and multiple worker instances (host + Docker agents) via the Aspire AppHost project so running and staging environments stay in sync.
- **Polled/pushed connectors**: Local and HTTP connectors can enqueue jobs, publish logs, and drive heartbeats through the orchestrator API.
- **Rich UI experience**: Blazor dashboard offers configurable themes, console views, job history with step-by-step logs, and job assignment tools.
- **Configurable workers**: Workers read YAML configs from `src/JD.Worker.Cli` and support Docker builds, process/shell execution, environment handling, and workspace cleanup.
- **Expandable step ecosystem**: Docker, shell, and process runners are registered via dependency injection; logs emit step markers to power the UI timelines.

## Getting started

1. Restore and build the solution:
   ```bash
   dotnet restore
   dotnet build
   ```

2. Run `JD.Worker.Orchestrator.AppHost` to spin up the API, workers, and web frontend. The default Aspire layout starts three worker configs (host/docker) defined in `src/JD.Worker.Cli`.

3. Visit the Blazor UI (http://localhost:xxxx) to monitor workers, view console logs, review job history, and dispatch jobs through the “New Job” flow.

4. Configure jobs via the Assign page: add steps, mark Docker requirements, and provide Dockerfile/build/run arguments. Docker pools are auto-selected for Docker steps.

## Architecture overview

- `JD.Worker.Configuration`: defines the JSON/YAML models for jobs, steps, workers, and policies.
- `JD.Worker.Core`: contains job state machines, schedulers, the Docker/Shell/Process runners, workspace managers, and registries.
- `JD.Worker.Orchestrator.ApiService`: lightweight ASP.NET Core Web API that exposes job/workers/log endpoints.
- `JD.Worker.Orchestrator.Web`: Blazor UI with theming (`theme.js`, CSS variables), job consoles, assignment helpers, and history pages.
- `JD.Worker.Orchestrator.AppHost`: Aspire project wiring API + UI + workers for local demos with health checks.

## Docs and learning path

 - [Architecture notes](docs/architecture.md)
 - [Job creation & Docker workflows](docs/usage.md)
 - [Troubleshooting tips](docs/troubleshooting.md)

## Contributing

- Follow the existing C# stylistic choices (PascalCase, expression-bodied members, scoped CSS files for components).
- Update or add tests under `tests/*` when touching core logic (e.g., job parsing, step runners).
- Keep Aspire-bound configs in `src/JD.Worker.Cli` so the AppHost can keep picking them up automatically.

## Related samples

 - Aspire demo integration: `JD.Worker.Orchestrator.AppHost` orchestrates projects and ensures workers register with the API and dashboard.
 - Worker configs: `worker-host-a.yaml`, `worker-host-b.yaml`, `worker-docker.yaml`.
