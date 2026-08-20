# Carina

Backend of a self-hosted recording system for Japanese digital broadcasting: a
privileged driver process that owns the tuner hardware and writes recordings, and
an unprivileged app process that serves the HTTP API around it.

Two properties decide most of what follows, and are the argument to answer before
relaxing any of it:

- **A recording in progress survives a deployment.** The driver is a separate
  process on its own release stream; replacing the app must not touch it.
- **Recording quality is observable.** Continuity errors are counted while the
  recording is being written, so a broken recording is found by searching for it
  rather than on playback.

`README.md` is for running this — setup, configuration, image roles, driver
operation. This file is for changing it, and does not repeat what is there.

## Tech Stack

- .NET 10, ASP.NET Core, xUnit.
- EF Core and PostgreSQL. Migrations are applied by the `Carina.Db --migrate`
  entry point, never by the app on startup.
- The driver reaches the tuner through the Linux DVB API with P/Invoke, and
  answers the app over HTTP/1.1 on a Unix domain socket. It binds no TCP port.
- Build settings are central in `Directory.Build.props`, package versions in
  `Directory.Packages.props`. Neither is repeated per project.

## Architecture

Two processes, one repository.

| Project | What it holds |
| --- | --- |
| `src/Carina.Driver` | privileged: tuning, transport stream handling, sessions, recording files |
| `src/Carina.Contracts` | the only artifact both processes share — the IPC contract |
| `src/Carina.Domain` | entities, value objects and repository interfaces, grouped by aggregate |
| `src/Carina.Broadcast` | broadcast-standard parsing, as a library that depends on nothing |
| `src/Carina.Infrastructure` | persistence, the driver IPC client, external boundaries |
| `src/Carina.Db` | the migration entry point |
| `src/Carina.Api` | the HTTP surface, and the OpenAPI document it serves |

Reference direction is one way, and `tests/Carina.Architecture.Tests` is what
holds it:

- `Carina.Driver` may reference `Carina.Contracts` and nothing else. Reaching
  into the app's layers would tie the two release streams back together.
- `Carina.Domain` may reference `Carina.Contracts` and nothing else, and carries
  no package reference at all — the driver client interface speaks the wire
  types, and mirroring them would duplicate every additive contract change.
- `Carina.Contracts` itself has neither project nor package references. The
  domain's framework-freeness runs through it, so a package added here reaches
  the domain transitively. What the contract carries is shared vocabulary:
  message records, enums, identifiers. The domain knows nothing of HTTP, URLs or
  JSON, and a source rule keeps `DriverEndpoints` and `DriverJson` out of it even
  though both compile against it.
- `Carina.Broadcast` has no project and no package references.
- `Carina.Infrastructure` and `Carina.Api` depend inwards only.
- `Carina.Db` is a leaf: no project may reference the migration entry point.
- The set of projects is itself asserted, so a new one is a deliberate act rather
  than something that appears in the graph unnoticed.

The rules read the project files rather than the compiled output, so a reference
that is declared but not yet used is still caught.

The running application is the only source of the OpenAPI document: it is served
at `GET /openapi/v1.json` and nothing is checked in. It is mapped in Development
only, so a deployment does not publish its own description, and there it is one
of the anonymous surfaces, which is where the web frontend fetches it from to
generate its client. Three surfaces cannot be expressed in the document — the
transport stream, the event hub and the bulk programme guide — and its
description names all three, so a consumer reading only the generated client
learns that they exist.

## Invariants

Most of these are held by a rule test rather than by memory, and every rule test
is paired with a self-check that runs the same rule against a deliberately
violating fixture — so a green run means the rule holds, not that it inspected
nothing.

- **Contract changes are additive only.** Removing or renaming an endpoint or an
  event breaks the "old driver, new app" combination, which is the normal state.
- **App events are signals, not messages.** A producer signals through
  `IAppEventPublisher` with a `Carina.Contracts.AppEventName` and nothing beside
  it; the nine names are the only instances there are, and none is reachable from
  a string. A subscriber reads a signal as "re-read", never as what changed.
- **Reservations hold no foreign key to the channel definitions.** They persist
  by their broadcast identifiers, so editing a channel definition can never
  delete a reservation.
- **The programme cache is disposable.** Dropping it is recoverable by collecting
  again, so no table outside the cache may hold a foreign key into it.
- **Which family a table belongs to is read from the feature namespace of its
  entity type, never from the table's name:** a reservation called `booking` is
  still a reservation. The map from feature namespace to family lives in
  `PersistenceBoundaryRules`, owned types are judged as their aggregate root, and
  an entity whose namespace is not in the map fails the rule instead of passing
  as unrelated. A domain that adds tables declares which side of the boundary
  they are on rather than escaping it by naming.
- **The driver asks nobody who they are.** The gate is the socket's permissions
  and owning group, and adding authentication would mean putting a secret in the
  privileged process. Only the app process holds secrets; the driver holds none,
  and the entrypoint strips database settings out of the driver role rather than
  trusting it to ignore them.
- **No endpoint exempts itself from the default denial,** and no production
  source reads an identity handed to it by an edge. Authentication is decided by
  the session the request carries, not by a header a proxy could be talked into
  setting.
- **The OIDC client secret is read in the clear in two files only** — where it is
  stored and where it is spent. Neither of them logs, and nothing that answers a
  caller can even name it.
- **Configuration is validated at startup** and the process stops with a message
  naming the offending setting. There is no hot reload. Secrets never enter
  committed configuration: placeholders only, real values from the environment.
- **A stop the driver was asked for exits 0; anything else exits 70.** Coming
  back is the supervisor's half of the deal, which is why `on-failure` is the one
  restart policy the driver must never be given.

## Conventions

- Controllers are one class per action, named `{Verb}{Entity}Action.cs` with a
  single public method `Invoke`, and they take their dependencies from the
  `Services` namespace and nowhere else.
- Use cases are `{Entity}Service`, and every public method returns a
  `ServiceResult<T>`. An action renders that as `BaseResponder<{X}Responder>`:
  the envelope carries the status and the message, and the `{X}Responder` record
  it wraps is built by a static `Of`. The one exception is `GET /api/health`,
  which answers a probe with bare JSON and no envelope; do not copy that shape
  into a business endpoint.
- Repository interfaces belong to `Carina.Domain`, implementations to
  `Carina.Infrastructure`.
- Value objects, identifiers included, derive from `CommonValueObject<T>` and are
  immutable — no property may have a setter.
- Entities have a private constructor and a static `Rehydrate`. A type that
  offers `Rehydrate` exposes no public constructor beside it.
- Time is taken from an injected `TimeProvider`, never from the ambient clock.
- Asynchronous methods end in `Async`.
- Declarations name their type. `var` is only for the case where the type already
  appears on the right — `new`, a cast, or a factory whose name carries the type
  (`ToList`, `Parse`, `CreateLinkedTokenSource`). Everything else is written out,
  so a reader learns the type without following the call. `.editorconfig` raises
  `IDE0008` to an error and `EnforceCodeStyleInBuild` makes the build enforce it,
  rather than memory.
- Comments earn their place or are absent. Code that needs a comment to be
  understood is rewritten instead.
- Warnings are errors. The build is the gate.
- Tests come first. There is no implementation without a test.

The conventions in the first five bullets are checked by reflection in
`tests/Carina.Conventions.Tests`, which is kept apart from
`Carina.Architecture.Tests` so that the latter can go on referencing no
production assembly.

## Tests

There is one test project per production project, plus `Carina.Architecture.Tests`
and `Carina.Conventions.Tests` for the rules above. `Carina.TestSupport` and
`Carina.BroadcastTestSupport` carry the shared fakes and fixtures; no test project
may reference another test project, and the shared support reaches no further than
the domain.

Three filters divide the suite, and CI runs one job per filter:

| Filter | What it selects |
| --- | --- |
| under a `Unit` folder, or nothing more specific | everything that needs neither a database nor the HTTP surface |
| `FullyQualifiedName~FeatureTest` | the tests that drive the application through its HTTP surface |
| `Category=DbIntegration` or a `DbIntegration` name | the tests that need a real PostgreSQL |

Each job counts the tests it ran and fails on zero, because `dotnet test` exits 0
when a filter matches nothing and a mistyped name would otherwise be green having
verified nothing.

## Commands

Everything runs inside the containers.

```bash
docker compose exec app dotnet build
docker compose exec app dotnet test
docker compose exec app dotnet format --verify-no-changes
```

`Taskfile.yml` is the place for a repeatable operation — `task build`, `task
test`, `task lint`, `task format`, `task migrate`, `task psql`, and the driver
tasks the README describes. Add a task rather than passing a longer command
around by hand.

GitHub Actions runs, on push and pull request to `master`: one job for the build
with warnings as errors and the format check, and the three test jobs above. A
second workflow builds the image and renders the compose file; it stays out of
the way of draft pull requests.

## Development environment

`compose.yml` brings up `app`, `driver` and `db` on the repository mounted at
`/code`, sharing `/run/carina` where the driver socket lives. No tuner device is
mapped: development runs against the synthetic tuner backend named in
`docker/driver.development.json`. Real hardware is attached by an untracked
compose override, which is also where the configuration for a real machine
belongs.

The `driver` service runs the driver as its own main process, so it receives
SIGTERM directly and `stop_grace_period` covers the recording linger. It builds
into a container-local artifacts path, so building from the `app` container never
writes over the assembly the running driver has open, and a tree that does not
compile costs one build attempt per cooldown rather than a restart loop — the
container picks the fix up by itself.

`--migrate` takes a PostgreSQL advisory lock, so a second one waits instead of
racing. Do not run it in parallel: the lock serialises, but two migrations still
make the slower deploy wait on a lock it cannot see.

## Domains

The system is the sum of the domains below. They land one at a time, each
finished through to merge before the next one starts, and each carries its own
share of the HTTP surface, its own tables and its own tests.

1. Foundation — the driver and app skeletons, IPC over the Unix socket, and an
   execution environment driven entirely by configuration
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
