# Acceptance scenarios

The twelve acceptance criteria of the foundation domain, as scripts that run against the
distribution image and the deployment stack rather than against test doubles.

```bash
task image                 # build the image these run against
task acceptance            # every scenario
task acceptance -- 01 07   # a subset, by number
```

`docker/acceptance/run.sh` prints a PASS/FAIL table and exits non-zero if any scenario failed.
Every scenario cleans up its own containers, volumes, networks and images.

## Why this shape

These are container lifecycle facts: replace a container, signal a process, kill it outright,
mount a full filesystem, run a driver built from an old commit. A shell harness driving Docker
observes all of that directly, and `docker/verify-image.sh` had already established the idiom —
role-level assertions against the real image, callable from CI. An xunit project driving Docker
would add a layer that cannot see more than the shell can, while the C# suite's own job is the
opposite one: it holds the fast, deterministic tests against the driver double
(`tests/Carina.TestSupport`), whose own note says only this task can prove the rest.

Two rules the scenarios follow, both learned here:

- **Nothing passes for an unrelated reason.** Each scenario asserts that the condition under test
  was actually produced before asserting the behaviour: the app container really was replaced, the
  socket really was stale (`ECONNREFUSED`), the outsider really was refused by the permissions
  (`EACCES`, not a missing file), the slow reader really did fall behind (`droppedChunks > 0`), the
  disk really did fill (`No space left on device`), the old driver really is old (no `draining` in
  its hello, `404` on `/diagnostics`, a binary with a different hash).
- **A failure inside `$(...)` is not a pass.** Helpers that can fail set a global instead of
  printing, numbers are checked with `require_number` before any comparison, and command
  substitutions capture the status explicitly. `docker/grace-period.sh` was once green while
  comparing empty strings; that is the trap being avoided.

## Where each criterion stands

| # | Criterion | Automated by | Runs in CI |
|---|---|---|---|
| 1 | app replacement | `01-app-replacement.sh` | yes |
| 2 | SIGTERM linger | `02-sigterm-linger.sh` | yes |
| 3 | CI tag behaviour | **not here** — `docker/image-tags.sh prove` on `feature/ci-tagging` (C-基盤-003) owns it | — |
| 4 | configuration driven | `04-configuration-driven.sh` | yes |
| 5 | stale socket | `05-stale-socket.sh` | yes |
| 6 | socket permissions | `06-socket-permissions.sh` | yes |
| 7 | version skew | `07-version-skew.sh` for the live combination; the degraded case stays with the double | yes |
| 8 | backpressure independence | `08-backpressure.sh` | yes |
| 9 | fail-closed | `09-fail-closed.sh` | yes |
| 10 | ENOSPC | `10-enospc.sh` | yes |
| 11 | architecture test self-check | **not here** — `ReferenceRuleSelfCheckTests` in `tests/Carina.Architecture.Tests` | yes, in the `test` job |
| 12 | all-role zombie reaping | **not here** — `docker/verify-image.sh` | yes, in the `image` job |

Criteria 3, 11 and 12 are automated elsewhere and are not duplicated here. They are listed so that
reading this table tells you where every criterion lives, not only the ones this suite runs.

## What is not automated, and what it would take

- **The linger cap itself (part of criterion 2).** `02` proves the driver stays up until its
  recording ends. It does not prove the driver gives up at the cap, because
  `shutdownGraceHours` has a floor of 1 hour (`DriverConfigurationReader`), so a recording that
  outlives the cap has to run for over an hour. Automating it needs either a configuration floor
  below an hour or a scenario allowed to take that long; neither belongs on a shared runner.
  The `DrainCapReached` path has unit coverage in `tests/Carina.Driver.Tests`.
- **A driver that advertises less than today's app expects (part of criterion 7).** No driver
  that has ever existed advertises fewer capabilities than `recording`, `live`,
  `qualityMetering`, or a protocol version below 1, so no real old image can produce the
  degraded case. `07` proves the live combination — today's app against a driver binary built
  from `fb89e7d` (A-基盤-013, before A-基盤-014 existed). The degraded case is held by the
  FeatureTests of A-基盤-025 against the driver double, which is the only way to produce it.
- **The app's own report of the skew.** `driverUpdateRequired` and `missingCapabilities` are
  fields of `GET /api/driver/status`, which answers 401 until the authentication domain
  registers a scheme. Nothing outside a test host can read them, so `07` observes the
  combination from the driver's side (the app's requests, served without a refusal) and from
  the app's liveness. When authentication lands, `07` should read the status body directly.
- **Criterion 3.** The two tag streams are the subject of C-基盤-003, whose
  `docker/image-tags.sh prove` measures it on a scratch worktree every CI run. It is not on
  `master` yet; when it merges, that is where criterion 3 is proven, and nothing here needs to
  change.

## Residual risk in the continuity check

`docker/check-recording-continuity.py` reads the synthetic tuner's own counters: a continuity
counter mod 16 and a payload counter mod 256. The stream is therefore exactly periodic every
256 packets, or 48,128 bytes, and a loss of exactly N × 48,128 bytes is bit-for-bit
indistinguishable from no loss at all. That is a property of the stream, not of the checker: no
reader of the file can close it.

The scenarios narrow it from two sides instead:

- the file's length is compared against `bytesRecorded`, the driver's own count of what it
  handed to `write(2)`, which is what closes the checker's other blind spot — a truncated tail
  is no longer indistinguishable from a short recording;
- `counters.drops` and `counters.discontinuities` are asserted to be zero, which is the
  driver's independent measurement of the same bytes.

What remains uncovered is a loss of exactly N × 48,128 bytes upstream of the driver's byte
count. Closing it needs a wider counter in `FakeTunerDevice` — for instance a 64-bit packet
index in the payload — which would make every gap visible and every scenario's continuity
assertion exact. That is a change to a production file and belongs to whoever next touches the
synthetic tuner.

## Cost

The synthetic tuner writes as fast as the CPU allows — around 150 MB/s on a developer machine.
Recordings in these scenarios are kept short and stopped as soon as their assertions are done,
but a scenario still writes on the order of a gigabyte before it deletes its volumes. The
scenarios run one at a time for that reason. `07` also compiles a second driver with Native AOT,
which is the slowest step in the suite.
