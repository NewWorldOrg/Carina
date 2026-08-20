# Carina

Backend for a self-hosted TV recording system for Japanese digital broadcasting.

Carina runs as two processes: a privileged `driver` that owns the tuners and writes
recording files, and an unprivileged `app` that serves the HTTP API. Splitting them
means replacing the API never interrupts a recording in progress.

The web frontend lives in a separate repository and generates its client from
`GET /openapi/v1.json`, which only the running app serves. The document is not
committed.

## Requirements

- Docker

No tuner card and no B-CAS card are needed for development. A synthetic tuner
produces a fixed transport stream.

## Getting started

```bash
task up      # driver, app, PostgreSQL
task build
task test
task lint    # dotnet format --verify-no-changes
```

Without Task:

```bash
docker compose up -d
docker compose exec app dotnet build
docker compose exec app dotnet test
```

The API listens on port 8080 in the container and is published on host port 8081
(`API_PORT`).

## Configuration

Nothing environment-specific is compiled in. Devices, output paths, the socket path,
the database connection and ports are all read from configuration, and an invalid
value stops startup with the offending setting named. Committed configuration files
contain placeholders only.

| Variable | Description |
| --- | --- |
| `CARINA_DRIVER_CONFIG` | Path to the driver's configuration file |
| `ConnectionStrings__Carina` | PostgreSQL connection string for the API |
| `CARINA_DB_CONNECTION` | Connection string used when applying migrations |
| `CARINA_ROLE` | Which role the image starts |
| `CARINA_KNOWN_PROXIES` | Addresses whose `X-Forwarded-*` headers are trusted |
| `CARINA_KNOWN_NETWORKS` | The same as networks, in address/prefix form |

## Image roles

`Dockerfile` produces a single image; `docker/entrypoint.sh` selects the role.

| Role | Starts |
| --- | --- |
| `driver` | The privileged process |
| `app` | The HTTP process |
| `migrate` | Applies migrations and exits |
| `web` | The frontend, injected by the distribution image build |
| `all` | Both processes in one container, for development |

Routing `/api/*` to `app` and everything else to `web` is done outside the image.
This is a contract, not a preference: on separate origins the browser drops the
session cookie, state-changing requests fail the `Origin` check, and iPadOS blocks
third-party cookies. See `deploy/README.md` for the contract and a Kubernetes
reference.

## Working with the driver

```bash
task probe:driver     # health check
task logs:driver
task restart:driver   # pick up code changes
```

Restarting while a recording is held does not return until that recording ends.
`POST /api/driver/restart` answers 409 instead of blocking.

Two things the runtime must respect:

- `stop_grace_period` must exceed the budget the driver reports through
  `Carina.Driver --shutdown-budget`. A shorter one SIGKILLs the driver mid-cleanup
- Do not use the `on-failure` restart policy. An asked-for stop exits 0, so the
  driver would stay down exactly when it was asked to come back

## Tests

`dotnet test` runs unit tests, API feature tests and architecture tests.

The architecture tests read the project files rather than compiled output, so a
reference that is declared but not yet used is still caught: the driver reaching
past the shared contract, the domain or the broadcast parser taking a dependency,
or anything referencing the migration project.
