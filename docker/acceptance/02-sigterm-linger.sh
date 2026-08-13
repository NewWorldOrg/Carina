#!/usr/bin/env bash
set -euo pipefail

scenario_id="02-sigterm-linger"
. "$(cd "$(dirname "$0")" && pwd)/lib.sh"

session="acc02"
volume="${project}_recordings"
recording_seconds="${CARINA_ACCEPTANCE_LINGER_SECONDS:-20}"

heading "受入基準 2 — SIGTERM while recording does not end the recording"

stack_up

container_of driver
driver_container="${text_value}"

start_recording "${session}" fake-terrestrial primary "${recording_seconds}"
ends_at "${recording_seconds}"
deadline="$(date -u -d "${text_value}" +%s)"
require_number "the recording's end time" "${deadline}"
note "recording ${session} runs until $(date -u -d "@${deadline}" +%H:%M:%S)Z"

if ! wait_until 30 bash -c "docker run --rm -v ${volume}:/rec ${python_image} test -s /rec/${session}.ts"; then
    fail "the recording file never appeared, so there was nothing in flight when SIGTERM arrived."
fi

recording_size "${volume}" "${session}.ts"
size_at_signal="${number_value}"

signalled="$(date +%s)"
docker kill --signal=TERM "${driver_container}" >/dev/null
note "SIGTERM sent at $(date -u -d "@${signalled}" +%H:%M:%S)Z with $((deadline - signalled))s of recording left"

sleep 5

inspect_field "${driver_container}" '{{.State.Running}}'
if [ "${text_value}" != "true" ]; then
    inspect_field "${driver_container}" '{{.State.ExitCode}}'
    fail "the driver exited (${text_value}) five seconds after SIGTERM, abandoning a recording that had $((deadline - signalled))s left."
fi
pass "the driver is still running five seconds after SIGTERM"

recording_size "${volume}" "${session}.ts"
if [ "${number_value}" -le "${size_at_signal}" ]; then
    fail "the recording stopped growing at ${size_at_signal} bytes when SIGTERM arrived."
fi
pass "the recording kept growing while the driver was draining: ${size_at_signal} -> ${number_value} bytes"

set +e
probe_output="$(docker exec "${driver_container}" /opt/carina/driver/Carina.Driver --probe 2>&1)"
probe_status=$?
set -e

if [ "${probe_status}" -eq 0 ]; then
    fail "the driver's own probe called a draining driver healthy: ${probe_output}"
fi
pass "the driver's own probe reports the draining driver as unhealthy (exit ${probe_status})"
note "${probe_output}"

ends_at 600
set +e
answer="$(driver_curl -w '\n%{http_code}' -X POST -H 'Content-Type: application/json' \
    -d "{\"sessionId\":\"acc02late\",\"purpose\":\"recording\",\"tuning\":{\"kind\":\"satellite\",\"physicalChannel\":31},\"deviceId\":\"fake-satellite\",\"outputRoot\":\"primary\",\"endsAt\":\"${text_value}\"}" \
    http://localhost/sessions 2>&1)"
refusal_status=$?
set -e

if [ "${refusal_status}" -ne 0 ]; then
    fail "asking a draining driver for new work failed at the transport (curl ${refusal_status}): ${answer}"
fi

refusal="$(printf '%s' "${answer}" | tail -1)"
refusal_body="$(printf '%s' "${answer}" | sed '$d')"

if [ "${refusal}" != "503" ]; then
    fail "a draining driver answered ${refusal} to a new recording request; it should refuse with 503. Body: ${refusal_body}"
fi

if ! grep -q 'draining' <<<"${refusal_body}"; then
    fail "the refusal does not say it is draining: ${refusal_body}"
fi
pass "a draining driver refuses new work with 503 and says why: ${refusal_body}"

if ! wait_until 180 bash -c "test \"\$(docker inspect -f '{{.State.Running}}' ${driver_container})\" = false"; then
    fail "the driver never exited after its recording finished."
fi

exited="$(date +%s)"
inspect_field "${driver_container}" '{{.State.ExitCode}}'
exit_code="${text_value}"

set +e
outcome="$(docker logs "${driver_container}" 2>&1 | grep "Session ${session} on .* ended (")"
outcome_status=$?
set -e

if [ "${outcome_status}" -ne 0 ]; then
    docker logs "${driver_container}" 2>&1 | tail -20
    fail "the driver never wrote down how the recording ended, so there is no record that it finished at all."
fi
note "${outcome}"

if ! grep -q 'ended (EndTimeReached)' <<<"${outcome}"; then
    fail "the recording did not end by reaching its own end time (the driver writes the reason as EndTimeReached, which the wire calls endsAtReached): ${outcome}"
fi

final_bytes="$(sed 's/.* after \([0-9][0-9]*\) bytes.*/\1/' <<<"${outcome}")"
require_number "the byte count the driver wrote down for the recording" "${final_bytes}"
pass "the driver's own record of the recording says it reached its end time after ${final_bytes} bytes"

if [ "${exit_code}" != "0" ]; then
    fail "the driver exited ${exit_code} after draining, not 0."
fi

lingered=$((exited - signalled))
owed=$((deadline - signalled - 2))

if [ "${lingered}" -lt "${owed}" ]; then
    fail "the driver lingered ${lingered}s but the recording had ${owed}s or more left to run when SIGTERM arrived; it did not wait for it."
fi
pass "the driver lingered ${lingered}s, the whole of the ${owed}s or more it owed the recording, then exited 0"
note "the two seconds of slack are for clock rounding: the end time is named to the second while the driver decides on it with more precision"

recording_size "${volume}" "${session}.ts"
if [ "${number_value}" -ne "${final_bytes}" ]; then
    fail "the file is ${number_value} bytes but the driver counted ${final_bytes} bytes into it; the tail was lost."
fi
pass "the file is exactly the ${final_bytes} bytes the driver counted into it"

check_continuity "${volume}" "${session}.ts"
pass "the lingered recording is byte-continuous"

note "configuration under test: shutdownGraceHours 6 (docker/driver.development.json), recording length ${recording_seconds}s."
note "the linger cap itself is not exercised: the configuration floor is 1 hour, so cutting a recording short at"
note "the cap needs a recording longer than an hour. See docker/acceptance/README.md."
