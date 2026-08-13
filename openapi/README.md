# The published HTTP contract

`Carina.Api.json` is the OpenAPI document for the app process, checked in as a build
artifact. It is the source of truth for the web frontend, which generates its client
from this file and holds no code dependency on this repository.

## Regenerating

```bash
task openapi
```

That builds `src/Carina.Api` and runs the `GenerateOpenApiDocuments` target, which
starts the application's document pipeline and writes the result here. The generated
file is the same document `GET /openapi/v1.json` serves; `OpenApiArtifactTests` fails
when the two drift apart, so a contract change that is not regenerated fails the test
run rather than reaching a consumer as a surprise.

Generation starts the application host, so the settings it validates at startup have to
be present. The development containers already carry them; the CI job passes placeholder
values of its own. Nothing is connected to — the host is built, described and dropped.

CI deletes this file first, regenerates it on the runner, and fails if the working tree
differs, so the check cannot pass by a generation that quietly did nothing. The file is
published as a build artifact of that job.

## Why a file and not an endpoint

`GET /openapi/v1.json` is behind the default-deny authentication seam and answers 401
until authentication is configured. Serving it anonymously outside Production would
put a hole in the seam that fail-closed exists to prevent, and it would still not help
the consumer: the frontend lives in its own repository with its own CI and no way to
run this application. A file at a pinned ref is fetchable without running anything,
and every change to the contract shows up in a reviewable diff.

## What this document does not contain

Three contract surfaces do not fit REST and are absent from it by design: the transport
stream, the server-sent event hub, and the bulk programme guide. They are declared in
[non-rest-contracts.md](non-rest-contracts.md).
