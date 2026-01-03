# Job Creation & Docker Workflows

## Assign page walkthrough

1. Select or generate a job id, choose a pool (the dropdown refreshes from worker registrations), and optionally override the timeout.
2. Add steps by selecting their `StepType`. When picking **Docker**, the form expands to capture:
   - `Image`: pre-built container (e.g., `alpine:latest`).
   - `Dockerfile` & `Build context`: for inline builds.
   - `Build args`: `KEY=value` entries passed to `docker build`.
   - `Run args`: additional flags (`--network host`, `-p`, etc.).
3. Docker steps automatically set `JobPayload.Requirements.DockerRequired` so only docker-capable pools accept them.
4. Submit the job and track it via the Jobs or Console pages. Each step emits `[step:start]`/`[step:end]` markers so you can trace execution and logs.

## Running docker jobs locally

- The Aspire AppHost launches `worker-docker` with `src/JD.Worker.Cli/worker-docker.yaml`, sandbox mode `container`, and workspace isolation.
- Docker steps will:
  1. Build an image if a Dockerfile/context or build args are present. If you don't supply an image, a temporary tag (`jd-worker/jobid-attempt-step`) is generated and removed afterward.
  2. Mount the worker workspace at `/workspace` inside the container.
  3. Run the command via `/bin/sh -c` so multi-argument commands execute reliably even inside minimal images.

## Monitoring

- `Console` shows workers, statuses (online/offline), and lets you pick a job to stream logs. `New Logs` actions fetch the latest `take` number of lines and allow filtering by step.
- Job detail and history pages refresh logs via `Load history` and `Clear` buttons.
