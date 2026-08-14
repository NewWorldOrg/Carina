#!/usr/bin/env bash
set -euo pipefail

scenario_id="01-app-replacement"
. "$(cd "$(dirname "$0")" && pwd)/lib.sh"

session="acc01"
volume="${project}_recordings"

heading "replacing the app leaves the driver and the recording alone"

stack_up

container_of driver
driver_container="${text_value}"
inspect_field "${driver_container}" '{{.State.Pid}}'
driver_pid="${text_value}"
require_number "the driver's host pid" "${driver_pid}"
inspect_field "${driver_container}" '{{.State.StartedAt}}'
driver_started="${text_value}"
driver_boot="$(docker exec "${driver_container}" awk '{ print $22 }' /proc/1/stat)"
require_number "the driver's boot tick" "${driver_boot}"
note "driver container ${driver_container:0:12}, host pid ${driver_pid}, started ${driver_started}, boot tick ${driver_boot}"

app_requests "${driver_container}" /sessions
sessions_before="${count_value}"
app_requests "${driver_container}" /events
events_before="${count_value}"
note "before the replacement the app had asked for the session list ${sessions_before} time(s) and the event feed ${events_before} time(s)"

start_recording "${session}" fake-terrestrial primary 180
note "recording ${session} started"

if ! wait_until 30 bash -c "docker run --rm -v ${volume}:/rec ${python_image} test -s /rec/${session}.ts"; then
    fail "the recording file never appeared, so nothing was being written when the app was replaced."
fi

recording_size "${volume}" "${session}.ts"
size_before="${number_value}"

container_of app
app_before="${text_value}"

replace_started="$(date +%s)"
if ! stack up -d --force-recreate --no-deps app >/dev/null 2>&1; then
    fail "the app could not be replaced."
fi

container_of app
app_after="${text_value}"

if [ "${app_before}" = "${app_after}" ]; then
    fail "the app container is still ${app_before:0:12}; nothing was replaced, so this scenario proved nothing."
fi

recording_size "${volume}" "${session}.ts"
size_during="${number_value}"
pass "the app was replaced: ${app_before:0:12} -> ${app_after:0:12}"

inspect_field "${driver_container}" '{{.State.Pid}}'
if [ "${text_value}" != "${driver_pid}" ]; then
    fail "the driver's host pid moved from ${driver_pid} to ${text_value}."
fi

inspect_field "${driver_container}" '{{.State.StartedAt}}'
if [ "${text_value}" != "${driver_started}" ]; then
    fail "the driver container restarted: ${driver_started} -> ${text_value}."
fi

boot_after="$(docker exec "${driver_container}" awk '{ print $22 }' /proc/1/stat)"
if [ "${boot_after}" != "${driver_boot}" ]; then
    fail "the driver process inside the container was replaced: boot tick ${driver_boot} -> ${boot_after}."
fi
pass "the driver process is the same one: host pid ${driver_pid}, container start ${driver_started}, boot tick ${driver_boot}"

if [ "${size_during}" -le "${size_before}" ]; then
    fail "the recording stopped growing across the replacement: ${size_before} -> ${size_during} bytes."
fi
pass "the recording kept growing across the replacement: ${size_before} -> ${size_during} bytes"

if ! wait_until 60 bash -c "docker logs ${driver_container} 2>&1 | grep -c 'Request starting HTTP/1.1 GET http://driver/sessions' | awk -v seen=${sessions_before} '{ exit \$1 > seen ? 0 : 1 }'"; then
    fail "the replacement app never asked the driver for its session list, so it never readopted the recording in flight."
fi

app_requests "${driver_container}" /sessions
sessions_after="${count_value}"
app_requests "${driver_container}" /events
events_after="${count_value}"

if [ "${events_after}" -le "${events_before}" ]; then
    fail "the replacement app never subscribed to the driver's event feed (${events_before} -> ${events_after})."
fi
pass "the replacement app reconnected on its own: session list asked ${sessions_before} -> ${sessions_after}, event feed ${events_before} -> ${events_after}"

driver_get /sessions
json "${reply}" session "${session}" state
if [ "${text_value}" != "active" ]; then
    fail "the session the app readopted is in state '${text_value}', not the running recording it was meant to inherit."
fi
json "${reply}" session "${session}" instanceId
note "the readopted list carried ${session} as active, held by driver instance ${text_value}"

stop_recording "${session}"

if ! wait_until 60 bash -c "docker run --rm --user 100:10001 -v ${socket_volume}:/run/carina ${curl_image} -s --max-time 15 --unix-socket /run/carina/driver.sock http://localhost/sessions | python3 ${acceptance_dir}/read-json.py session ${session} concluded | grep -qx true"; then
    fail "the recording never concluded after it was asked to stop."
fi

driver_get /sessions
json "${reply}" session "${session}" state
final_state="${text_value}"
json "${reply}" session "${session}" bytesRecorded
final_bytes="${text_value}"
require_number "the driver's final byte count" "${final_bytes}"
json "${reply}" session "${session}" counters drops
drops="${text_value}"
require_number "the recording's dropped packet count" "${drops}"

if [ "${final_state}" != "stopped" ]; then
    fail "the recording ended in state '${final_state}': ${reply}"
fi

if [ "${drops}" -ne 0 ]; then
    fail "the driver measured ${drops} dropped packets in a recording that was meant to be untouched."
fi

recording_size "${volume}" "${session}.ts"
final_size="${number_value}"

if [ "${final_size}" -ne "${final_bytes}" ]; then
    fail "the file is ${final_size} bytes but the driver counted ${final_bytes} bytes into it; the tail was lost."
fi
pass "the file is exactly the ${final_bytes} bytes the driver counted into it, so nothing was truncated"

check_continuity "${volume}" "${session}.ts"
pass "the recording is byte-continuous across the app replacement"

replace_finished="$(date +%s)"
note "residual risk: the file's own counters repeat every 48,128 bytes, so a loss of exactly N x 48,128 bytes"
note "upstream of the driver's byte count would be invisible to both checks above. Closing that needs a"
note "wider counter in FakeTunerDevice; see docker/acceptance/README.md."
note "elapsed around the replacement: $((replace_finished - replace_started))s"
