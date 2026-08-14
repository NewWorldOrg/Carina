#!/usr/bin/env bash
set -euo pipefail

scenario_id="06-socket-permissions"
. "$(cd "$(dirname "$0")" && pwd)/lib.sh"

heading "the socket is 0660 and the group is the whole access control"

stack_up

container_of driver
driver_container="${text_value}"
container_of app
app_container="${text_value}"

perms="$(docker exec "${driver_container}" stat -c '%a %U %G' /run/carina/driver.sock)"
if [ "${perms}" != "660 root carina" ]; then
    fail "the driver socket is '${perms}', expected '660 root carina'."
fi
pass "the driver socket is 0660 root:carina"

directory="$(docker exec "${driver_container}" stat -c '%a %U %G' /run/carina)"
note "the directory holding it is ${directory}"

connect_as() {
    local status

    set +e
    text_value="$(docker run --rm --user "$1" \
        -v "${socket_volume}:/run/carina" \
        -v "${acceptance_dir}/connect.py:/connect.py:ro" \
        "${python_image}" python /connect.py /run/carina/driver.sock 2>&1)"
    status=$?
    set -e

    if [ "${status}" -ne 0 ]; then
        fail "the connection attempt as $1 could not be made at all: ${text_value}"
    fi
}

connect_as 100:10001
if [ "${text_value}" != "connected" ]; then
    fail "a process in the carina group was refused with ${text_value}."
fi
pass "a process in the carina group connects"

set +e
member="$(docker run --rm --user 100:10001 -v "${socket_volume}:/run/carina" "${curl_image}" \
    -sf --max-time 10 --unix-socket /run/carina/driver.sock http://localhost/health 2>&1)"
member_status=$?
set -e

if [ "${member_status}" -ne 0 ]; then
    fail "a process in the carina group could not be served (curl ${member_status}): ${member}"
fi
pass "an unprivileged process in the carina group is served: ${member}"

connect_as 100:100
if [ "${text_value}" = "connected" ]; then
    fail "a process outside the carina group connected to the socket."
fi

if [ "${text_value}" != "EACCES" ]; then
    fail "a process outside the carina group failed with ${text_value}, not the permission denial under test; the socket may simply have been missing."
fi
pass "a process outside the carina group is refused with EACCES, which is the permissions and nothing else"

app_user="$(docker exec "${app_container}" id -u)"
if [ "${app_user}" != "10001" ]; then
    fail "the app runs as uid ${app_user}; the half of this criterion about the real unprivileged client proves nothing."
fi

app_groups="$(docker exec "${app_container}" id -G)"
note "the app runs as uid ${app_user}, groups ${app_groups}"

app_requests "${driver_container}" /health
if [ "${count_value}" -lt 1 ]; then
    fail "the real app has never been served over this socket, so only synthetic clients were tested."
fi
pass "the real unprivileged app is being served over the same socket (${count_value} health calls so far)"
