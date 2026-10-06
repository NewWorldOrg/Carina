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
- The driver reaches the tuner through the Linux DVB API with P/Invoke and the
  card reader through `libpcsclite`, loaded at run time, and answers the app over
  HTTP/1.1 on a Unix domain socket. It binds no TCP port.
- Build settings are central in `Directory.Build.props`, package versions in
  `Directory.Packages.props`. Neither is repeated per project.

## Architecture

Two processes, one repository.

| Project | What it holds |
| --- | --- |
| `src/Carina.Driver` | privileged: tuning, descrambling, transport stream handling, sessions, recording files |
| `src/Carina.Contracts` | the only artifact both processes share — the IPC contract |
| `src/Carina.Domain` | entities, value objects and repository interfaces, grouped by aggregate |
| `src/Carina.Broadcast` | broadcast-standard parsing, as a library that depends on nothing |
| `src/Carina.Infrastructure` | persistence, the driver IPC client, external boundaries |
| `src/Carina.Db` | the migration entry point |
| `src/Carina.Api` | the HTTP surface, and the OpenAPI document it serves |

Reference direction is one way, and `tests/Carina.Architecture.Tests` holds it,
reading the project files rather than the compiled output so that a reference
declared but not yet used is still caught:

- `Carina.Driver` may reference `Carina.Contracts` and nothing else. Reaching
  into the app's layers would tie the two release streams back together.
- `Carina.Domain` may reference `Carina.Contracts` and nothing else, and carries
  no package reference at all — the driver client interface speaks the wire
  types, and mirroring them would duplicate every additive contract change.
- `Carina.Contracts` itself has neither project nor package references, because
  a package added there reaches the domain transitively. It carries shared
  vocabulary only: message records, enums, identifiers. A source rule keeps
  `DriverEndpoints` and `DriverJson` out of the domain even though both compile
  against it.
- `Carina.Broadcast` has no project and no package references.
- `Carina.Infrastructure` and `Carina.Api` depend inwards only.
- `Carina.Db` is a leaf: no project may reference the migration entry point.
- The set of projects is itself asserted, so a new one is a deliberate act.

The OpenAPI document exists only as the running application serves it, at
`GET /openapi/v1.json`; nothing is checked in. It is mapped in Development only,
and there it is one of the anonymous surfaces, which is where the web frontend
fetches it from. The transport stream, the event hub and the bulk programme guide
cannot be expressed in it, and its description names all three.

## Invariants

Most of these are held by a rule test, and every rule test is paired with a
self-check that runs the same rule against a deliberately violating fixture — so
a green run means the rule holds, not that it inspected nothing. A rule marked
**trip wire** reads source text or compiled call sites: it catches the ordinary
way of breaking the invariant, not every way, and its self-check states what
walks past it.

- **Contract changes are additive only.** Removing or renaming an endpoint or an
  event breaks the "old driver, new app" combination, which is the normal state.
- **App events are signals, not messages.** A producer signals through
  `IAppEventPublisher` with a `Carina.Contracts.AppEventName` and nothing beside
  it; the names declared there are the only instances, and none is reachable from
  a string. A subscriber reads a signal as "re-read", never as what changed.
- **Reservations hold no foreign key to the channel definitions.** They persist
  by their broadcast identifiers, so editing a channel definition can never
  delete a reservation.
- **What fits on the tuners is worked out in one place, and one place moves a
  reservation on the answer.** Creating a reservation, recalculating, and
  previewing an unsaved rule all join the same procedure before the same
  calculation runs, and a reservation moves between secured, contended and out of
  reach through a single method. A census of the compiled call sites holds it:
  the IL of every method is walked and the callers of the planner and of those
  moves are listed, so a second caller is caught wherever it sits and whatever it
  is named, even when handed around as a method group. **Trip wire:** a move made
  through reflection walks past. The walk refuses to answer if it did not consume
  a method body exactly.
- **A tuner ledger that cannot be read is unknown, not empty.** Scheduling reads
  the desired-state ledger once per run; when it cannot be read, or a service's
  selection cannot be answered for the same reason, the run writes nothing. The
  mark that says a reservation has nowhere to tune is written only when the
  answer really was "nowhere" — a driver that went away would otherwise put it on
  everything.
- **The programme cache is disposable.** Dropping it is recoverable by collecting
  again, so no table outside the cache may hold a foreign key into it.
- **Which family a table belongs to is read from the feature namespace of its
  entity type, never from the table's name.** The map lives in
  `PersistenceBoundaryRules`, owned types are judged as their aggregate root, and
  an entity whose namespace is not in the map fails the rule. A domain that adds
  tables declares which side of the boundary they are on.
- **The process that writes a recording file is the only one that removes it.**
  The app's mount of the output roots is read-only, and throwing a recording away
  is a call over the socket naming one recording and one output root; there is no
  call that throws away more than one. The driver derives the file name from the
  recording id rather than being handed a path, and refuses a name that is not
  one of its own, a root it does not declare, a root that holds no file at all
  (which is what a lost mount looks like) and a recording a session is still
  writing. The app removes only what it made from the recording — its picture and
  its captions — each in a directory of its own.

  A file that no recording owns goes the same way, and only as something the most
  recent ledger check found. The caller names the finding, never a path: the app
  resolves it to the root, path, size and last write the check kept, checks again
  that nothing claims the file and that it has not changed, and hands exactly
  those to the driver. The driver refuses a path that leaves the root or passes
  through a link, the file of a recording a session is writing, a root that holds
  no file at all, and a file whose size or last write is no longer the one it was
  handed, which it reads again just before it unlinks. A finding about a
  recording's own file is never a way to remove it.

  The check also walks the places the app writes into itself — the encode roots,
  the thumbnail directory and the captions directory — and the app removes a file
  there that nothing claims, with the same checks made again just before it
  unlinks. Such a place is not walked when it shares a name or a directory with a
  recording root, or holds a file under a recording's own file name, so a
  recording root seen from another path is never judged as one of them.
- **The version is declared once, in `Directory.Build.props`.** Every assembly
  carries it, the application answers with what it was built as, and a test holds
  the two together.
- **The driver asks nobody who they are.** The gate is the socket's permissions
  and owning group; authentication would mean putting a secret in the privileged
  process. The driver holds no secrets, and the entrypoint strips database
  settings out of the driver role rather than trusting it to ignore them.
- **No endpoint exempts itself from the default denial,** and no production
  source reads an identity handed to it by an edge. Authentication is decided by
  the session the request carries, not by a header a proxy could be talked into
  setting.
- **The OIDC client secret is read in the clear in two files only** — where it is
  stored and where it is spent. Neither of them logs, and nothing that answers a
  caller can name it.
- **No log names the path a request came on,** because a playback ticket can
  travel in the path of the two surfaces an external player opens. A line about a
  request names the route pattern it matched; the framework categories that write
  the path are pinned to warnings after every configured rule, a provider's own
  included; and a source rule refuses a file on the HTTP surface that both reads
  the request path and logs. A feature test turns every log up to its finest and
  finds the ticket in no line. **Trip wire:** a path handed to a helper that logs
  walks past the source rule.
- **Configuration is validated at startup** and the process stops with a message
  naming the offending setting. There is no hot reload. Secrets never enter
  committed configuration: placeholders only, real values from the environment.
- **The recording ledger's `CHECK` constraints call SQL functions labelled
  `IMMUTABLE`, on one condition:** every timestamp they read is required by
  regular expression to be an ISO-8601 instant ending in `Z` before it is cast,
  so `TimeZone` cannot change the answer. Relax that shape and the label becomes
  a lie the planner believes. The definitions live in one place and the migration
  carries a frozen copy; changing a definition means a new migration, and a test
  says so.
- **The recording and the count come from the same chunk.** The session's read
  loop hands each chunk to the writer and then to the counter, because a second
  pipeline over the same stream puts its own back pressure on the read the
  recording depends on. Counting is always on; no setting turns it off. One rule
  names the files allowed to mention the counter; another lists the files
  carrying two or more marks of a transport-stream parser (the three type names,
  the sync byte, the 188 and 184 strides, the pid and continuity masks). **Trip
  wire:** a loop that shows one mark or none — a stride written `4 + 180 + 4`,
  lower-case hex — walks past.
- **Streaming reads, counts nothing, and writes nothing that is not its own.** The
  live and playback paths give the bytes the driver already hands out to one
  transcoder; the app's drop figure is the fan-out's, never a continuity count.
  Source rules over the feature (its folders plus any file naming its namespaces,
  the composition root excepted) hold that no file shows even one parser mark; at
  most one file opens the driver's stream, once, asking for the viewer's seat, and
  none spells the path or another seat; and no file calls a repository write
  verb, reaches the store or the change tracker, writes SQL, changes a file on
  disk, or names the writers of the ledger, the tuners or the guide. Reading them
  is allowed. Under `/api/live` and `/api/videos` nothing deletes or declares
  itself destructive; the only state changes there are issuing a ticket and
  keeping a played position. **Trip wire:** a seat asked for as a literal, a write
  behind a verb the rule does not know or a delegate handed in from the
  composition root, and a count done inside ffmpeg walk past.
- **"Nothing counted this" and "this was counted and was clean" are different
  answers,** and so are "nowhere" and "somewhere, with nothing in it". A driver
  that cannot count says so in its greeting rather than answering zero, and a
  reader that does not find the capability reads no number at all. A position
  rides on the continuity count and the scrambling count together, because the
  seconds it names carry both.
- **A total is what the stream should have carried, not what arrived.** The
  driver counts packets read and packets never seen separately, and the ledger
  stores their sum — a recording that lost more than it received is ordinary in
  heavy rain.
- **Every count and the position beside it are read in one breath,** under a
  single lock, because a position read a moment later can place more losses than
  the count admits to. Both the entity and the table reject that pairing.
- **Where a loss happened is kept as a second of the stream's own clock.** The
  programme clock reference is what the file is played back against: a byte
  offset only approximates it at a variable bit rate, and the wall clock keeps
  running while a recording is interrupted. The 33-bit wrap is followed through;
  a jump the broadcast spliced in is written down as a re-anchor, so the timeline
  only reads forwards. A packet that sets the discontinuity indicator is taken at
  its word; the size test behind it admits a hundred times the longest gap the
  standard allows between two clock readings, and exists only for breaks nobody
  declared.
- **The driver announces progress every thirty seconds** while anything is being
  recorded; the app wakes on it and writes the counts into the ledger as the
  recording runs, so one that dies part way through can be told from a perfect
  one.
- **The clock the positions are measured against is the one the recorded service
  carries, and the driver cannot yet know which that is.** Measurement runs on
  the whole multiplex and follows the first programme clock it hears, handing
  over only when that one goes silent. A PID filter that narrows the file to one
  service would leave the followed clock possibly belonging to a service the file
  no longer contains, so deciding how the recorded service's clock reaches the
  session is a precondition for adding that filter.
- **A search is one vocabulary and one predicate.** The names a search is asked
  by are declared once, in `ProgrammeSearchQuery`, which reads a query string into
  a `ProgrammeSearch`; the HTTP action declares no argument of its own, and the
  OpenAPI document lists the same names from the same list. What a broadcast type
  means and which services the guide does not list are worked out below the
  application service.

  Searching happens in the store, on a stored generated column
  (`lower(pg_catalog.normalize(name || ' ' || summary, 'NFKC'))`); no request
  folds text in C#. `ProgrammeSearchText` is that column written out in C#, and
  the running application takes only the two constants the column is built from.
  `ProgrammeSearchMatching` answers a search in memory for whatever has to judge a
  programme without a query — the rule matcher, and the stand-in the feature
  tests use. Because that is a second implementation of the predicate, the two
  are held equal by database tests: every code point the store can hold goes
  through both sides of the folding, and the same programmes and searches go
  through both arms of the matching.

  Folding in C# needs the runtime's Unicode tables (with `InvariantGlobalization`
  on, `String.Normalize` silently returns its input). The driver folds nothing,
  so it and its tests keep the invariant tables and a rule names the pair. Both
  application entry points pin the default culture to the invariant one before
  anything else, and the reading and folding are measured under several languages
  to show no answer moves.
- **A visit of the guide is written in one statement, and its merge has two
  implementations.** `ProgrammeRepository.AbsorbAsync` hands the whole visit to
  the store as one `INSERT ... ON CONFLICT DO UPDATE ... WHERE (...) IS DISTINCT
  FROM (...)`, whose `CASE` expressions are `Programme.Absorb` written in SQL;
  `Programme.Absorb` is what the in-memory stand-in runs, and
  `ProgrammeAbsorbArmsTests` pushes the same visits through both and compares
  every column and whether the revision moved. Every visit first takes one
  transaction-scoped advisory lock: revisions are drawn from a sequence inside the
  writing transaction, and without the lock a visit that drew lower numbers could
  commit after one that drew higher ones, behind a reader following
  `revision > cursor`.
- **Which rule takes a programme is decided by weight, never by age or identifier
  alone.** Rules are read in falling priority, then oldest first, then by
  identifier, and the first to take a programme keeps it. A rule whose query
  cannot be read is turned off and reported, and the run carries on. A rule's
  sort, page and page size are read and then left out of the decision, because
  honouring the page size would drop programmes the rule was written to take.
- **The search across both layers keeps the "already held in the hot layer"
  exclusion above the union, never inside the archive arm.** A `NOT EXISTS` in the
  arm makes it a subquery the planner cannot merge into the append, and a search
  reaching into the archive then sorts the whole archive to hand back one page.
  Above the union the same exclusion is an anti-join the primary key answers.
- **The page a search hands back is bounded; the count beside it is not.** The
  ordered path stops at fifty rows, but `Total` still counts every archived row
  that matches, so it grows with the archive. Nothing caps it yet; an estimated
  total, cursor paging, or a count that stops at a ceiling would.
- **A recording that has ended is frozen except for its picture, its captions,
  what throwing it away left behind and when it was descrambled — as long as it
  is reached through the aggregate's own methods.** Every public method on
  `Recording` but four refuses once an outcome is set: `Illustrate` moves the two
  thumbnail columns; `Caption` moves the four caption columns, and refuses
  anything but waiting while the recording is still being written; `Erased`
  moves the two columns that say a deletion left files on disk (a deletion that
  took everything removes the row); `Descrambled` moves `descrambled_at`, only on
  a recording that ended with `ScramblingUnresolved` and has not been descrambled
  yet, leaving the outcome as written. Reflection tests assert the whole set of
  methods, that no property has a public setter, and that the only static entry
  points are the two that make a recording. The change tracker, raw SQL and
  reflection reach past the aggregate, and only the trip wires below look for
  those.

  The thumbnail pass reads the ledger for ended rows with no picture rather than
  being called from the path that ends a recording. A failed recording is skipped,
  because a picture would say it was recorded; one cut short is illustrated and
  the ledger still says so. A picture that cannot be drawn keeps its failure class
  beside the state, never in `outcome_detail`. Two source rules sit on top:
  one reads every file whose path carries the word thumbnail and reports a call
  that says how a recording ended, or a reach past the aggregate through
  reflection, raw SQL or the change tracker's `Entry`/`Property`/`CurrentValue`;
  the other reports a file outside the feature folder that names a type the
  feature is made of, except the files `ThumbnailRules` lists, and that list is
  asserted whole. **Trip wire:** a helper whose name says nothing about thumbnails
  walks past both.
- **What a caller has to send is in the document.** A handler that reads a query
  input the framework never saw — indexed off `Request.Query` rather than
  declared as an argument — declares it beside its mapping, so the generated
  client can send it. A feature test reads the HTTP surface for every query name a
  handler asks for and looks for each in the served document under that file's
  path; a name read on a path the document disowns is listed rather than passed
  over. **Trip wire:** it sees the indexer and `TryGetValue`, reports a name it
  cannot follow, and misses a handler that never names what it reads, such as the
  programme search, whose vocabulary is held by a rule of its own. Headers,
  cookies and bodies are outside it.
- **The card is opened through one render node, the one `Machine:RenderNode`
  names.** Encoding, live viewing, playback transcoded on the fly and the probe
  of what the card can do all take it from `MachineSettings`, so a machine with
  two cards cannot find one usable and then open the other. The default node is
  named once, as the default of that setting: `RenderNodeConventionRuleTests`
  refuses the constant anywhere else, a render node written as a literal, and a
  flag that opens the card in any file but the three argument builders handed the
  settings. **Trip wire:** a node assembled from pieces walks past.
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
- Declarations name their type. `var` is only for a right-hand side that is a
  `new`, a cast, or a projection into an anonymous type; a factory whose name
  carries the type is not enough. `.editorconfig` raises `IDE0008` to an error,
  `EnforceCodeStyleInBuild` makes the build enforce it, and
  `VarConventionRuleTests` reads the source for the rest.
- Nesting stays shallow: two deep, three only when nothing shallower reads as
  well, for conditional expressions and for blocks alike. A choice on two axes
  becomes a named mapping, a staged condition a named method that returns early
  or a `switch` expression; a block that would be the fourth becomes an early
  return, a named method or a `using` declaration. `NestingConventionRuleTests`
  reads the syntax tree and refuses an expression that joins more than two
  conditionals and a function whose blocks stand more than three deep; what opens
  a block and where the count restarts is defined in `NestingConventionRules`.
- Comments earn their place or are absent. Code that needs a comment to be
  understood is rewritten instead.
- Warnings are errors. The build is the gate.
- Tests come first. There is no implementation without a test.

The first five bullets are checked by reflection in `tests/Carina.Conventions.Tests`,
which is kept apart from `Carina.Architecture.Tests` so that the latter can go on
referencing no production assembly.

## Tests

There is one test project per production project, plus `Carina.Architecture.Tests`
and `Carina.Conventions.Tests` for the rules above. `Carina.TestSupport` and
`Carina.BroadcastTestSupport` carry the shared fakes and fixtures; no test project
may reference another test project, and the shared support reaches no further than
the domain.

Five filters divide the suite, and CI runs one job per filter:

| Filter | What it selects |
| --- | --- |
| under a `Unit` folder, or nothing more specific | everything that needs neither a database, the HTTP surface nor ffmpeg |
| `FullyQualifiedName~FeatureTest` | the tests that drive the application through its HTTP surface, split by class into shards |
| `Category=DbIntegration` or a `DbIntegration` name | the tests that need a real PostgreSQL |
| `Category=Material` | the tests that write a synthetic broadcast with ffmpeg and read it back |
| `Category=Scale` | the measurements at archive and schedule scale |

Each job counts the tests it ran and fails on zero, because `dotnet test` exits 0
when a filter matches nothing. A separate job checks that every test is selected
by one of the filters, and that every feature test falls in exactly one shard.

The material job runs inside the `develop` image, because the ffmpeg the
application runs is built from source there with libaribcaption.
`SyntheticBroadcast` in `Carina.BroadcastTestSupport` writes a broadcast from
ffmpeg's own generators plus caption, superimposition and dual-mono streams
written byte by byte; nothing generated is checked in, and the same arguments
give the same bytes.

The scale job runs alone on a PostgreSQL no other test is using; `task
test:scale` runs the same filter locally, which is worth doing when the search or
the shape of either programme table changes. Wall clock on the same data moves
several times over with how much of it the page cache holds, so where a budget in
milliseconds cannot hold on its own the tests assert the plan shape and the
blocks read. The year-back search without a keyword is the shape that guards the
search; the test says why the keyword shape beside it cannot stand in for it.

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

GitHub Actions runs, on push and pull request to `master`, the build with warnings
as errors, the format check, the test jobs above and the image-tag check. A second
workflow builds the image and renders the compose file; it stays out of the way of
draft pull requests, and on `master` publishes the image under the tags the README
describes.

`.github/image-tags.sh` derives the driver and app sides of the image from the
stages of the `Dockerfile` and the project references they publish rather than
from a list, and refuses to answer when something that goes into the image belongs
to neither side. Its `prove` runs on every push: a change to one side alone must
move that side's tag and leave the other where it was. A tag already in the
registry is never pushed again, because a driver left running on it would have its
image changed underneath it.

## Development environment

`compose.yml` brings up `app`, `driver` and `db` on the repository mounted at
`/code`, sharing `/run/carina` where the driver socket lives. No tuner device is
mapped: development runs against the synthetic tuner backend named in
`docker/driver.development.json`. Real hardware is attached by an untracked
compose override, which is also where the configuration for a real machine
belongs.

The render node is the exception, because the transcoder is meant to find it
without being configured. `task up` reads the host through `docker/dri-env.sh` and
hands `/dev/dri` to `app` when it is there, with the owning groups of `card0` and
of the render node as numbers measured on that host — the render group is numbered
differently from one distribution to the next and is often absent from the
container's `/etc/group`. A host without the node gets `/dev/null` in the device
entry, because a `devices:` entry naming a missing path stops the container from
being created.

The image demotes `app` to an unprivileged user before starting it. The `app` role
reads the owning group off the `card` and `render` nodes actually present in the
container and hands the demoted process those beside its own group, so the number
comes from the device rather than from a name; group 0 is never handed over.

The `driver` service runs the driver as its own main process, so it receives
SIGTERM directly and `stop_grace_period` covers the recording linger. It builds
into a container-local artifacts path, so building from the `app` container never
writes over the assembly the running driver has open, and a tree that does not
compile costs one build attempt per cooldown rather than a restart loop.

`--migrate` takes a PostgreSQL advisory lock, so a second one waits instead of
racing. Do not run it in parallel: two migrations still make the slower deploy
wait on a lock it cannot see.

## Domains

The system is the sum of the domains below. Each carries its own share of the
HTTP surface, its own tables and its own tests.

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
