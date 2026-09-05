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
- **What fits on the tuners is worked out in one place, and one place moves a
  reservation on the answer.** Creating a reservation, recalculating, and
  previewing an unsaved rule all join the same procedure before the same
  calculation runs, and a reservation's moves between secured, contended and out
  of reach are made from a single method. What holds that is a census of the
  compiled call sites rather than a rule over source text: the IL of every method
  in the application is walked and the callers of the planner and of those moves
  are listed, so a second caller is caught wherever it sits, whatever it is
  named, and even when the move is handed around as a method group rather than
  called. **It is a trip wire, not a proof** — a move made through reflection
  walks straight past it, and a test says so plainly. The walk refuses to answer
  if it did not consume a method body exactly, so a misread operand width cannot
  quietly turn into a shorter list.
- **A tuner ledger that cannot be read is unknown, not empty.** Scheduling reads
  the desired-state ledger once per run, and when it cannot be read it writes
  nothing at all rather than deciding that nothing fits; a service whose
  selection cannot be answered for the same reason stops the run too. The mark
  that says a reservation has nowhere to tune is only ever written when the
  answer really was "nowhere", because a driver that went away would otherwise
  put that mark on everything.
- **The programme cache is disposable.** Dropping it is recoverable by collecting
  again, so no table outside the cache may hold a foreign key into it.
- **Which family a table belongs to is read from the feature namespace of its
  entity type, never from the table's name:** a reservation called `booking` is
  still a reservation. The map from feature namespace to family lives in
  `PersistenceBoundaryRules`, owned types are judged as their aggregate root, and
  an entity whose namespace is not in the map fails the rule instead of passing
  as unrelated. A domain that adds tables declares which side of the boundary
  they are on rather than escaping it by naming.
- **The process that writes a recording file is the only one that removes it.**
  The app's mount of the output roots is read-only, and throwing a recording away
  is a call over the socket naming one recording and one output root. The driver
  derives the file name from the recording id rather than being handed a path, so
  a caller cannot name a file of its own choosing, and it refuses a name that is
  not one of its own, a root it does not declare, a root that holds no file at all
  (which is what a lost mount looks like) and a recording a session is still
  writing. The app removes only the picture drawn of the recording, which lives on
  a directory of its own. One call throws away one recording; there is no call that
  throws away more than one.

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
- **The recording ledger's `CHECK` constraints call SQL functions, and those
  functions are labelled `IMMUTABLE` on one condition:** every timestamp they
  read is required by regular expression to be an ISO-8601 instant ending in
  `Z` before it is cast, so `TimeZone` cannot change the answer. Relax that
  shape and the label becomes a lie the planner believes. The definitions live
  in one place and the migration carries a frozen copy of it; changing the
  definition means writing a new migration, and a test says so.

- **The recording and the count are meant to come from the same chunk.** The
  session's read loop hands each chunk to the writer and then to the counter,
  because a second pipeline over the same stream puts its own back pressure on
  the read the recording depends on. Counting is always on — there is no setting
  that turns it off.

  **What holds that is a trip wire, not a proof, and it is worth knowing where
  it stops.** One rule names the files allowed to mention the counter; another
  lists the files carrying two or more of the marks a transport-stream parser
  leaves behind — the three type names, the sync byte, the 188 and 184 strides,
  the pid and continuity masks. Both read source text. **A second loop that
  shows one mark or none walks straight past**: written with `4 + 180 + 4` for
  the stride, or in lower-case hex, it is invisible to them, and tests assert
  that plainly so nobody reads the rules as a guarantee. What the rules do catch
  is the ordinary way somebody would write one.

- **Streaming reads, counts nothing, and writes nothing that is not its own.** The
  live and playback paths take the bytes the driver already hands out and give them
  to one transcoder. A second pipeline over the same stream — a packet loop in the
  app, or a second seat on the driver's session stream — is the back pressure the
  rule above exists to keep off the recording, and the app's drop figure is the
  fan-out's, never a continuity count. What holds that is a set of source rules
  over the feature, which is its folders plus any file naming its namespaces, the
  composition root excepted: no file shows even one mark of a transport-stream
  parser where the global rule asks for two; at most one file opens the driver's
  stream, once, asking for the viewer's seat, and none spells the path or another
  seat; and no file calls a repository write verb, reaches the store or the change
  tracker, writes SQL, changes a file on disk, or names the writers of the ledger,
  the tuners or the guide. Reading any of them is allowed and a test says so. Over
  the HTTP surface, nothing under `/api/live` or `/api/videos` deletes or declares
  itself destructive, and the only thing there that changes state issues a ticket.
  **Trip wires, not proof:** a stride written `4 + 180 + 4`, a seat asked for as a
  literal, a write behind a verb the rule does not know or a delegate handed in
  from the composition root, and a count done inside ffmpeg all walk past, and the
  self-checks say so plainly.

- **"Nothing counted this" and "this was counted and was clean" are different
  answers,** and so are "nowhere" and "somewhere, with nothing in it". A driver
  that cannot count says so in its greeting rather than answering zero, and a
  reader that does not find the capability reads no number at all. A position
  needs both counts behind it: it rides on the continuity count and the
  scrambling count together, because the seconds it names carry both.

- **A total is what the stream should have carried, not what arrived.** The
  driver counts the packets it read and the packets it never saw separately, and
  the total the ledger stores is the two added together — a recording that lost
  more than it received is ordinary in heavy rain, and "lost 117 of 40" is not a
  number the ledger can hold.

- **Every count and the position beside it are read in one breath.** They are
  taken under a single lock and handed over together, because a position read a
  moment after its counts can place more losses than the count admits to, and
  both the entity and the table reject that pairing. Reading them apart is the
  bug this rule exists for.

- **Where a loss happened is kept as a second of the stream's own clock.** The
  programme clock reference is what the file is played back against: a byte
  offset only approximates it at a variable bit rate, and the wall clock keeps
  running while a recording is interrupted — which is the recording with the
  most losses to place. The 33-bit clock coming around is followed through; a
  jump the broadcast spliced in is written down as a re-anchor instead, so the
  timeline only ever reads forwards. A packet that sets the discontinuity
  indicator is taken at its word however small the jump; the size test behind it
  admits a hundred times the longest gap the standard leaves between two clock
  readings, and is there only for the breaks nobody declared.

- **The driver announces progress every thirty seconds** for as long as anything
  is being recorded, so that a recording which dies part way through need not be
  indistinguishable from a perfect one. Only the driver's half of that is in
  place: nothing yet subscribes to the signal and nothing yet writes the counts
  into the ledger, so today the numbers still only reach the ledger when the
  recording ends.

- **The clock the positions are measured against is the one the recorded service
  carries, and the driver cannot yet know which that is.** Measurement runs on
  the raw chunk, which is the whole multiplex, so the timeline follows the first
  programme clock it hears and hands over to another only when that one goes
  silent. That is right while the recording is the whole multiplex; the moment a
  PID filter narrows the file to one service, the followed clock may belong to a
  service the file no longer contains. Deciding how the recorded service's clock
  reaches the session is a precondition for adding that filter, not a follow-up.

- **A search is one vocabulary and one predicate.** The names a search is asked by
  are declared once, in `ProgrammeSearchQuery`, which reads a query string into a
  `ProgrammeSearch`; the HTTP action passes it the query string it was called with
  and declares no argument of its own, and the OpenAPI document lists the same
  names from the same list, so nothing can rename half of it. What a broadcast
  type means and which services the guide does not list are worked out below the
  application service, because a caller that judged a programme without them would
  disagree with the search that returned it.

  **Searching still happens in the store.** The query matches on a stored generated
  column, `lower(pg_catalog.normalize(name || ' ' || summary, 'NFKC'))`, and no
  request folds text in C#. All the running application takes from
  `ProgrammeSearchText` is two constants the column definition is built from, so the
  form and the joining space are spelled once, and answering a request never reaches
  `String.Normalize`. What `ProgrammeSearchMatching` is for is everything that has to
  judge a programme **without** running a query, and the rule matcher is the first
  caller it has: a rule keeps its conditions as the same query string a search is
  asked by, is read by `ProgrammeSearchQuery`, has its broadcast type and the guide's
  unlisted services worked out by the same scope, and is then answered programme by
  programme by that predicate. That is a second implementation of the same
  predicate, and a second implementation is exactly how the search came to have two
  answers before.

  So the two are held equal by measurement rather than by intention.
  `ProgrammeSearchText` is that column written out in C#, and a database test pushes
  every code point the store can hold through both sides and compares — 1,112,063 of
  them, every one agreeing, and where a character is in one side's Unicode tables
  and not the other's, that side left it alone rather than folding it differently.
  `ProgrammeSearchMatching` answers a search in memory the way the query answers it,
  down to the wildcards `LIKE` reads and the order names come back in, and a database
  test runs the same programmes and the same searches through both arms. The stand-in
  the feature tests use is that same code, so those tests measure a predicate the
  store is checked against rather than a third one nobody compares to anything.

  **Folding in C# needs the runtime's own Unicode tables, so globalization is not
  invariant for the processes that do it.** With `InvariantGlobalization` on,
  `String.Normalize` returns its input unchanged and says nothing about it. The
  driver folds nothing, so it and its tests keep the invariant tables and a rule
  names the pair. Turning them on elsewhere is what makes a language environment
  variable able to change an answer, so **both application entry points pin the
  default culture to the invariant one before they do anything else**, and the
  reading and folding are measured under seven languages — including ones where
  lowercasing `I` and parsing `-1` genuinely differ — to say that no answer moves.

- **A visit of the guide is written in one statement, and the merge it does has two
  implementations.** `ProgrammeRepository.AbsorbAsync` hands the whole visit to the
  store as one `INSERT ... ON CONFLICT DO UPDATE ... WHERE (...) IS DISTINCT FROM (...)`,
  and the `CASE` expressions in it are `Programme.Absorb` written out in SQL: an
  empty name or summary keeps the one held, an end that is not after the start is no
  end and keeps the one held, an empty set keeps the one held, and a row whose
  answer would be the same takes no new revision. `Programme.Absorb` is what the
  in-memory stand-in runs, so the two are held equal the way the search's two arms
  are — `ProgrammeAbsorbArmsTests` pushes the same visits through both and compares
  every column and whether the revision moved. Measured on a copy of the running
  guide (8,727 rows), re-ingesting the same visit fell from about 3 s row by row to
  0.4 s, and a visit that changed every row from 80 s to about 1.2 s.

- **Which rule takes a programme is decided by weight, never by age or identifier
  alone.** Rules are read in falling priority, then oldest first, then by identifier
  as the last resort, and the first one to take a programme keeps it. A rule whose
  query cannot be read is turned off and reported rather than passed over in
  silence, and the run carries on: one unreadable rule does not silence the rest.
  What a page would have held — the sort, the page and how many fit on it — is read
  and then left out of the decision, because honouring the page size would drop
  programmes the rule was written to take.

- **The search across both layers keeps the "already held in the hot layer"
  exclusion above the union, never inside the archive arm.** A `NOT EXISTS` in the
  arm makes that arm a subquery the planner cannot merge into the append, and the
  ordered index path goes with it: a search whose start date reaches into the
  archive then sorts the whole archive to hand back one page. Above the union the
  same exclusion reads as an anti-join the primary key answers.

- **The page a search hands back is bounded; the count beside it is not.** The
  ordered path stops at fifty rows however far back the start date reaches, but
  `Total` still walks every archived row that matches — measured at roughly 53 ms
  per year held, so three years is about 160 ms and ten about 530 ms, spent before
  the page itself is read. Reaching back as far as the archive goes is the
  decision, so this arrives on its own as the archive grows. The ways out are an
  estimated total, cursor paging in place of a page count, or a count that stops
  at a ceiling and answers "more than". None of them is in place.

- **A recording that has ended is frozen except for its picture — as long as it is
  reached through the aggregate's own methods.** Every public method on `Recording`
  but one refuses once an outcome is set; `Illustrate` is the exception, and it
  moves the two thumbnail columns and nothing else. Reflection tests assert the
  whole set of methods, that no property carries a public setter, and that the only
  static entry points are the two that make a recording — so a new way in cannot
  appear without being accounted for. A database round trip says the outcome, the
  size and the reasons come back unchanged after a picture is drawn. **That is the
  guarantee, and it stops at the aggregate's surface:** the change tracker, raw SQL
  and reflection all reach past it, and only the trip wires below look for those.

  Around it, the thumbnail pass never asks for a recording that has not ended:
  it reads the ledger for rows with no picture yet rather than being called from
  the path that ends a recording. A recording that failed is skipped rather than
  illustrated, because a picture of it would say it was recorded; one that was cut
  short is illustrated, and the ledger still says it was cut short. When the
  picture cannot be drawn, the class of the failure is kept on the row beside the
  state, never in `outcome_detail`, which belongs to the recording's own result.

  **Two source rules sit on top of that, and they are trip wires rather than
  proof.** One reads every file whose path carries the word thumbnail — the feature
  folder and anything named beside it — and reports a call that says how a
  recording ended, or a reach past the aggregate through reflection, raw SQL or the
  change tracker's `Entry`/`Property`/`CurrentValue`, which is the way somebody who
  knows this mapper would write it by accident.
  The other reports a file outside the feature folder that names any of the types
  the feature is made of. Four files are allowed to: the two where the feature is
  built, and the two on the HTTP surface that offer a picture to be drawn again —
  a request to redraw one has to name the six answers the pass can give, so the
  surface cannot be written without naming them. The list is asserted whole, so a
  fifth is a deliberate act rather than a drift. **A helper whose name says
  nothing about thumbnails walks straight past both**, and a test asserts that
  plainly. What they catch is the ordinary way somebody would write it, not every
  way there is.

- **What a caller has to send is in the document.** A handler that reads a query
  input the framework never saw — indexed off `Request.Query` rather than declared
  as an argument — declares it beside its mapping, and the generated client can
  send it. What holds that is a feature test: the source of the HTTP surface is
  read for every query name a handler asks for, and each one is looked for in the
  served document under the path that file declares. A name read on a path the
  document disowns is listed rather than passed over, so a new one is a deliberate
  act.

  **It is a trip wire, not a proof.** It reads source text, so it sees the two
  ordinary spellings — the indexer and `TryGetValue` — and reports rather than
  skips a name it cannot follow: one built out of pieces, one held in a variable,
  one moved into a helper of its own. What walks straight past is a handler that
  never names what it reads — the whole query string, or the collection iterated.
  The programme search does exactly that, and its vocabulary is held in the
  document by a rule of its own. Headers, cookies and bodies are outside it, and a
  test says so.

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

Four filters divide the suite, and CI runs one job per filter:

| Filter | What it selects |
| --- | --- |
| under a `Unit` folder, or nothing more specific | everything that needs neither a database, the HTTP surface nor ffmpeg |
| `FullyQualifiedName~FeatureTest` | the tests that drive the application through its HTTP surface |
| `Category=DbIntegration` or a `DbIntegration` name | the tests that need a real PostgreSQL |
| `Category=Material` | the tests that write a synthetic broadcast with ffmpeg and read it back |

Each job counts the tests it ran and fails on zero, because `dotnet test` exits 0
when a filter matches nothing and a mistyped name would otherwise be green having
verified nothing.

The material job is the one that does not run on the bare runner: the runner image
carries no ffmpeg, and the one the application runs is built from source with
libaribcaption in the `ffmpeg-build` stage, so the job builds the `develop` target
from the build cache and runs the filter inside it. `SyntheticBroadcast` in
`Carina.BroadcastTestSupport` is where a broadcast is synthesised — the picture and
sound come from ffmpeg's own generators, the caption and superimposition streams are
written byte by byte and handed to the same ffmpeg run, and the dual-mono sound is a
hand-written AAC frame because no encoder in the build produces two single-channel
elements. Nothing generated is checked in: a three-second broadcast is written
bit-exact in under a second, and the same arguments give the same bytes.

A fifth filter, `Category=Scale`, is the one no job runs. It builds a year of the
programme archive — 410,000 archived rows beside 10,000 held ones, about half a
gigabyte — and times the search across both layers against a one-second budget.
That is not something to pay for on every push, so the unit job excludes the
category by name and nothing else selects it; `task test:scale` runs it by hand,
which is what to do when the search or the shape of either programme table
changes. Being compiled with everything else is what keeps it from rotting.

What it asserts is the plan and the blocks read, not the clock. Wall clock on the
same machine and the same data moves three to five times with how much of the
half gigabyte the page cache happens to hold, so a budget in milliseconds is worth
stating but cannot hold on its own: it was measured passing against the very
regression it exists to catch. The plan shape and the block counts do not move
with cache temperature, and between the two shapes they differ by two hundred
times.

The year-back search without a keyword is the shape that guards this. The keyword
shape beside it reads well but flips its plan with the statistics sample, and was
measured passing against both of the regressions it looks like it covers. Deleting
the plain one as redundant would leave the measurement non-deterministic.

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

The render node is the exception, because a machine that has one is the normal
case and the transcoder is meant to find it without being configured. `task up`
reads the host through `docker/dri-env.sh` and hands `/dev/dri` to `app` when it
is there, along with the owning groups of `card0` and `renderD128` as numbers
measured on that host — the render node's group is numbered differently from one
distribution to the next and is often absent from the container's own
`/etc/group`, so a name would resolve to something other than the device. A host
without the node sets nothing and the device entry falls back to `/dev/null`,
because a `devices:` entry naming a path that is not there stops the container
from being created at all.

Handing the node in is not the whole of it, because the image demotes `app` to
an unprivileged user before starting it and demoting with `--init-groups` reads
the supplementary groups back out of `/etc/group`, dropping whatever the
container was given from outside. So the `app` role reads the owning group off
the `card` and `render` nodes actually present in the container and hands the
demoted process those and nothing else. The number comes from the node rather
than from a name or a variable, so it cannot disagree with the device; a
container without the nodes gets exactly the set `--init-groups` gave it before;
and group 0 is never handed over, whichever side it came from.

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
