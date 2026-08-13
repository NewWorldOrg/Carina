# The contracts the OpenAPI document cannot hold

Three surfaces carry traffic that does not fit a request/response schema. They are absent
from `Carina.Api.json` on purpose, which is exactly why they need to be written down: a
consumer that only reads the generated client would never learn they exist.

| Surface | Path | Framing | Owner |
|---|---|---|---|
| Transport stream | `/sessions/{id}/stream` (driver socket) | HTTP chunked, raw transport stream bytes | driver |
| Event hub | `GET /api/events` | `text/event-stream`, signals without payload | app, one hub for every domain |
| Bulk programme guide | `GET /api/programs/bulk` | chunked NDJSON, cursor continuation | app, programme guide domain |

Everything below is the state of the contract, not a plan. Where a surface is not built
yet, that is said in its own section.

## Transport stream

**What it is.** The bytes of a tuner session, unwrapped. The driver serves
`/sessions/{id}/stream` over the Unix socket as HTTP chunked transfer with nothing but
transport stream packets in the body: no envelope, no framing of our own, batched in
multiples of the 188-byte packet size. One session is one connection; head-of-line
blocking is solved by opening another connection, never by multiplexing.

Recording bytes do not travel this path. The driver writes recording files itself, so a
slow or absent reader on this surface cannot affect a recording — only live viewing and
playback read it.

**Failure shape.** A failure after the first byte aborts the connection instead of closing
it cleanly. A receiver treats EOF and exception alike as *incomplete, reconnect required*.
Closing cleanly on failure is the one thing this contract forbids, because a truncated
recording that ends cleanly is indistinguishable from a complete one.

**Backpressure.** Per reader. A reader that falls behind is disconnected on its own and
takes nobody else down with it.

**Who owns it.** The driver owns the socket-side surface, and it exists today. The
app-side surface for browsers is owned by the streaming domain and is *not this framing*:
the live path there multiplexes video, audio, subtitles and control over a WebSocket, and
recorded files are served with range requests. No app-side path serving raw transport
stream bytes has been declared; if the streaming domain adds one, it belongs in this table.

**Compatibility.** Raw bytes, permanently stable, no version negotiation. What can change
is which sessions exist and what they mean, and that is negotiated in the JSON contract
(protocol version plus capabilities), never here.

**Authentication.** The driver side is deliberately unauthenticated: the socket's
permissions are the gate, and the privileged process is not given secrets to check. The
app side requires authentication and answers `401` — never a redirect to a login page,
because a player follows neither.

## Event hub

**What it is.** One server-sent-event endpoint that says *something of this kind changed*.
Nothing else. The receiver re-fetches the affected resource over the REST surface that this
document does describe.

**Shape.** Event names only, no payload. The set is closed and lives in code as
`Carina.Contracts.AppEvents`:

`tuners` · `programs` · `epgCollection` · `reservations` · `rules` · `recordings` ·
`quality` · `live` · `encodeJobs`

Names are lower camel case; a name for a set of resources is plural, a name for an ongoing
activity is singular.

The driver has its own hub at `/events` on the socket with its own, different closed set
(`Carina.Contracts.DriverEvents`), and those events do carry payloads. The two hubs are not
the same contract and their name sets are not related.

**Who owns it.** The foundation owns the hub and the name set. A domain adds a name; no
domain adds a second hub.

**Compatibility.** Additive only: names may be added, never renamed or removed, and a
consumer ignores names it does not know, so a server may add one before its consumer knows
it. Payloads are forbidden rather than merely discouraged — the moment an event carries
data, the shape of that data becomes an API that additive-only can no longer protect, and
the receiver starts trusting a diff it cannot verify.

**Authentication.** Required, judged once when the connection is established rather than
per message. A consumer that receives `401` closes explicitly instead of reconnecting
forever, so a dead session shows as a dead session rather than a screen that quietly stops
updating. Comment lines keep the connection alive through proxies that drop idle traffic.

**Status.** The name set is fixed in `Carina.Contracts`; the hub endpoint is not built yet.
The path is written here as `/api/events` because the deploy contract routes `/api/*` to
the app and everything else to the web frontend, so a hub outside that prefix would never
reach this process.

## Bulk programme guide

**What it is.** A synchronisation surface for tens of thousands of programme rows, kept
apart from the grid and search endpoints because their load characteristics have nothing in
common. The document surface answers one broadcast day for one screen; this one hands over
the whole working set incrementally.

**Shape.** `GET /api/programs/bulk` responds with chunked NDJSON — one JSON object per
line — ordered by a monotone revision, cursored by keyset. A response carries at most 5,000
rows plus a cursor to continue from. A cursor from a superseded epoch is answered with
`{"op":"reset"}`, which tells the consumer to discard what it has and start again.

Only the working layer is in scope. Rows that age out of it are not tombstoned: the
consumer drops rows whose broadcast ended more than 24 hours ago by convention. Deletions
outside that rule — a service disappearing, a purge — are tombstoned explicitly, because
a consumer cannot infer them from time.

**Who owns it.** The programme guide domain. Not built yet.

**Compatibility.** The same additive-only rule as the rest of the contract: fields are
added, never removed or repurposed, and an unknown field is ignored. `{"op":"reset"}` is
the escape hatch for the one case additive changes cannot cover — when the server can no
longer explain the difference between what the consumer has and what is true.

**Guards.** A row cap per response, a concurrency cap, and a statement timeout. A long
chunked connection is the easiest surface on which to hold a database open, and it is
authenticated like everything else.
