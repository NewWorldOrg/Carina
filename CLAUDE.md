# Carina

Backend of a self-hosted recording system for Japanese digital broadcasting: the
unprivileged app process that serves the HTTP API around a privileged driver
process. The driver, which owns the tuner hardware and writes recordings, lives in
its own repository, Puppis, and ships as an image of its own.

Two properties decide most of what follows, and are the argument to answer before
relaxing any of it:

- **A recording in progress survives a deployment.** The driver is a separate
  process on its own release stream; replacing the app must not touch it.
- **Recording quality is observable.** Continuity errors are counted while the
  recording is being written, so a broken recording is found by searching for it
  rather than on playback.

`README.md` is for running this — setup, configuration, image roles, how the
driver is reached. This file is for changing it, and does not repeat what is
there.

## Tech Stack

- .NET 11, ASP.NET Core, xUnit.
- EF Core and PostgreSQL. Migrations are applied by the `Carina.Db --migrate`
  entry point, never by the app on startup.
- The app reaches the driver over HTTP/1.1 on a Unix domain socket. The contract —
  the endpoints, events and messages the two exchange — is the NuGet package
  `Puppis.Contracts`, published from the driver's repository and pinned to one
  version in `Directory.Packages.props`. Nothing of the contract is defined here.
- Build settings are central in `Directory.Build.props`, package versions in
  `Directory.Packages.props`. Neither is repeated per project.

## Architecture

One process, one repository; the driver is the other half of the pair and is
reached only through the contract package.

| Project | What it holds |
| --- | --- |
| `src/Carina.Domain` | entities, value objects, repository interfaces and domain services, grouped by aggregate |
| `src/Carina.Broadcast` | broadcast-standard parsing, as a library that depends on nothing |
| `src/Carina.Infrastructure` | persistence, the driver IPC client, external boundaries |
| `src/Carina.Db` | the migration entry point |
| `src/Carina.Api` | the HTTP surface, and the OpenAPI document it serves |

A request reaches an action, the action calls an application service, and the
service either calls a domain service, which reaches persistence and the driver
through interfaces the domain declares, or calls the infrastructure directly. A
background job enters at the same level, calling a service or a domain service
from a timer.

Reference direction is one way, and `tests/Carina.Architecture.Tests` holds it,
reading the project files rather than the compiled output so that a reference
declared but not yet used is still caught:

- `Carina.Domain` references no project, and the one package it references is
  `Puppis.Contracts` — the driver client interface speaks the wire types, and
  mirroring them would duplicate every additive contract change. A source rule
  keeps `DriverEndpoints` and `DriverJson` out of the domain even though both
  compile against it.
- `Puppis.Contracts` is pinned once, in `Directory.Packages.props`; no project
  names a version of its own. Raising it is a deliberate act, made in the same
  change as the first use of what the new version adds.
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

- **The contract is additive, and the app reads the driver's capabilities before
  asking.** Puppis never removes or renames an endpoint or an event, so an old
  driver with a new app is the normal state, and so is a new driver with an old
  app. A driver that cannot do something says so in its greeting; the app asks
  nothing the greeting does not list.
- **App events are signals, not messages.** A producer signals through
  `IAppEventPublisher` with a `Puppis.Contracts.AppEventName` and nothing beside
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
- **The head the recorder allows is the time the driver spends getting to the
  first byte.** `RecordingSettings` names the three waits — a seat, the lock, the
  first byte — in whole seconds, and a rule holds them equal to what the driver
  waits. The driver's side of the same rule lives in Puppis, pinned to the same
  three numbers; moving one means moving both.
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
  call that throws away more than one. The app removes only what it made from the
  recording — its picture, its captions and the record of its data broadcast —
  the picture in a directory of its own, the other two side by side in the
  captions directory. A rule lists every way the source tree could change what
  is on disk, and the list holds no `FileStream` opened for writing: no recording
  is written here.

  A file that no recording owns goes the same way, and only as something the most
  recent ledger check found. The caller names the finding, never a path: the app
  resolves it to the root, path, size and last write the check kept, checks again
  that nothing claims the file and that it has not changed, and hands exactly
  those to the driver, which refuses anything that is not what it was handed. A
  finding about a recording's own file is never a way to remove it.

  The check also walks the places the app writes into itself — the encode roots,
  the thumbnail directory and the captions directory, which holds the records of
  data broadcasts beside the captions — and the app removes a file
  there that nothing claims, with the same checks made again just before it
  unlinks. Such a place is not walked when it shares a name or a directory with a
  recording root, or holds a file under a recording's own file name, so a
  recording root seen from another path is never judged as one of them.
- **The version is declared once, in `Directory.Build.props`.** Every assembly
  built here carries it, the application answers with what it was built as, and a
  test holds the two together. The contract package carries the driver's version,
  not this one.
- **The app asks the driver for nothing it is not entitled to by the socket.**
  The driver authenticates nobody; the gate is the socket's permissions and owning
  group, which is why the entrypoint demotes the app into the group the driver
  gives its socket by default, and a rule holds that number against the
  `Dockerfile`.
- **No endpoint exempts itself from the default denial,** and no production
  source reads an identity handed to it by an edge. Authentication is decided by
  the session the request carries, not by a header a proxy could be talked into
  setting.
- **The Hybridcast frame's two relays are the only surfaces another origin may read, and the only ones a POST
  reaches without naming this origin.** The frame is sandboxed without an origin of its own and sends no cookie;
  the ticket in the path is its only credential. `OpenToTheFrame` gives those routes a CORS policy of any origin
  without credentials and takes them out of the Origin check on state-changing requests, and the WebSocket one
  takes an origin of `null`. Every other route carries no CORS header and keeps its checks, and a test lists the
  routes so marked.
- **The OIDC client secret is read in the clear in two files only** — where it is
  stored and where it is spent. Neither of them logs, and nothing that answers a
  caller can name it.
- **No log names the path a request came on,** because a playback ticket can
  travel in the path of four surfaces: the two an external player opens and the
  two a Hybridcast application's frame opens. A line about a
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
- **Nothing here takes the transport stream apart or counts it, except the
  library whose trade is parsing.** The driver counts continuity as it writes,
  and `Carina.Broadcast` reads sections and tables; a rule lists the files
  outside that library carrying two or more marks of a transport-stream parser
  (the three type names, the sync byte, the 188 and 184 strides, the pid and
  continuity masks) and the list is empty, and no file names the driver's
  counter. **Trip wire:** a loop that shows one mark or none — a stride written
  `4 + 180 + 4`, lower-case hex — walks past.
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
- **Every count and the position beside it are written together.** A position
  read a moment later than its count can place more losses than the count admits
  to, so both the entity and the table reject that pairing; the driver reads the
  two under one lock before it sends them.
- **The driver announces progress every thirty seconds** while anything is being
  recorded; the app wakes on it and writes the counts into the ledger as the
  recording runs, so one that dies part way through can be told from a perfect
  one.
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

  The marks a programme carries — the ARIB symbols for a new series, a repeat, a
  last episode and the rest — are a second stored generated column, `marks`, read
  from the same name and summary in both tables. `ProgrammeMarks` is that column
  written out in C#, and the store builds the column from the same table of
  symbols; a database test sends every mark's symbol, and symbols that are not
  marks, through both.

  Folding in C# needs the runtime's Unicode tables (with `InvariantGlobalization`
  on, `String.Normalize` silently returns its input). No project here keeps the
  invariant tables, and a rule says so. The application entry point pins the
  default culture to the invariant one before anything else, and the reading and
  folding are measured under several languages to show no answer moves.
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
  the record of its data broadcast, what throwing it away left behind and when it
  was descrambled — as long as it is reached through the aggregate's own
  methods.** Every public method on `Recording` that changes it but seven refuses
  once an outcome is set, and a method that answers a question changes nothing.
  Of the seven, `Illustrate` moves the two thumbnail columns; `Caption` moves the four
  caption columns, and refuses anything but waiting while the recording is still
  being written; `DataBroadcastTaken`, `DataBroadcastFailed` and
  `DataBroadcastAgain` move the four data broadcast columns, which `Settle` puts
  to coming as the recording ends; `Erased` moves the two columns that say a
  deletion left files on disk (a deletion that took everything removes the row);
  `Descrambled` moves `descrambled_at`, only on a recording that ended with
  `ScramblingUnresolved` and has not been descrambled yet, leaving the outcome as
  written, and puts a data broadcast record already taken back to coming.
  Reflection tests assert the whole set of changes and of questions, that no
  property has a public setter, and that the only static entry points are the two
  that make a recording. The change tracker, raw SQL and
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
- **The address of a webhook is sealed before it is kept, and nothing shows it
  again.** The row of a destination holds the sealed address and its last four
  characters. The address is read as written in two files only, the type that
  seals it and the line that sends to it; neither writes to a log, and nothing
  that answers a caller can name it. `WebhookUrlRuleTests` reads the source for
  each of the three and a self-check shows the rule sees an offence. What the
  other side answered is neither kept nor shown. **Trip wire:** an address
  copied into a plain string in one of the two files and handed on from there
  walks past.
- **A notice is taken by reading the ledgers, never by listening.** The intake
  looks on a timer at what recordings, reservations, encodes and the quality
  ledger say happened, and writes each happening once: the notice ledger is
  unique on the kind and the id of the row it came from, so a look that runs
  twice, or two processes that look at once, write one notice. The notice and
  its deliveries are written in one transaction. A signal that was never
  raised, or raised while the process was down, therefore loses nothing.
- **A programme asked to be told of uses no tuner.** Its row points at no other
  table and is in none of the reservations, the reservation views, the plan
  that hands out tuners, the capacity, the conflicts or the results of
  recordings, so it can stand beside a reservation for the same programme. It
  is told to the destinations asked for with it and to no rule set. The schema
  tests refuse a foreign key on the row and a view that names its table, and a
  rule test refuses any type of the reservations, the plan or the capacity that
  mentions the row, its ledger or its look in what it holds, takes, returns or
  calls. **Trip wire:** the rule knows those types by their namespace and their
  names, and sees neither a use through reflection nor SQL written out in a
  string that joins the row to a reservation.

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
- A business decision one aggregate can make from its own state and the values
  handed to it is a method of that aggregate. A calculation that needs only
  values, such as `TunerAllocationPlanner.Plan`, stays a static pure function.
- A decision that needs a repository lookup, or spans several aggregates, is a
  domain service: a sealed, non-static class in `Carina.Domain/<Area>/` whose
  name and file name both end in `DomainService`, taking only interfaces the
  domain declares and `TimeProvider` through its constructor. The outer layers
  register it, so the domain still references nothing beyond `Puppis.Contracts`.
  It answers a refusal as a domain type; turning that into HTTP is the Api's job.
- An application `{Entity}Service` keeps the orchestration only: the transaction
  and the turn lock, calling domain services and aggregates, saving, and mapping
  the answer to `ServiceResult`.
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
referencing no production assembly. `DomainServiceRuleTests` there holds the
shape of a domain service: a class of the domain that takes a domain interface or
`TimeProvider` through its constructor is one, except the outcomes that carry a
live stream, a transcoder seat or a socket back to their caller, which the rule
lists by type, and no type outside the domain carries the name.

## Tests

There is one test project per production project, plus `Carina.Architecture.Tests`
and `Carina.Conventions.Tests` for the rules above. `Carina.TestSupport` and
`Carina.BroadcastTestSupport` carry the shared fakes and fixtures; no test project
may reference another test project, and the shared support reaches no further than
the domain.

The driver is not built here, so no test starts one. The tests that drive the
application through its HTTP surface stand a fake driver on a Unix socket, speaking
the contract package; what the pair does together — a recording carried across an
app swap, the counts a driver keeps while the app is away — is proven where both
halves are present, not in this repository.

A test sits where its kind and the layer of what it tests put it. A feature test
drives the running application from outside through HTTP, a unit test needs
neither a database, the HTTP surface nor ffmpeg, and a database test needs a real
PostgreSQL. `<Area>` is the folder the subject sits in under its own layer:
directly under `src/Carina.Domain`, `src/Carina.Infrastructure` or
`src/Carina.Api/Controllers`. A type of the API outside `Controllers` goes under
the area it serves, and what `src/Carina.Api/Authentication` holds belongs to
`Auth`.

| Folder | What is tested there | Class |
| --- | --- | --- |
| `Carina.Api.Tests/Feature/<Area>/` | one HTTP action per class; an endpoint or a part of the request pipeline that the start-up code wires in directly gets a class of its own | `<Verb><Entity>ActionTests`, or `<Class>Tests` after the class of the API that `Program.cs` or `Extensions/` names |
| `Carina.Api.Tests/Unit/App/<Area>/` | an application service, or another type of the API, with stand-in repositories | `<Type>Tests` |
| `Carina.Api.Tests/DbIntegration/App/<Area>/` | an application service over a real database; business behaviour that passes through no domain service is tested here | `<Name>ServiceTests` |
| `Carina.Domain.Tests/Unit/<Area>/` | aggregates, value objects, static functions of the domain, and domain services with stand-in repositories | `<Type>Tests` |
| `Carina.Infrastructure.Tests/DbIntegration/Domain/<Area>/` | a domain service or a repository over a real database; a test that holds SQL equal to an aggregate sits with the domain service or repository whose SQL it is | `<Name>DomainServiceTests`, `<Name>RepositoryTests` |
| `Carina.Infrastructure.Tests/Unit/Infra/<Area>/`, `DbIntegration/Infra/<Area>/` | what exists in the infrastructure alone: ffmpeg invocations, the driver client, EF configurations, file system adapters | `<Type>Tests` |
| `Carina.Db.Tests/DbIntegration/Infra/<Area>/`, `Unit/Infra/<Area>/` | the shape of the tables, and the migrations | `<Tables>SchemaTests`, `<Migration>MigrationTests`, `<Type>Tests` |

- A test class is named after what it tests, sits alone in a file of its own name
  and is in the namespace of its folder. Test methods are named as sentences.
- The class carries `[Trait("Category", "Feature")]`, `"Unit"` or `"DbIntegration"`
  to match its kind. A class of the scale or the material job carries `"Scale"` or
  `"Material"` instead.
- A test over a real database takes its subject from the container the application
  builds, never from a constructor. Under `DbIntegration/Domain` a repository is
  asked for as its domain interface and its class is not named at all; a domain
  service is asked for as itself, and so is an application service under
  `DbIntegration/App`.
- A feature class tests one action, or one class the start-up code names, and
  every action of an area that has feature classes has its class. No two feature
  classes share a name.
- A file that holds no test, such as a fixture or a stand-in, may sit beside the
  tests that use it.
- `Carina.Architecture.Tests`, `Carina.Conventions.Tests` and
  `Carina.Broadcast.Tests` are outside this layout.

The suite is moving into this layout one area at a time. Until an area moves its
tests stay where they were: under `FeatureTest/` and `Unit/` of the API tests,
under a folder named for the area, or at the root of a test project.
`TestLayoutRules.AreasNotYetMoved` in `Carina.Conventions.Tests` lists the areas
still to move. Moving an area means moving every test of it in the four projects
and taking its name off the list in the same change. `TestLayoutRuleTests` holds
the bullets above for every class inside the layout. For an area off the list it
refuses a test left behind: one under the area's old folder, one named after a
type of the area, an old feature test that speaks only to routes of areas that
have moved, or one with a namesake in the layout. A class at the root of a project
is tied to its area by those marks alone, so what they miss is caught when the
list is empty; from then on every test class outside the layout is refused.

Five filters divide the suite, and CI runs one job per filter:

| Filter | What it selects |
| --- | --- |
| under a `Unit` folder, or nothing more specific | everything that needs neither a database, the HTTP surface nor ffmpeg |
| `Category=Feature` or a `FeatureTest` name | the tests that drive the application through its HTTP surface, split by class into shards |
| `Category=DbIntegration` or a `DbIntegration` name | the tests that need a real PostgreSQL |
| `Category=Material` | the tests that write a synthetic broadcast with ffmpeg and read it back |
| `Category=Scale` | the measurements at archive and schedule scale |

Each job counts the tests it ran and fails on zero, because `dotnet test` exits 0
when a filter matches nothing. A separate job checks that every test is selected
by exactly one of the filters, and that every feature test falls in exactly one
shard. The filters read names as well as categories, so a test of another kind
whose name contains `FeatureTest` or `DbIntegration` is selected by no job, and
that check is what says so.

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
as errors, the format check and the test jobs above. A second workflow builds the
image and renders the compose file; it stays out of the way of draft pull
requests, and on `master` publishes the image as `sha-<commit>`, the first twelve
digits of the commit. A tag already in the registry is never pushed again. A third
workflow, on a `v*` tag, builds nothing: it refuses a tag that is not the version
`Directory.Build.props` declares, gives the image that commit was already
published under the tag `vX.Y.Z`, and moves `latest` onto it when the release is
the newest that is not a prerelease; `.github/release-latest.sh` makes that call
and its `prove` runs on every pull request.

`THIRD-PARTY-NOTICES.md` is checked by the image build. `docker/notices/nuget.sh`
writes the license of every NuGet package the app and migrate programs carry into
the image, and `docker/notices/check.sh` fails the build when the NuGet table or
the table of Ubuntu packages added to the base image differs from what the image
holds, or when a package listed under a GPL license has no source package beside
it. A package added needs its row; `Puppis.Contracts` has one, and its license
text is `docker/notices/nuget/Puppis.Contracts.txt` because the package declares
its license by expression and carries no text of its own.

## Development environment

`compose.yml` brings up `app` on the repository mounted at `/code`, `driver` and
`db`, the first two sharing `/run/carina` where the driver socket lives. `driver`
is the Puppis image at a version written in the compose file, not a build of this
repository; its configuration is the `configs` entry beside it, naming the
synthetic tuner backend, and reaches the container as the file
`CARINA_DRIVER_CONFIG` points at. No tuner device is mapped. Real hardware is
attached by an untracked compose override, which is also where the configuration
for a real machine belongs.

The render node is the exception, because the transcoder is meant to find it
without being configured. `task up` reads the host through `docker/dri-env.sh` and
hands `/dev/dri` to `app` when it is there, with the owning groups of `card0` and
of the render node as numbers measured on that host — the render group is numbered
differently from one distribution to the next and is often absent from the
container's `/etc/group`. A host without the node gets `/dev/null` in the device
entry, because a `devices:` entry naming a missing path stops the container from
being created.

An NVIDIA card is not a device entry at all: `gpu-nvidia.yml` reserves the host's
cards through the NVIDIA container runtime (`task up:nvidia`), which mounts the
driver's libraries into the container, and the image carries nothing of NVIDIA's.
The ffmpeg in the image is built with nv-codec-headers, so it loads those
libraries at run time and the NVENC probe fails cleanly on a host without them.
The image sets `NVIDIA_DRIVER_CAPABILITIES=video,compute,utility` because the
runtime's default leaves out `video`, and without it `libnvidia-encode` is never
mounted and every NVENC probe fails on a host that has the card.

The image demotes `app` to an unprivileged user before starting it. The `app` role
reads the owning group off the `card` and `render` nodes actually present in the
container and hands the demoted process those beside its own group, so the number
comes from the device rather than from a name; group 0 is never handed over.

The `driver` service runs the driver as its own main process, so it receives
SIGTERM directly and `stop_grace_period` covers the recording linger. Changing the
driver means changing Puppis and moving the version in the compose file; nothing
built from this tree reaches it.

`--migrate` takes a PostgreSQL advisory lock, so a second one waits instead of
racing. Do not run it in parallel: two migrations still make the slower deploy
wait on a lock it cannot see.

## Domains

The system is the sum of the domains below. Each carries its own share of the
HTTP surface, its own tables and its own tests.

1. Foundation — the app skeleton, IPC over the Unix socket, and an execution
   environment driven entirely by configuration
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
12. Notifications
