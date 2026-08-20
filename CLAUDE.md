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
src/Carina.Domain          entities, value objects, repository interfaces — references Contracts only
src/Carina.Broadcast       broadcast-standard parsing — no dependencies
src/Carina.Infrastructure  persistence, IPC client, external boundaries
src/Carina.Db              migration entry point (leaf; nothing references it)
src/Carina.Api             HTTP surface; serves the OpenAPI document
tests/                     one test project per production project + architecture tests
```

The running application is the only source of the OpenAPI document: it is served at
`GET /openapi/v1.json` and nothing is checked in. It is mapped in Development only, so a
deployment does not publish its own description, and there it is anonymous, which is
where the web frontend fetches it from to generate its client. Three surfaces cannot be expressed in
the document — the transport stream, the event hub and the bulk programme guide — and
its description names all three so a consumer reading only the generated client learns
they exist.

Reference direction is one-way and enforced by `tests/Carina.Architecture.Tests`:

- `Carina.Driver` may reference `Carina.Contracts` and nothing else. Reaching into
  the app's layers would tie the two release streams back together.
- `Carina.Domain` may reference `Carina.Contracts` and nothing else — the driver client
  interface speaks the wire types, and mirroring them would duplicate every additive
  contract change. `Carina.Broadcast` has no project and no package references.
- `Carina.Contracts` itself has neither project nor package references. The domain's
  framework-freeness now runs through it, so a package added here would reach the domain
  transitively. What the contract carries is shared vocabulary — message records, enums,
  identifiers; the domain knows nothing of HTTP, URLs or JSON, and a source rule keeps
  `DriverEndpoints` and `DriverJson` out of it even though both compile against it.
- `Carina.Db` is a leaf: no project may reference the migration entry point.

The architecture tests read the project files rather than the compiled output, so a
reference that is declared but not yet used is still caught. `ReferenceRuleSelfCheckTests`
runs the same rules against a deliberately violating graph, so a green run means the
rules hold rather than that they inspected nothing.

### Conventions

- Controllers are one class per action: `{Verb}{Entity}Action.cs`, method `Invoke`.
- Use cases are `{Entity}Service`, returning `ServiceResult<T>` rendered by `BaseResponder`.
  The one exception is `/api/health`, which answers a probe with bare JSON and no envelope;
  do not copy that shape into a business endpoint.
- App-layer conventions are enforced by reflection in `tests/Carina.Conventions.Tests`,
  kept apart from `Carina.Architecture.Tests` so the latter can keep referencing no
  production assembly.
- Repository interfaces belong to `Carina.Domain`, implementations to `Carina.Infrastructure`.
- Value objects — identifiers included — derive from `CommonValueObject<T>`.
- Entities have a private constructor and a static `Rehydrate`.
- Time is taken from an injected `TimeProvider`, never from the ambient clock.
- Common build settings live in `Directory.Build.props`, package versions in
  `Directory.Packages.props`. Do not repeat them per project.
- Tests come first; no implementation without a test.

### Boundaries that must not be broken

- Reservations persist by their broadcast identifiers (nid + sid) and hold no foreign
  key to the channel definitions. Editing a channel definition must never delete a
  reservation.
- The programme cache is disposable: dropping it is recoverable by collecting again,
  so no table outside the cache may hold a foreign key into it.
- Both persistence boundaries are enforced against the EF Core model by
  `PersistenceBoundaryRuleTests` in `tests/Carina.Infrastructure.Tests`, self-checked
  against a deliberately violating model. The real columns of the three aggregates are
  out of the foundation's scope and belong to their own domains.
- Which family a table belongs to is read from the feature namespace of its entity type,
  never from the table's name: a reservation called `booking` is still a reservation.
  The map from feature namespace to family lives in `PersistenceBoundaryRules`, owned
  types are judged as their aggregate root, and an entity whose namespace is not in the
  map fails the rule instead of passing as unrelated — a domain adding tables declares
  which side of the boundary they are on rather than escaping it by naming.
- Contract changes are additive only. Removing or renaming an endpoint or an event
  breaks the "old driver + new app" combination, which is the normal state.
- App events are signals, not messages. A producer signals through `IAppEventPublisher`
  with a `Carina.Contracts.AppEventName` and nothing beside it — the nine names are the
  only instances there are and none is reachable from a string. `AppEventRuleTests` and
  `EventStreamRuleTests` hold both halves of that before the hub itself is built.
- Configuration is validated at startup and the process fails fast with a message
  naming the offending setting. There is no hot reload.
- Secrets never enter committed configuration — placeholders only, real values from
  the environment. Only the app process holds secrets; the driver holds none.

## Coding Conventions

- Declarations name their type. `var` is only for the case where the type already
  appears on the right — `new`, a cast, or a factory whose name carries the type
  (`ToList`, `Parse`, `CreateLinkedTokenSource`). Everything else is written out, so a
  reader learns the type without following the call. `.editorconfig` sets `IDE0008` to
  error and `EnforceCodeStyleInBuild` makes the build the thing that enforces it,
  not memory.
- Comments earn their place or are absent. Code that needs a comment to be understood
  is rewritten instead.
- Asynchronous methods end in `Async`.
- Domain holds entities, value objects, and repository interfaces, grouped by
  aggregate. Infrastructure holds the implementations. Entities are restored through a
  static `Rehydrate`.
- Warnings are errors. The build is the gate.

## CI Commands

All commands run inside the containers.

```bash
docker compose exec app dotnet build
docker compose exec app dotnet test
docker compose exec app dotnet format --verify-no-changes
```

`task` shortcuts: `task build`, `task test`, `task lint`, `task format`.

GitHub Actions runs build, format verification, the compose
render, the image build, and two test jobs: one
for everything except `Category=DbIntegration`, one for those against a PostgreSQL
service container. The second job counts the tests it ran, because `dotnet test` exits 0
when a filter matches nothing and a mistyped category would otherwise be green having
verified nothing.

## Docker Config

- `compose.yml` is the development environment: an `app` and a `driver` container on
  the .NET SDK image with the repository mounted at `/code`, plus `db` (PostgreSQL).
  No tuner device is mapped; development runs against the synthetic tuner backend.
- The two processes share `/run/carina`, where the driver socket lives.
- `driver` has a stop grace period longer than the driver's recording linger cap;
  shortening it would kill a recording that was about to finish. `docker compose stop`
  is an explicit stop, so the restart policy does not fight it.
- The driver's health probe is the driver itself — `Carina.Driver --probe` reads the
  configured socket, asks `/health` and `/tuners`, and answers on what it finds:
  draining, or every usable tuner faulted, is not healthy. The runtime image carries
  no HTTP client for this.
- `Carina.Driver --shutdown-budget` prints the seconds the runtime has to allow
  before SIGKILL — the linger cap plus the hard stop plus the host's own slack. The
  driver prints the same figure at startup. Whatever runs the image has to allow at
  least that long before killing the driver, or a recording that was about to finish
  is lost.
- The driver can be asked to restart over IPC (`POST /restart`, reached from the app as
  `POST /api/driver/restart`); it refuses with 409 while a recording is running. The
  driver only ever stops itself — coming back is the supervisor's half of the deal, and
  a stop it was asked for, by that call or by a signal, leaves exit code 0 while anything
  else leaves 70.
- Because an asked-for stop exits 0, `on-failure` is the one restart policy that must not
  be used for the driver: it would leave the process down exactly when it was asked to
  come back. `compose.yml` uses `unless-stopped`, which restarts on both exit codes and
  still leaves a driver the operator stopped by hand stopped across a daemon restart.
- In development the `driver` service runs the driver as its own main process
  (`docker/driver.dev.sh`), so it receives SIGTERM directly and `stop_grace_period` covers
  the recording linger. That script builds into `--artifacts-path /driver`, a container-local
  volume, so building from the `app` container never writes over the assembly the running
  driver has open. A tree that does not compile costs one build attempt per cooldown rather
  than a restart loop, and the container picks up the fix by itself.
- Code changes reach the driver with `task restart:driver` (`docker compose restart driver`);
  `task logs:driver` reads what it printed and `task probe:driver` runs the health probe
  against the built assembly without rebuilding it. With a recording held, that restart
  does not return until the recording ends — the same linger the grace period exists for.
  `POST /api/driver/restart` says so up front with a 409 instead of blocking.
- `migrate` takes a PostgreSQL advisory lock, so a second one waits instead of
  racing. Do not scale it: the lock serialises, but two migrations still make the
  slower deploy wait on a lock it cannot see.
- `Dockerfile` builds the single role-switched image (`driver`, `app`, `web`, `all`,
  plus `migrate`) via `docker/entrypoint.sh`. Routing between app and web is the job
  of a reverse proxy outside the image; the image contains no proxy.
- In that image the driver is published Native AOT; the app and the migration entry
  point are framework-dependent. The driver role runs as root, `app` and `migrate`
  drop to the unprivileged `carina` user, and `all` supervises both processes as a
  reaping PID 1.
- Releasing one role must not recreate the container running the other: replacing the
  app while the driver holds a recording ends that recording. Whatever builds and
  publishes the image owns that guarantee; this repository builds the image and does
  not publish it.

## UI Hostname

This repository serves the API only. In development the container listens on port
8080 and compose publishes it on host port 8081; `API_PORT` overrides the host
side. The web frontend lives in its own repository and fetches the OpenAPI
document this process serves.

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
