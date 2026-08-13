#!/usr/bin/env bash
set -euo pipefail

image="${1:?usage: verify-image.sh <image>}"

prefix="carina-verify-$$"
run_volume="${prefix}-run"
rec_volume="${prefix}-rec"
workdir="$(mktemp -d)"
connection="Host=localhost;Database=carina;Username=verify;Password=verify"

cleanup() {
    docker rm -f "${prefix}-driver" "${prefix}-app" "${prefix}-web" "${prefix}-all" >/dev/null 2>&1 || true
    docker volume rm "${run_volume}" "${rec_volume}" >/dev/null 2>&1 || true
    rm -rf "${workdir}"
}
trap cleanup EXIT

pass() { echo "PASS: $*"; }
fail() { echo "FAIL: $*" >&2; exit 1; }

cat > "${workdir}/driver.json" <<'JSON'
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
echo '{ "unexpected": true }' > "${workdir}/broken.json"
chmod 0644 "${workdir}"/*.json

driver_curl() {
    docker run --rm --user 100:10001 -v "${run_volume}:/run/carina" curlimages/curl \
        -sf --max-time 5 --unix-socket /run/carina/driver.sock "$@"
}

stranger_curl() {
    docker run --rm --user 100:100 -v "${run_volume}:/run/carina" curlimages/curl \
        -sf --max-time 5 --unix-socket /run/carina/driver.sock "$@"
}

wait_for() {
    local tries
    for tries in $(seq 1 75); do
        if "$@" >/dev/null 2>&1; then
            return 0
        fi
        sleep 0.2
    done
    return 1
}

process_uid() {
    docker exec "$1" awk '/^Uid:/ { print $2 }' /proc/1/status
}

docker run -d --name "${prefix}-driver" \
    -e CARINA_ROLE=driver \
    -e CARINA_DRIVER_CONFIG=/etc/carina/driver.json \
    -v "${workdir}/driver.json:/etc/carina/driver.json:ro" \
    -v "${run_volume}:/run/carina" \
    -v "${rec_volume}:/srv/recordings" \
    "${image}" >/dev/null

wait_for driver_curl http://localhost/health || fail "the driver did not serve /health on its socket"
echo "driver /health: $(driver_curl http://localhost/health)"
pass "driver role serves /health over the Unix socket"

perms="$(docker exec "${prefix}-driver" stat -c '%a %U %G' /run/carina/driver.sock)"
[ "${perms}" = "660 root carina" ] || fail "the driver socket is '${perms}', expected '660 root carina'"
pass "driver socket is 0660 root:carina"

if stranger_curl http://localhost/health >/dev/null 2>&1; then
    fail "a user outside the carina group could connect to the driver socket"
fi
pass "a user outside the carina group is denied by the socket permissions"

[ "$(process_uid "${prefix}-driver")" = "0" ] || fail "the driver does not run as root"
echo "driver binary: $(docker exec "${prefix}-driver" ls -l /opt/carina/driver/Carina.Driver)"
pass "driver role runs the native binary as root"

docker stop -t 30 "${prefix}-driver" >/dev/null
rc="$(docker inspect -f '{{.State.ExitCode}}' "${prefix}-driver")"
[ "${rc}" = "0" ] || fail "the driver exited ${rc} on SIGTERM, expected 0"
pass "driver role exits 0 on SIGTERM"
docker rm -f "${prefix}-driver" >/dev/null

set +e
output="$(docker run --rm \
    -e CARINA_ROLE=driver \
    -e CARINA_DRIVER_CONFIG=/etc/carina/driver.json \
    -v "${workdir}/broken.json:/etc/carina/driver.json:ro" \
    "${image}" 2>&1)"
rc=$?
set -e
[ "${rc}" = "78" ] || fail "the driver exited ${rc} on a broken configuration, expected 78"
echo "${output}"
pass "driver role refuses a broken configuration with exit 78 and a diagnosis"

docker run -d --name "${prefix}-app" \
    -e CARINA_ROLE=app \
    -e "ConnectionStrings__Carina=${connection}" \
    -p 127.0.0.1:0:8080 \
    "${image}" >/dev/null
port="$(docker port "${prefix}-app" 8080 | head -1 | awk -F: '{ print $NF }')"

wait_for curl -sf "http://127.0.0.1:${port}/api/health" || fail "the app did not serve /api/health"
echo "app /api/health: $(curl -sf "http://127.0.0.1:${port}/api/health")"
pass "app role serves /api/health with 200"

[ "$(process_uid "${prefix}-app")" = "10001" ] || fail "the app runs as uid $(process_uid "${prefix}-app"), expected 10001"
pass "app role runs as the unprivileged carina user (uid 10001)"

code="$(curl -s -o /dev/null -w '%{http_code}' "http://127.0.0.1:${port}/api/driver/status")"
[ "${code}" = "401" ] || fail "/api/driver/status answered ${code} without credentials, expected 401"
pass "app role denies an unauthenticated business endpoint with 401"
docker rm -f "${prefix}-app" >/dev/null

set +e
output="$(docker run --rm -e CARINA_ROLE=migrate "${image}" 2>&1)"
rc=$?
set -e
[ "${rc}" = "78" ] || fail "migrate without CARINA_DB_CONNECTION exited ${rc}, expected 78"
echo "${output}" | grep -q "CARINA_DB_CONNECTION" || fail "the migrate failure does not name CARINA_DB_CONNECTION"
pass "migrate role fails without CARINA_DB_CONNECTION (exit ${rc}) and names the variable"

docker run -d --name "${prefix}-web" -e CARINA_ROLE=web "${image}" >/dev/null
sleep 1
[ "$(docker inspect -f '{{.State.Running}}' "${prefix}-web")" = "true" ] || fail "the web stub did not stay up"
docker logs "${prefix}-web" 2>&1 | grep -q "no asset" || fail "the web stub did not explain itself"
pass "web role stays up as a stub and says why"
docker rm -f "${prefix}-web" >/dev/null

set +e
docker run --rm "${image}" nonsense >/dev/null 2>&1
rc=$?
set -e
[ "${rc}" = "64" ] || fail "an unknown role exited ${rc}, expected 64"
pass "an unknown role is refused with exit 64"

docker run -d --name "${prefix}-all" \
    -e CARINA_ROLE=all \
    -e CARINA_DRIVER_CONFIG=/etc/carina/driver.json \
    -e "ConnectionStrings__Carina=${connection}" \
    -v "${workdir}/driver.json:/etc/carina/driver.json:ro" \
    -v "${run_volume}:/run/carina" \
    -v "${rec_volume}:/srv/recordings" \
    -p 127.0.0.1:0:8080 \
    "${image}" >/dev/null
port="$(docker port "${prefix}-all" 8080 | head -1 | awk -F: '{ print $NF }')"

wait_for driver_curl http://localhost/health || fail "the all role did not bring up the driver"
wait_for curl -sf "http://127.0.0.1:${port}/api/health" || fail "the all role did not bring up the app"
pass "all role runs both children"

docker exec "${prefix}-all" bash -c '( sleep 1 & )'
sleep 3
zombies="$(docker exec "${prefix}-all" bash -c 'grep -l "^State:.*zombie" /proc/[0-9]*/status' 2>/dev/null || true)"
[ -z "${zombies}" ] || fail "zombie processes remain: ${zombies}"
pass "all role reaps an orphaned child; no zombie remains"

driver_pid="$(docker exec "${prefix}-all" bash -c 'for f in /proc/[0-9]*/comm; do read -r c < "$f"; if [ "$c" = "Carina.Driver" ]; then basename "$(dirname "$f")"; fi; done')"
[ -n "${driver_pid}" ] || fail "no Carina.Driver process was found in the all role"
docker exec "${prefix}-all" kill -KILL "${driver_pid}"
rc="$(timeout 60 docker wait "${prefix}-all")"
[ "${rc}" = "137" ] || fail "the all role exited ${rc} after its driver child was killed, expected 137"
pass "all role: killing the driver child stops the container with the child's status (137)"
docker rm -f "${prefix}-all" >/dev/null

echo "image size: $(docker image inspect -f '{{.Size}}' "${image}" | awk '{ printf "%.0f MB\n", $1 / 1000000 }')"
echo "every role of ${image} behaves as required."
