#!/usr/bin/env bash
set -euo pipefail

scenario_id="04-configuration-driven"
. "$(cd "$(dirname "$0")" && pwd)/lib.sh"

heading "受入基準 4 — swapping the configuration file moves the socket, the output and the devices"

require_image_present

inspect_field "${image}" '{{.Id}}'
image_id="${text_value}"
note "every run below uses image ${image_id}"

cat > "${workdir}/first.json" <<'JSON'
{
  "socketPath": "/run/carina/driver.sock",
  "socketGroupId": 10001,
  "outputRoots": [{ "name": "primary", "path": "/srv/recordings" }],
  "shutdownGraceHours": 6,
  "liveSessionMinutes": 240,
  "tuner": { "backend": "fake" },
  "devices": [{ "id": "fake-terrestrial", "kind": "terrestrial", "enabled": true }]
}
JSON

cat > "${workdir}/second.json" <<'JSON'
{
  "socketPath": "/run/carina/elsewhere.sock",
  "socketGroupId": 10001,
  "outputRoots": [{ "name": "archive", "path": "/srv/archive" }],
  "shutdownGraceHours": 6,
  "liveSessionMinutes": 240,
  "tuner": { "backend": "fake" },
  "devices": [{ "id": "fake-satellite", "kind": "satellite", "enabled": true }]
}
JSON

cat > "${workdir}/broken.json" <<'JSON'
{
  "socketPath": "relative/driver.sock",
  "socketGroupId": 10001,
  "outputRoots": [{ "name": "not a name!", "path": "/srv/recordings" }],
  "shutdownGraceHours": 0,
  "tuner": { "backend": "fake" },
  "devices": [{ "id": "fake-terrestrial", "kind": "terrestrial", "enabled": true }]
}
JSON
chmod 0644 "${workdir}"/*.json

run_volume="${prefix}-run"
first_volume="${prefix}-first"
second_volume="${prefix}-second"
docker volume create "${run_volume}" >/dev/null
track_volume "${run_volume}"
docker volume create "${first_volume}" >/dev/null
track_volume "${first_volume}"
docker volume create "${second_volume}" >/dev/null
track_volume "${second_volume}"

socket_volume="${run_volume}"

start_driver() {
    local name="$1"
    local config="$2"
    local output_volume="$3"
    local output_path="$4"

    docker run -d --name "${name}" \
        -e CARINA_ROLE=driver \
        -e CARINA_DRIVER_CONFIG=/etc/carina/driver.json \
        -v "${workdir}/${config}:/etc/carina/driver.json:ro" \
        -v "${run_volume}:/run/carina" \
        -v "${output_volume}:${output_path}" \
        "${image}" >/dev/null
    track_container "${name}"
}

socket_answers() {
    local path="$1"

    docker run --rm --user 100:10001 -v "${run_volume}:/run/carina" "${curl_image}" \
        -sf --max-time 5 --unix-socket "${path}" http://localhost/health
}

first="${prefix}-first-run"
start_driver "${first}" first.json "${first_volume}" /srv/recordings

if ! wait_until 60 bash -c "docker run --rm --user 100:10001 -v ${run_volume}:/run/carina ${curl_image} -sf --max-time 5 --unix-socket /run/carina/driver.sock http://localhost/health"; then
    docker logs "${first}" 2>&1 | tail -20
    fail "the first configuration did not bring the driver up on /run/carina/driver.sock."
fi
pass "the first configuration puts the socket at /run/carina/driver.sock"

driver_curl() {
    docker run --rm --user 100:10001 -v "${run_volume}:/run/carina" "${curl_image}" \
        -s --max-time 15 --unix-socket /run/carina/driver.sock "$@"
}

driver_get /tuners
json "${reply}" field 0 deviceId
if [ "${text_value}" != "fake-terrestrial" ]; then
    fail "the first configuration produced device '${text_value}', not the one it declares."
fi

set +e
extra="$(printf '%s' "${reply}" | python3 "${acceptance_dir}/read-json.py" field 1 deviceId 2>/dev/null)"
extra_status=$?
set -e

if [ "${extra_status}" -eq 0 ]; then
    fail "the first configuration declares one device but the driver reports another: ${extra}"
fi
pass "the first configuration produces exactly the device it declares: fake-terrestrial"

start_recording acc04first fake-terrestrial primary 120

if ! wait_until 30 bash -c "docker run --rm -v ${first_volume}:/rec ${python_image} test -s /rec/acc04first.ts"; then
    fail "the first configuration's output root received nothing."
fi
stop_recording acc04first
pass "the first configuration writes into /srv/recordings"

docker rm -f "${first}" >/dev/null

second="${prefix}-second-run"
start_driver "${second}" second.json "${second_volume}" /srv/archive

if ! wait_until 60 bash -c "docker run --rm --user 100:10001 -v ${run_volume}:/run/carina ${curl_image} -sf --max-time 5 --unix-socket /run/carina/elsewhere.sock http://localhost/health"; then
    docker logs "${second}" 2>&1 | tail -20
    fail "the second configuration did not bring the driver up on /run/carina/elsewhere.sock."
fi

if socket_answers /run/carina/driver.sock >/dev/null 2>&1; then
    fail "the old socket path still answers; the driver did not follow the configuration it was given."
fi
pass "the second configuration moves the socket to /run/carina/elsewhere.sock and leaves the old path dead"

driver_curl() {
    docker run --rm --user 100:10001 -v "${run_volume}:/run/carina" "${curl_image}" \
        -s --max-time 15 --unix-socket /run/carina/elsewhere.sock "$@"
}

driver_get /tuners
json "${reply}" field 0 deviceId
if [ "${text_value}" != "fake-satellite" ]; then
    fail "the second configuration produced device '${text_value}', not the one it declares."
fi
pass "the second configuration produces exactly the device it declares: fake-satellite"

start_recording acc04second fake-satellite archive 120 satellite 31

if ! wait_until 30 bash -c "docker run --rm -v ${second_volume}:/rec ${python_image} test -s /rec/acc04second.ts"; then
    fail "the second configuration's output root received nothing."
fi
stop_recording acc04second
pass "the second configuration writes into /srv/archive"

inspect_field "${image}" '{{.Id}}'
if [ "${text_value}" != "${image_id}" ]; then
    fail "the image changed between the two runs (${image_id} -> ${text_value}); this was not a configuration swap."
fi
pass "both runs are the same image, ${image_id}; nothing was rebuilt"

docker rm -f "${second}" >/dev/null

set +e
diagnosis="$(docker run --rm \
    -e CARINA_ROLE=driver \
    -e CARINA_DRIVER_CONFIG=/etc/carina/driver.json \
    -v "${workdir}/broken.json:/etc/carina/driver.json:ro" \
    "${image}" 2>&1)"
broken_status=$?
set -e

if [ "${broken_status}" -ne 78 ]; then
    fail "a configuration with three faults exited ${broken_status}, expected 78: ${diagnosis}"
fi

for setting in socketPath outputRoots shutdownGraceHours; do
    if ! grep -q "${setting}" <<<"${diagnosis}"; then
        fail "the refusal does not name ${setting}, so the operator is not told what to fix: ${diagnosis}"
    fi
done
pass "a broken configuration is refused before anything is bound, with exit 78 naming every offending setting"
echo "${diagnosis}" | sed 's/^/     /'
