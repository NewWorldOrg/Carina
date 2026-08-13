#!/usr/bin/env bash

acceptance_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${acceptance_dir}/../.." && pwd)"

image="${CARINA_ACCEPTANCE_IMAGE:-carina}"
curl_image="${CARINA_ACCEPTANCE_CURL_IMAGE:-curlimages/curl}"
python_image="${CARINA_ACCEPTANCE_PYTHON_IMAGE:-python:3.13-alpine}"
deploy_file="${repo_root}/compose.deploy.yml"
driver_config_file="${CARINA_ACCEPTANCE_DRIVER_CONFIG:-${repo_root}/docker/driver.development.json}"
postgres_password="acceptance-throwaway-not-a-deployment"
api_port="${CARINA_ACCEPTANCE_API_PORT:-0}"
compose_override="${CARINA_ACCEPTANCE_COMPOSE_OVERRIDE:-}"

prefix="carina-acc-${scenario_id}-$$"
project="${prefix}"
socket_volume="${prefix}_driver-run"
workdir="$(mktemp -d)"
stop_grace=""
reply=""
http_status=""
curl_status=""
diagnostics_container=""
count_value=""
number_value=""
text_value=""
error_text=""

created_containers=()
created_volumes=()
created_images=()
stack_started="no"

pass() { echo "PASS: $*"; }
note() { echo "     $*"; }
fail() {
    echo "FAIL: $*" >&2
    exit 1
}

cleanup() {
    local status=$?

    if [ "${stack_started}" = "yes" ]; then
        stack kill >/dev/null 2>&1 || true
        stack down -v --remove-orphans --timeout 5 >/dev/null 2>&1 || true
    fi

    if [ "${#created_containers[@]}" -gt 0 ]; then
        docker rm -f "${created_containers[@]}" >/dev/null 2>&1 || true
    fi

    if [ "${#created_volumes[@]}" -gt 0 ]; then
        docker volume rm "${created_volumes[@]}" >/dev/null 2>&1 || true
    fi

    if [ "${#created_images[@]}" -gt 0 ]; then
        docker image rm -f "${created_images[@]}" >/dev/null 2>&1 || true
    fi

    rm -rf "${workdir}"

    exit "${status}"
}

trap cleanup EXIT

track_container() { created_containers+=("$1"); }
track_volume() { created_volumes+=("$1"); }
track_image() { created_images+=("$1"); }

capture() {
    local status

    set +e
    text_value="$("$@" 2>"${workdir}/stderr")"
    status=$?
    set -e

    error_text="$(cat "${workdir}/stderr" 2>/dev/null)"

    return "${status}"
}

fetch_tools() {
    local tool

    for tool in "${curl_image}" "${python_image}"; do
        if docker image inspect "${tool}" >/dev/null 2>&1; then
            continue
        fi

        note "fetching ${tool} before anything is measured, so that no pull lands in an answer"

        if ! docker pull "${tool}"; then
            fail "the harness needs ${tool} and it could not be fetched."
        fi
    done
}

is_number() {
    case "${1}" in
        '' | *[!0-9]*) return 1 ;;
        *) return 0 ;;
    esac
}

require_number() {
    if ! is_number "${2}"; then
        fail "${1} came out as '${2}', which is not a number; nothing was compared."
    fi
}

json() {
    local document="$1"
    local status
    local complaint

    shift

    if [ -z "${document}" ]; then
        show_driver_log
        fail "there is no answer to read ${*} out of: the body was empty."
    fi

    set +e
    text_value="$(printf '%s' "${document}" | python3 "${acceptance_dir}/read-json.py" "$@" 2>&1)"
    status=$?
    set -e

    if [ "${status}" -ne 0 ]; then
        complaint="$(printf '%s' "${text_value}" | tail -n 1)"
        show_driver_log
        fail "reading ${*} out of the answer failed (${complaint}). The answer was: ${document}"
    fi
}

show_driver_log() {
    if [ -z "${diagnostics_container}" ]; then
        return 0
    fi

    echo "     the driver's last words:" >&2
    docker logs --tail 40 "${diagnostics_container}" 2>&1 | sed 's/^/     /' >&2 || true
}

body_or_nothing() {
    if [ -z "${reply}" ]; then
        printf '(no body at all)'

        return 0
    fi

    printf '%s' "${reply}"
}

require_image_present() {
    if ! docker image inspect "${image}" >/dev/null 2>&1; then
        fail "the image '${image}' is not built; run 'task image' or set CARINA_ACCEPTANCE_IMAGE."
    fi
}

derive_stop_grace() {
    local status

    set +e
    stop_grace="$(CARINA_IMAGE="${image}" CARINA_DRIVER_CONFIG_FILE="${driver_config_file}" \
        "${repo_root}/docker/grace-period.sh" derive 2>&1)"
    status=$?
    set -e

    if [ "${status}" -ne 0 ]; then
        fail "the driver would not report its shutdown budget, so no grace period could be derived: ${stop_grace}"
    fi

    if ! is_number "${stop_grace%s}"; then
        fail "grace-period.sh derive printed '${stop_grace}', which is not a number of seconds."
    fi
}

stack() {
    local extra=()

    if [ -n "${compose_override}" ]; then
        extra=(-f "${compose_override}")
    fi

    CARINA_IMAGE="${image}" \
        CARINA_DRIVER_CONFIG_FILE="${driver_config_file}" \
        POSTGRES_PASSWORD="${postgres_password}" \
        CARINA_API_PORT="${api_port}" \
        CARINA_STOP_GRACE="${stop_grace}" \
        docker compose -p "${project}" -f "${deploy_file}" "${extra[@]}" "$@"
}

stack_up() {
    require_image_present
    derive_stop_grace

    socket_volume="${project}_driver-run"

    stack_started="yes"

    if ! stack up -d --wait >/dev/null 2>&1; then
        stack ps
        stack logs --tail 40
        fail "the deployment stack did not come up healthy."
    fi

    container_of driver
    diagnostics_container="${text_value}"

    note "deployment stack up as project ${project}, stop_grace_period ${stop_grace}"
}

container_of() {
    local service="$1"

    if ! capture stack ps -q "${service}"; then
        fail "the ${service} container could not be found: ${error_text}"
    fi

    if [ -z "${text_value}" ]; then
        fail "the ${service} container could not be found; compose named none. ${error_text}"
    fi
}

inspect_field() {
    local container="$1"
    local template="$2"

    if ! capture docker inspect -f "${template}" "${container}"; then
        fail "docker inspect ${template} on ${container} failed: ${error_text}"
    fi
}

driver_curl() {
    docker run --rm --user 100:10001 -v "${socket_volume}:/run/carina" "${curl_image}" \
        -s --max-time 15 --unix-socket /run/carina/driver.sock "$@"
}

driver_request() {
    local answer

    if ! capture driver_curl -w '\n%{http_code}' "$@"; then
        curl_status=$?
        reply="${text_value}"
        http_status=""

        return 1
    fi

    answer="${text_value}"
    curl_status=0
    http_status="$(printf '%s' "${answer}" | tail -n 1)"
    reply="$(printf '%s' "${answer}" | sed '$d')"

    if ! is_number "${http_status}"; then
        reply="${answer}"
        http_status=""

        return 1
    fi

    return 0
}

driver_get() {
    if ! driver_request "http://localhost$1"; then
        show_driver_log
        fail "GET $1 over the driver socket did not complete (curl ${curl_status}): $(body_or_nothing)"
    fi

    if [ "${http_status}" != "200" ]; then
        show_driver_log
        fail "GET $1 answered ${http_status}, not 200. Body: $(body_or_nothing)"
    fi

    if [ -z "${reply}" ]; then
        show_driver_log
        fail "GET $1 answered ${http_status} with no body at all, so there is nothing to read."
    fi
}

driver_status_code() {
    if ! driver_request "http://localhost$1"; then
        show_driver_log
        fail "GET $1 over the driver socket did not complete (curl ${curl_status}): $(body_or_nothing)"
    fi

    text_value="${http_status}"
}

ends_at() {
    local status

    set +e
    text_value="$(date -u -d "+$1 seconds" +%Y-%m-%dT%H:%M:%SZ 2>&1)"
    status=$?
    set -e

    if [ "${status}" -ne 0 ]; then
        fail "this harness needs GNU date to name a recording's end time: ${text_value}"
    fi
}

start_recording() {
    local session="$1"
    local device="$2"
    local root="$3"
    local seconds="$4"
    local kind="${5:-terrestrial}"
    local channel="${6:-27}"

    ends_at "${seconds}"

    if ! driver_request -X POST -H 'Content-Type: application/json' \
        -d "{\"sessionId\":\"${session}\",\"purpose\":\"recording\",\"tuning\":{\"kind\":\"${kind}\",\"physicalChannel\":${channel}},\"deviceId\":\"${device}\",\"outputRoot\":\"${root}\",\"endsAt\":\"${text_value}\"}" \
        http://localhost/sessions; then
        show_driver_log
        fail "starting recording ${session} did not complete (curl ${curl_status}): $(body_or_nothing)"
    fi

    if [ "${http_status}" != "201" ]; then
        show_driver_log
        fail "starting recording ${session} answered ${http_status}, not 201. Body: $(body_or_nothing)"
    fi

    if [ -z "${reply}" ]; then
        show_driver_log
        fail "starting recording ${session} answered ${http_status} with no body at all; the driver accepted nothing and said nothing."
    fi

    json "${reply}" field sessionId

    if [ "${text_value}" != "${session}" ]; then
        fail "the driver answered the start of ${session} with '${text_value}': ${reply}"
    fi

    json "${reply}" field state

    if [ "${text_value}" != "active" ]; then
        fail "recording ${session} came back in state '${text_value}': ${reply}"
    fi
}

stop_recording() {
    if ! driver_request -X DELETE "http://localhost/sessions/$1"; then
        note "asking the driver to stop $1 did not complete (curl ${curl_status}): $(body_or_nothing)"

        return 0
    fi

    note "asking the driver to stop $1 answered ${http_status}"
}

recorded_bytes() {
    local session="$1"

    driver_get /sessions
    json "${reply}" session "${session}" bytesRecorded
    require_number "the driver's byte count for ${session}" "${text_value}"
    number_value="${text_value}"
}

recording_size() {
    local volume="$1"
    local file="$2"

    if ! capture docker run --rm -v "${volume}:/rec" "${python_image}" \
        stat -c '%s' "/rec/${file}"; then
        fail "the recording file ${file} could not be measured: ${error_text}"
    fi

    require_number "the size of ${file}" "${text_value}"
    number_value="${text_value}"
}

check_continuity() {
    local volume="$1"
    local file="$2"

    if ! capture docker run --rm \
        -v "${volume}:/rec" \
        -v "${repo_root}/docker/check-recording-continuity.py:/check.py:ro" \
        "${python_image}" python /check.py "/rec/${file}"; then
        fail "the recording ${file} is not continuous: ${text_value} ${error_text}"
    fi

    echo "${text_value}" | sed 's/^/     /'
}

app_requests() {
    local container="$1"
    local path="$2"
    local out
    local status

    set +e
    out="$(docker logs "${container}" 2>&1)"
    status=$?
    set -e

    if [ "${status}" -ne 0 ]; then
        fail "the driver's log could not be read: ${out}"
    fi

    count_value="$(printf '%s\n' "${out}" | grep -c "Request starting HTTP/1.1 GET http://driver${path}" || true)"
    require_number "the number of ${path} requests the app made" "${count_value}"
}

wait_until() {
    local limit="$1"
    local elapsed=0

    shift

    while [ "${elapsed}" -lt "${limit}" ]; do
        if "$@" >/dev/null 2>&1; then
            return 0
        fi
        sleep 1
        elapsed=$((elapsed + 1))
    done

    return 1
}

heading() {
    echo
    echo "### ${scenario_id}: $*"
}

fetch_tools
