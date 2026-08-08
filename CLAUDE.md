# Carina

Backend of a self-hosted recording system for Japanese digital broadcasting: a
privileged driver process that owns the tuner hardware and writes recordings, and
an unprivileged app process that provides the HTTP API around it.

Two properties drive the design:

- **A recording in progress survives a deployment.** The driver is a separate
  process on its own release tag; replacing the app must not touch it.
- **Recording quality is observable.** Continuity errors are measured while
  recording, so a broken recording can be found afterwards instead of being
  discovered on playback.

## Tech Stack

- .NET 10, ASP.NET Core.
- EF Core + PostgreSQL. Migrations live in a dedicated entry point.
- Driver: Linux DVB API through P/Invoke, in-process descrambling, smart card daemon.
- IPC: HTTP/1.1 over a Unix domain socket. The driver never binds a TCP port.

## Architecture

Two processes, one repository.

```
src/Carina.Driver          privileged; tuning, descrambling, TS handling, recording files
src/Carina.Contracts       the only artifact both processes share (IPC contract)
src/Carina.Domain          entities, value objects, repository interfaces — no dependencies
src/Carina.Broadcast       broadcast-standard parsing — no dependencies
src/Carina.Infrastructure  persistence, IPC client, external boundaries
src/Carina.Db              migration entry point (leaf; nothing references it)
src/Carina.Api             HTTP surface; publishes the OpenAPI document
tests/                     one test project per production project + architecture tests
```

Reference direction is one-way and enforced by `tests/Carina.Architecture.Tests`:

- `Carina.Driver` may reference `Carina.Contracts` and nothing else. Reaching into
  the app's layers would tie the two release streams back together.
- `Carina.Domain` and `Carina.Broadcast` have no project and no package references.
- `Carina.Db` is a leaf: no project may reference the migration entry point.

The architecture tests read the project files rather than the compiled output, so a
reference that is declared but not yet used is still caught. `ReferenceRuleSelfCheckTests`
runs the same rules against a deliberately violating graph, so a green run means the
rules hold rather than that they inspected nothing.

### Conventions

- Controllers are one class per action: `{Verb}{Entity}Action.cs`, method `Invoke`.
- Use cases are `{Entity}Service`, returning `ServiceResult<T>` rendered by `BaseResponder`.
- Repository interfaces belong to `Carina.Domain`, implementations to `Carina.Infrastructure`.
- Value objects — identifiers included — derive from `CommonValueObject<T>`.
- Entities have a private constructor and a static `Rehydrate`.
- Time is taken from an injected `TimeProvider`, never from the ambient clock.
- Common build settings live in `Directory.Build.props`, package versions in
  `Directory.Packages.props`. Do not repeat them per project.
- Tests come first; no implementation without a test.

### Boundaries that must not be broken

- Reservations persist by their broadcast identifiers and hold no foreign key to the
  channel definitions. Editing a channel definition must never delete a reservation.
- The programme cache is disposable: dropping it is recoverable by collecting again.
- Contract changes are additive only. Removing or renaming an endpoint or an event
  breaks the "old driver + new app" combination, which is the normal state.
- Configuration is validated at startup and the process fails fast with a message
  naming the offending setting. There is no hot reload.
- Secrets never enter committed configuration — placeholders only, real values from
  the environment. Only the app process holds secrets; the driver holds none.

## CI Commands

All commands run inside the containers.

```bash
docker compose exec app dotnet build
docker compose exec app dotnet test
docker compose exec app dotnet format --verify-no-changes
```

`task` shortcuts: `task build`, `task test`, `task lint`, `task format`.

GitHub Actions runs build, test and format verification on push and pull request to
`master`.

## Docker Config

- `compose.yml` is the development environment: an `app` and a `driver` container on
  the .NET SDK image with the repository mounted at `/code`, plus `db` (PostgreSQL).
  No tuner device is mapped; development runs against the synthetic tuner backend.
- The two processes share `/run/carina`, where the driver socket lives.
- `driver` has a stop grace period longer than the driver's recording linger cap;
  shortening it would kill a recording that was about to finish.
- `Dockerfile` builds the single role-switched image (`driver`, `app`, `web`, `all`,
  plus `migrate`) via `docker/entrypoint.sh`. Routing between app and web is the job
  of a reverse proxy outside the image; the image contains no proxy.

## UI Hostname

This repository serves the API only, published at http://localhost:8080 in
development. The web frontend lives in its own repository and consumes the OpenAPI
document generated here.

## Implementation Phases

The skeleton is in place: solution layout, build settings, container and CI. Feature
work proceeds one domain at a time, each finished through to merge before the next
one starts.

1. Foundation — driver/app skeleton, IPC over the Unix socket, configuration-driven
   environment independence
2. Tuners and channel selection
3. Programme guide
4. Authentication
5. Reservations and rules
6. Recording
7. Quality observability
8. Streaming and playback
9. Library
10. Encoding
11. Migration from an existing recording setup
