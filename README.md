# Carina

Backend of a self-hosted recording system for Japanese digital broadcasting.

It runs as two processes built from this one repository:

- **driver** — privileged. Owns the tuner devices, descrambles, handles the transport
  stream and writes recording files. It talks to nothing but a Unix domain socket and
  holds no secrets.
- **app** — unprivileged. Interprets the stream, keeps the programme guide,
  reservations, rules and recordings, and serves the HTTP API.

They are released on independent tags so that replacing the app leaves a recording in
progress running.

## Layout

```
src/Carina.Driver               privileged process
src/Carina.Contracts            IPC contract shared by both processes
src/Carina.Domain               entities, value objects, repository interfaces
src/Carina.Broadcast            broadcast-standard parsing (dependency free)
src/Carina.Infrastructure       persistence, IPC client, external boundaries
src/Carina.Db                   migration entry point
src/Carina.Api                  HTTP surface and OpenAPI document
tests/                          one test project per production project
tests/Carina.Architecture.Tests reference rules, checked against the project files
```

## Getting started

Requires Docker and, optionally, [Task](https://taskfile.dev).

```bash
task up                 # docker compose up -d  (app, driver, PostgreSQL)
task build              # dotnet build
task test               # dotnet test
task lint               # dotnet format --verify-no-changes
task run:app            # serves http://localhost:8080
```

Without Task:

```bash
docker compose up -d
docker compose exec app dotnet build
docker compose exec app dotnet test
```

The development environment needs no tuner hardware: the driver runs a synthetic
tuner backend that produces a deterministic transport stream.

## Configuration

Nothing environment-specific is compiled in. Device inventory, recording output
directory, socket path, database connection and ports all come from configuration,
and an invalid setting fails the process at startup with a message naming it.

Committed configuration files contain placeholders only. Real values come from the
environment:

| Variable | Meaning |
| --- | --- |
| `ConnectionStrings__Carina` | PostgreSQL connection string used by the API |
| `CARINA_DB_CONNECTION` | PostgreSQL connection string used by the migration entry point |
| `CARINA_DRIVER_SOCKET` | Path of the driver's Unix domain socket |
| `CARINA_ROLE` | Role the container image starts (`driver`, `app`, `web`, `all`, `migrate`) |

## Image roles

`Dockerfile` produces a single image; `docker/entrypoint.sh` selects the role:

| Role | Starts |
| --- | --- |
| `driver` | the privileged process |
| `app` | the HTTP process |
| `migrate` | applies database migrations and exits |
| `web` | placeholder; the web asset is supplied by the distribution image build |
| `all` | driver and app in one container, for development only |

Routing (`/api/*` to the app, everything else to the web asset) is the job of a
reverse proxy outside the image. The image contains no proxy.

## Tests

`dotnet test` runs the unit tests, the API feature tests and the architecture tests.
The architecture tests enforce the reference rules the two-process split depends on —
notably that the driver reaches nothing but the shared contract, and that the domain
and the parsing library stay dependency free.
