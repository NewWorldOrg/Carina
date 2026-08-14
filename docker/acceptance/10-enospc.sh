#!/usr/bin/env bash
set -euo pipefail

scenario_id="10-enospc"
. "$(cd "$(dirname "$0")" && pwd)/lib.sh"

driver_container="${prefix}-driver"
socket_volume="${prefix}-run"
roomy_volume="${prefix}-rec"
tiny="acc10tiny"
roomy="acc10roomy"

heading "a full disk fails one recording, diagnoses it, and leaves the driver running"

require_image_present

cat > "${workdir}/driver.json" <<'JSON'
{
  "socketPath": "/run/carina/driver.sock",
  "socketGroupId": 10001,
  "outputRoots": [
    { "name": "tiny", "path": "/srv/tiny" },
    { "name": "roomy", "path": "/srv/recordings" }
  ],
  "shutdownGraceHours": 6,
  "liveSessionMinutes": 240,
  "tuner": { "backend": "fake" },
  "devices": [
    { "id": "fake-terrestrial", "kind": "terrestrial", "enabled": true },
    { "id": "fake-satellite", "kind": "satellite", "enabled": true }
  ]
}
JSON
chmod 0644 "${workdir}/driver.json"

docker volume create "${socket_volume}" >/dev/null
track_volume "${socket_volume}"
docker volume create "${roomy_volume}" >/dev/null
track_volume "${roomy_volume}"

docker run -d --name "${driver_container}" \
    -e CARINA_ROLE=driver \
    -e CARINA_DRIVER_CONFIG=/etc/carina/driver.json \
    -v "${workdir}/driver.json:/etc/carina/driver.json:ro" \
    -v "${socket_volume}:/run/carina" \
    -v "${roomy_volume}:/srv/recordings" \
    --tmpfs /srv/tiny:size=8m \
    "${image}" >/dev/null
track_container "${driver_container}"
diagnostics_container="${driver_container}"

if ! wait_until 60 bash -c "docker run --rm --user 100:10001 -v ${socket_volume}:/run/carina ${curl_image} -sf --max-time 5 --unix-socket /run/carina/driver.sock http://localhost/health"; then
    docker logs "${driver_container}" 2>&1 | tail -20
    fail "the driver did not come up with an 8 MiB output root."
fi

boot_before="$(docker exec "${driver_container}" awk '{ print $22 }' /proc/1/stat)"
require_number "the driver's boot tick" "${boot_before}"

free_bytes="$(docker exec "${driver_container}" stat -f -c '%a * %S' /srv/tiny)"
note "the small output root reports $((free_bytes)) bytes free, and the synthetic tuner writes that in well under a second"

start_recording "${roomy}" fake-satellite roomy 240 satellite 31
start_recording "${tiny}" fake-terrestrial tiny 240

if ! wait_until 60 bash -c "docker run --rm --user 100:10001 -v ${socket_volume}:/run/carina ${curl_image} -s --max-time 15 --unix-socket /run/carina/driver.sock http://localhost/sessions | python3 ${acceptance_dir}/read-json.py session ${tiny} state | grep -qx failed"; then
    driver_get /sessions
    fail "the recording on the full output root never failed: ${reply}"
fi

driver_get /sessions
json "${reply}" session "${tiny}" failureCause
cause="${text_value}"

if ! grep -qi 'no space left on device' <<<"${cause}"; then
    fail "the recording failed for '${cause}', not because the disk was full; this scenario did not produce the condition under test."
fi
pass "the recording on the full output root failed and named the reason: ${cause}"

driver_get /diagnostics
json "${reply}" diagnostics-count recordingWriteFailed "${tiny}"
diagnosed="${text_value}"
require_number "the number of write-failure diagnostics for ${tiny}" "${diagnosed}"

if [ "${diagnosed}" -lt 1 ]; then
    fail "the driver kept the failure to itself; /diagnostics carries no recordingWriteFailed for ${tiny}: ${reply}"
fi

json "${reply}" diagnostics-detail recordingWriteFailed "${tiny}"
pass "the driver published the failure as a diagnostic event with a reason: recordingWriteFailed, ${text_value}"

inspect_field "${driver_container}" '{{.State.Running}}'
if [ "${text_value}" != "true" ]; then
    fail "the driver container is not running after a full disk."
fi

boot_after="$(docker exec "${driver_container}" awk '{ print $22 }' /proc/1/stat)"
if [ "${boot_after}" != "${boot_before}" ]; then
    fail "the driver process was replaced (boot tick ${boot_before} -> ${boot_after}); it did not survive the full disk, it was restarted."
fi

driver_get /health
json "${reply}" field protocolVersion
pass "the same driver process is still serving (boot tick ${boot_before}, protocol version ${text_value})"

driver_get /tuners
json "${reply}" tuner fake-terrestrial state
if [ "${text_value}" = "faulted" ]; then
    fail "the device was isolated because the disk was full; a write failure is not a device failure."
fi
pass "the device whose recording failed is not faulted: it reports ${text_value}"

driver_get /sessions
json "${reply}" session "${roomy}" state
if [ "${text_value}" != "active" ]; then
    fail "the recording on the other output root is '${text_value}'; the failure spread beyond the session that hit the full disk."
fi

recorded_bytes "${roomy}"
roomy_first="${number_value}"
sleep 3
recorded_bytes "${roomy}"

if [ "${number_value}" -le "${roomy_first}" ]; then
    fail "the recording on the other output root stopped growing at ${roomy_first} bytes."
fi
pass "the recording on the other output root kept going: ${roomy_first} -> ${number_value} bytes"

stop_recording "${roomy}"

if ! wait_until 60 bash -c "docker run --rm --user 100:10001 -v ${socket_volume}:/run/carina ${curl_image} -s --max-time 15 --unix-socket /run/carina/driver.sock http://localhost/sessions | python3 ${acceptance_dir}/read-json.py session ${roomy} concluded | grep -qx true"; then
    fail "the surviving recording never concluded."
fi

driver_get /sessions
json "${reply}" session "${roomy}" bytesRecorded
roomy_bytes="${text_value}"
require_number "the surviving recording's byte count" "${roomy_bytes}"

recording_size "${roomy_volume}" "${roomy}.ts"
if [ "${number_value}" -ne "${roomy_bytes}" ]; then
    fail "the surviving file is ${number_value} bytes but the driver counted ${roomy_bytes}."
fi

check_continuity "${roomy_volume}" "${roomy}.ts"
pass "the recording that shared the driver with a failing one is byte-continuous"

docker stop -t 60 "${driver_container}" >/dev/null
inspect_field "${driver_container}" '{{.State.ExitCode}}'
if [ "${text_value}" != "0" ]; then
    fail "the driver exited ${text_value} after a full disk, not 0."
fi
pass "the driver exited 0 on its own terms afterwards"

note "configuration under test: outputRoots tiny -> an 8 MiB tmpfs, roomy -> a normal volume; the criterion's"
note "injection is a real ENOSPC from the kernel, not a fault the driver was told to simulate."
