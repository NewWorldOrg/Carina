#!/usr/bin/env bash
set -euo pipefail

scenario_id="05-stale-socket"
. "$(cd "$(dirname "$0")" && pwd)/lib.sh"

heading "受入基準 5 — a killed driver leaves a stale socket and the app survives it"

stack_up

container_of driver
driver_container="${text_value}"
container_of app
app_container="${text_value}"

inspect_field "${app_container}" '{{.RestartCount}}'
app_restarts_before="${text_value}"
require_number "the app's restart count" "${app_restarts_before}"

inspect_field "${driver_container}" '{{.State.StartedAt}}'
driver_started_before="${text_value}"

driver_get /health
json "${reply}" field instanceId
instance_before="${text_value}"

socket_inode() {
    local status

    set +e
    text_value="$(docker run --rm -v "${socket_volume}:/run/carina" "${python_image}" \
        stat -c '%i %F' /run/carina/driver.sock 2>&1)"
    status=$?
    set -e

    if [ "${status}" -ne 0 ]; then
        fail "the driver socket could not be examined: ${text_value}"
    fi
}

socket_inode
inode_before="${text_value}"
note "before the kill: driver instance ${instance_before}, socket ${inode_before}"

app_requests "${driver_container}" /health
health_before="${count_value}"
app_requests "${driver_container}" /sessions
sessions_before="${count_value}"
app_requests "${driver_container}" /events
events_before="${count_value}"

docker kill --signal=KILL "${driver_container}" >/dev/null
note "SIGKILL sent to the driver"

if ! wait_until 30 bash -c "test \"\$(docker inspect -f '{{.State.Running}}' ${driver_container})\" = false"; then
    fail "the driver survived SIGKILL, so nothing was left behind to recover from."
fi

inspect_field "${driver_container}" '{{.State.ExitCode}}'
if [ "${text_value}" != "137" ]; then
    fail "the killed driver exited ${text_value}, not 137; it was not killed outright."
fi

socket_inode
if [ "${text_value}" != "${inode_before}" ]; then
    fail "the socket is ${text_value} while the driver is dead; it should be the same file it could not unlink."
fi
pass "the dead driver left its socket behind: ${inode_before}, still there with no process on it"

connect_status="$(docker run --rm --user 100:10001 \
    -v "${socket_volume}:/run/carina" \
    -v "${acceptance_dir}/connect.py:/connect.py:ro" \
    "${python_image}" python /connect.py /run/carina/driver.sock)"
if [ "${connect_status}" != "ECONNREFUSED" ]; then
    fail "connecting to the abandoned socket answered '${connect_status}'; a stale socket refuses connections, so this is not the state under test."
fi
pass "the abandoned socket is stale: connecting to it is refused (${connect_status})"

if ! stack up -d --no-deps --wait driver >/dev/null 2>&1; then
    stack logs --tail 30 driver
    fail "the driver would not start again over its own stale socket."
fi

container_of driver
if [ "${text_value}" != "${driver_container}" ]; then
    note "the runtime replaced the driver container ${driver_container:0:12} with ${text_value:0:12}, so its request log starts again"
    driver_container="${text_value}"
    health_before=0
    sessions_before=0
    events_before=0
fi

if ! wait_until 60 bash -c "test \"\$(docker inspect -f '{{.State.Running}}' ${driver_container})\" = true"; then
    inspect_field "${driver_container}" '{{.State.Status}}'
    fail "the driver never came back; it is ${text_value}."
fi

socket_inode
note "after the restart the socket is ${text_value} (it was ${inode_before}; the filesystem may reuse a number, so this is a note and not the proof)"

if ! wait_until 60 bash -c "docker run --rm --user 100:10001 -v ${socket_volume}:/run/carina ${curl_image} -sf --max-time 5 --unix-socket /run/carina/driver.sock http://localhost/health"; then
    docker logs "${driver_container}" 2>&1 | tail -20
    fail "the restarted driver never answered on the path its predecessor left a socket file at; binding over a stale socket is what this criterion is about."
fi
pass "the restarted driver serves on the path the dead one left a socket file at, which it could only do by unlinking it first"

perms="$(docker exec "${driver_container}" stat -c '%a %U %G' /run/carina/driver.sock)"
if [ "${perms}" != "660 root carina" ]; then
    fail "the replaced socket is '${perms}', expected '660 root carina'."
fi
pass "the replaced socket is 0660 root:carina, as the first one was"

driver_get /health
json "${reply}" field instanceId
instance_after="${text_value}"

if [ "${instance_after}" = "${instance_before}" ]; then
    fail "the driver reports the same instance id ${instance_before}; this is not a new process, so nothing recovered."
fi
pass "the driver that came back is a new process: instance ${instance_before} -> ${instance_after}"

inspect_field "${app_container}" '{{.RestartCount}}'
app_restarts_after="${text_value}"

container_of app
if [ "${text_value}" != "${app_container}" ]; then
    fail "the app container was replaced (${app_container:0:12} -> ${text_value:0:12}); it did not ride out the driver's death."
fi

if [ "${app_restarts_after}" != "${app_restarts_before}" ]; then
    fail "the app restarted ${app_restarts_before} -> ${app_restarts_after} times while the driver was away; that is the crash loop this criterion forbids."
fi
pass "the app neither restarted nor was replaced while the driver was gone (restart count ${app_restarts_after})"

if ! wait_until 90 bash -c "docker logs ${driver_container} 2>&1 | grep -c 'Request starting HTTP/1.1 GET http://driver/events' | awk -v seen=${events_before} '{ exit \$1 > seen ? 0 : 1 }'"; then
    fail "the app never resubscribed to the new driver's event feed, so it did not reconnect on its own."
fi

app_requests "${driver_container}" /health
health_after="${count_value}"
app_requests "${driver_container}" /sessions
sessions_after="${count_value}"
app_requests "${driver_container}" /events
events_after="${count_value}"

if [ "${sessions_after}" -le "${sessions_before}" ]; then
    fail "the app never asked the new driver for its session list (${sessions_before} -> ${sessions_after})."
fi
pass "the app reconnected by itself: health ${health_before} -> ${health_after}, sessions ${sessions_before} -> ${sessions_after}, events ${events_before} -> ${events_after}"

