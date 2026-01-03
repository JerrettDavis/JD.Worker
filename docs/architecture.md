# Architecture Notes

## Core domains

- **Job Envelope/Step Models** (`JD.Worker.Configuration`): Flattened into contracts with `IStepDefinition`, `JobRequirements`, and optional Docker build/run metadata.
- **Worker Core** (`JD.Worker.Core`): Handles job state transitions, workspace management, registries, Docker/shell/process execution, and log publishing (with step markers such as `[step:start]`/`[step:end]` being emitted from `JobExecutor`).
- **Orchestrator services**: API service hosts the job/workers/log endpoints plus lease/ack/nack semantics, while the web project consumes them and surfaces dashboards, payload builders, and log consoles.
- **Aspire AppHost**: Spins up API, UI, and multiple `JD.Worker.Cli` agents with configurations for host and Docker workloads; health checks and environment wiring keep the system discoverable.

## Execution flow

1. Jobs arrive via connectors (polling/pushed). `ConnectorListenerService` validates requirements, especially the new `DockerRequired` flag, against worker capabilities (sandbox mode, labels).
2. Accepted jobs are stored by `JobService` and scheduled. `JobExecutor` orchestrates steps while emitting log markers consumed by the UI’s timeline view.
3. Step runners (`ShellStepRunner`, `ProcessStepRunner`, `DockerStepRunner`) interpret signaling and run commands inside workspaces or Docker containers. Docker runners optionally build images using provided contexts before executing the container command.
4. Logs are stored in `ILogStore`, leading to console views acquiring them through `/v1/jobs/{id}/logs` with filtering by step timeline markers.

## UI/UX integration

- Job detail pages parse log markers to build timeline cards and filter log panels for “All logs” or specific steps.
- Theme switching is managed via `wwwroot/theme.js` and CSS variables (`--topbar-background`, `--bg`, `--panel-border`) so the site can adopt light/dark/system modes.
