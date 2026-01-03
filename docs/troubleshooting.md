# Troubleshooting & Tips

## Worker heartbeats

- Workers send heartbeats to `/v1/workers/heartbeat`. The dashboard marks them `Unknown` if their last heartbeat is older than 20 seconds. Confirm the worker process is running and that `JD_WORKER_API_URL` points to the API.

## Docker step failures

- If you see `exec: "<command>": executable file not found`, confirm the step runs via `/bin/sh -c` by providing `Command` and `Arguments` through the Assign UI, or set the `Image` to one that already contains the binary.
- Builds require a valid context path and Dockerfile. Relative paths are resolved from the worker’s `workspace.root`.

## Logs missing or empty

- Step logs rely on `JobLogEntry` records written with step markers from `JobExecutor.PublishStepMarkerAsync`. If logs are missing, make sure the step runner still writes to `IWorkspaceContext.LogSink`.
- `Console` auto-selects the first running job/worker, but you can manually choose another job to inspect historical logs from `/v1/jobs/{jobId}/logs`.

## UI theming

- Theme selection persists per session using `localStorage` via `wwwroot/theme.js`. If colors seem off, open the browser dev tools and ensure CSS variables such as `--bg`, `--panel-border`, and `--text-muted` are defined under `:root`.
