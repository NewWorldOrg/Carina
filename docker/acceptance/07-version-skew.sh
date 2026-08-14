#!/usr/bin/env bash
set -euo pipefail

scenario_id="07-version-skew"
. "$(cd "$(dirname "$0")" && pwd)/lib.sh"

old_commit="${CARINA_ACCEPTANCE_OLD_DRIVER_COMMIT:-fb89e7d}"
old_image="${prefix}-old"
build_image="${prefix}-old-build"
driver_container="${prefix}-driver"
app_container="${prefix}-app"
socket_volume="${prefix}-run"
recordings_volume="${prefix}-rec"
session="acc07"

heading "today's app against a driver built from an older commit"

require_image_present

if ! git -C "${repo_root}" cat-file -e "${old_commit}^{commit}" 2>/dev/null; then
    fail "commit ${old_commit} is not in this clone, so no old driver can be built. A shallow checkout cannot run this scenario; fetch the history (actions/checkout with fetch-depth: 0)."
fi

described="$(git -C "${repo_root}" log -1 --format='%h %ad %s' --date=short "${old_commit}")"
note "the old driver comes from ${described}"

mkdir -p "${workdir}/old"
git -C "${repo_root}" archive "${old_commit}" | tar -x -C "${workdir}/old"
cp "${repo_root}/Dockerfile" "${workdir}/old/Dockerfile"
note "the driver sources are the ones of ${old_commit}; the build recipe is today's, because the Dockerfile of that tree predates the multi-stage image"

if [ ! -d "${workdir}/old/src/Carina.Driver" ]; then
    fail "commit ${old_commit} has no driver to build."
fi

track_image "${build_image}"
if ! docker build --target driver-build -t "${build_image}" "${workdir}/old" > "${workdir}/build.log" 2>&1; then
    tail -30 "${workdir}/build.log"
    fail "the driver of ${old_commit} does not build."
fi
pass "the driver of ${old_commit} builds"

extractor="$(docker create "${build_image}")"
docker cp "${extractor}:/out/driver" "${workdir}/driver" >/dev/null
docker rm "${extractor}" >/dev/null

printf 'FROM %s\nCOPY driver /opt/carina/driver\n' "${image}" > "${workdir}/Dockerfile"
track_image "${old_image}"
if ! docker build -t "${old_image}" "${workdir}" > "${workdir}/assemble.log" 2>&1; then
    tail -20 "${workdir}/assemble.log"
    fail "the old driver could not be put into today's runtime image."
fi

old_sum="$(docker run --rm --entrypoint sha256sum "${old_image}" /opt/carina/driver/Carina.Driver | awk '{ print $1 }')"
new_sum="$(docker run --rm --entrypoint sha256sum "${image}" /opt/carina/driver/Carina.Driver | awk '{ print $1 }')"

if [ "${old_sum}" = "${new_sum}" ]; then
    fail "the old and current driver binaries are the same file; nothing old is under test."
fi
pass "the driver binary under test is not today's: ${old_sum:0:12} against ${new_sum:0:12}"

docker volume create "${socket_volume}" >/dev/null
track_volume "${socket_volume}"
docker volume create "${recordings_volume}" >/dev/null
track_volume "${recordings_volume}"

docker run -d --name "${driver_container}" \
    -e CARINA_ROLE=driver \
    -e CARINA_DRIVER_CONFIG=/etc/carina/driver.json \
    -v "${driver_config_file}:/etc/carina/driver.json:ro" \
    -v "${socket_volume}:/run/carina" \
    -v "${recordings_volume}:/srv/recordings" \
    "${old_image}" >/dev/null
track_container "${driver_container}"
diagnostics_container="${driver_container}"

if ! wait_until 60 bash -c "docker run --rm --user 100:10001 -v ${socket_volume}:/run/carina ${curl_image} -sf --max-time 5 --unix-socket /run/carina/driver.sock http://localhost/health"; then
    docker logs "${driver_container}" 2>&1 | tail -20
    fail "the old driver did not come up."
fi

driver_get /health
old_hello="${reply}"
note "the old driver says hello with ${old_hello}"

json "${old_hello}" has-key draining
if [ "${text_value}" != "false" ]; then
    fail "the old driver already carries the draining flag, so it is not older than the contract change under test."
fi
pass "the old driver's hello has no draining flag; today's contract has one"

driver_status_code /diagnostics
if [ "${text_value}" != "404" ]; then
    fail "the old driver answered ${text_value} on /diagnostics; it already has the endpoint that came later."
fi
pass "the old driver has no /diagnostics endpoint (404), which today's driver serves"

set +e
probe_answer="$(timeout 20 docker exec "${driver_container}" /opt/carina/driver/Carina.Driver --probe 2>&1)"
old_probe=$?
set -e

if [ "${old_probe}" -eq 0 ]; then
    fail "the old driver answered --probe with a healthy verdict; it already knows the flag that came later: ${probe_answer}"
fi

if grep -qi 'draining\|faulted\|healthy' <<<"${probe_answer}"; then
    fail "the old driver answered --probe with a health verdict (${probe_answer}); it is not as old as this scenario assumes."
fi
pass "the old driver does not know --probe: exit ${old_probe}, and what it says is about its own start-up, not about health"
echo "${probe_answer}" | sed 's/^/     /'
note "docker/verify-image.sh holds the other side of this: today's driver answers --probe with 0 while healthy."
note "so compose.deploy.yml's healthcheck cannot be pointed at this driver, which is why this scenario runs it by hand."

start_recording "${session}" fake-terrestrial primary 240
note "a recording is already running on the old driver before today's app is started"

docker run -d --name "${app_container}" \
    --user 10001:10001 \
    -e CARINA_ROLE=app \
    -e CARINA_DRIVER_SOCKET=/run/carina/driver.sock \
    -e 'ConnectionStrings__Carina=Host=nowhere;Port=5432;Database=carina;Username=carina;Password=acceptance' \
    -v "${socket_volume}:/run/carina" \
    -p 127.0.0.1:0:8080 \
    "${image}" >/dev/null
track_container "${app_container}"

published="$(docker port "${app_container}" 8080 | head -1)"
port="${published##*:}"
require_number "the app's published port" "${port}"

if ! wait_until 60 bash -c "curl -sf --max-time 5 http://127.0.0.1:${port}/api/health"; then
    docker logs "${app_container}" 2>&1 | tail -20
    fail "today's app did not come up next to the old driver."
fi
pass "today's app serves /api/health next to a driver from ${old_commit}"

for path in /health /sessions /events; do
    if ! wait_until 60 bash -c "docker logs ${driver_container} 2>&1 | grep -q 'Request starting HTTP/1.1 GET http://driver${path}'"; then
        docker logs "${driver_container}" 2>&1 | tail -20
        fail "today's app never asked the old driver for ${path}, so the two never spoke."
    fi
done
pass "today's app reached the old driver on /health, /sessions and /events"

refusals="$(docker logs "${driver_container}" 2>&1 | grep -c 'Request finished HTTP/1.1 GET http://driver/[a-z]* - [45]' || true)"
require_number "the number of failed requests the old driver served the app" "${refusals}"

if [ "${refusals}" -ne 0 ]; then
    docker logs "${driver_container}" 2>&1 | grep 'Request finished HTTP/1.1 GET http://driver' | tail -10
    fail "the old driver answered today's app with ${refusals} failures."
fi
pass "the old driver answered every one of today's app's requests without a refusal"

driver_get /sessions
json "${reply}" session "${session}" state
if [ "${text_value}" != "active" ]; then
    fail "the recording the old driver was holding is '${text_value}'; the arrival of today's app disturbed it."
fi

json "${reply}" session "${session}" bytesRecorded
recorded="${text_value}"
require_number "the old driver's byte count" "${recorded}"
pass "the recording the old driver held through the app's arrival is still active at ${recorded} bytes"

stop_recording "${session}"

if ! wait_until 60 bash -c "docker run --rm --user 100:10001 -v ${socket_volume}:/run/carina ${curl_image} -s --max-time 15 --unix-socket /run/carina/driver.sock http://localhost/sessions | python3 ${acceptance_dir}/read-json.py session ${session} concluded | grep -qx true"; then
    fail "the old driver's recording never concluded."
fi

driver_get /sessions
json "${reply}" session "${session}" bytesRecorded
final_bytes="${text_value}"
require_number "the old driver's final byte count" "${final_bytes}"

recording_size "${recordings_volume}" "${session}.ts"
if [ "${number_value}" -ne "${final_bytes}" ]; then
    fail "the old driver's file is ${number_value} bytes but it counted ${final_bytes}."
fi

check_continuity "${recordings_volume}" "${session}.ts"
pass "the old driver's recording is byte-continuous and exactly ${final_bytes} bytes"

sleep 15

inspect_field "${app_container}" '{{.RestartCount}}'
if [ "${text_value}" != "0" ]; then
    fail "today's app restarted ${text_value} times against the old driver."
fi

inspect_field "${app_container}" '{{.State.Running}}'
if [ "${text_value}" != "true" ]; then
    docker logs "${app_container}" 2>&1 | tail -20
    fail "today's app is not running any more."
fi

if ! curl -sf --max-time 5 "http://127.0.0.1:${port}/api/health" >/dev/null; then
    fail "today's app stopped answering while attached to the old driver."
fi
pass "today's app stayed up, unrestarted, for the whole run"

errors="$(docker logs "${app_container}" 2>&1 | grep -c '^\(fail\|crit\)' || true)"
require_number "the number of errors today's app logged" "${errors}"

if [ "${errors}" -ne 0 ]; then
    docker logs "${app_container}" 2>&1 | grep -A 3 '^\(fail\|crit\)' | head -20
    fail "today's app logged ${errors} errors against the old driver."
fi
pass "today's app logged no error against the old driver"

note "what this does not cover: no driver that ever existed advertises fewer capabilities or a lower protocol"
note "version than today's app expects, so a real old image cannot produce the degraded case. That half of the"
note "criterion is held by DriverVersionSkewTests in tests/Carina.Api.Tests/FeatureTest against a driver double."
note "the app's own 'driver update required' surface is GET /api/driver/status, which answers 401 until the"
note "authentication domain registers a scheme, so it cannot be read from outside a test host."
