#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
mode="${1:-check}"
compose_file="${CARINA_DEPLOY_FILE:-${repo_root}/compose.deploy.yml}"
image="${CARINA_IMAGE:-carina}"
config_file="${CARINA_DRIVER_CONFIG_FILE:-}"
margin_seconds="${CARINA_STOP_GRACE_MARGIN:-60}"

fail() {
    echo "FAIL: $*" >&2
    exit 1
}

usage() {
    echo "usage: grace-period.sh [derive|check]" >&2
    exit 64
}

require_config() {
    if [ -z "${config_file}" ]; then
        fail "CARINA_DRIVER_CONFIG_FILE names the driver configuration this deployment runs; set it."
    fi

    if [ ! -r "${config_file}" ]; then
        fail "the driver configuration '${config_file}' is not readable."
    fi
}

require_image() {
    if ! docker image inspect "${image}" >/dev/null 2>&1; then
        fail "the image '${image}' is not built; run 'task image' or set CARINA_IMAGE."
    fi
}

budget_seconds() {
    require_config
    require_image

    docker run --rm \
        --entrypoint /opt/carina/driver/Carina.Driver \
        -e CARINA_DRIVER_CONFIG=/etc/carina/driver.json \
        -v "$(cd "$(dirname "${config_file}")" && pwd)/$(basename "${config_file}"):/etc/carina/driver.json:ro" \
        "${image}" --shutdown-budget
}

compose_grace_seconds() {
    local rendered
    local status

    set +e
    rendered="$(docker compose -f "${compose_file}" config --format json 2>&1)"
    status=$?
    set -e

    if [ "${status}" -ne 0 ]; then
        fail "${compose_file} did not render: ${rendered}"
    fi

    printf '%s' "${rendered}" | python3 -c '
import json, re, sys

document = json.load(sys.stdin)
services = document.get("services", {})
driver = services.get("driver")

if driver is None:
    print("no-driver-service " + ",".join(sorted(services)))
    raise SystemExit(0)

grace = driver.get("stop_grace_period")

if grace is None:
    print("no-stop-grace-period")
    raise SystemExit(0)

units = {"ns": 1e-9, "us": 1e-6, "ms": 1e-3, "s": 1, "m": 60, "h": 3600}
pattern = re.compile(r"\A(?:(\d+(?:\.\d+)?)(ns|us|ms|s|m|h))+\Z")

if not pattern.match(grace):
    print("unreadable " + grace)
    raise SystemExit(0)

total = sum(
    float(value) * units[unit]
    for value, unit in re.findall(r"(\d+(?:\.\d+)?)(ns|us|ms|s|m|h)", grace)
)
print(f"seconds {total:.6f} {grace}")
'
}

case "${mode}" in
    derive)
        budget="$(budget_seconds)"
        echo "$((budget + margin_seconds))s"
        ;;
    check)
        budget="$(budget_seconds)"
        answer=""

        if ! answer="$(compose_grace_seconds)"; then
            exit 1
        fi

        read -r kind value rendered <<<"${answer}"

        if [ "${kind}" = "no-driver-service" ]; then
            fail "${compose_file} has no 'driver' service; it declares: ${value}"
        fi

        if [ "${kind}" = "no-stop-grace-period" ]; then
            fail "${compose_file} declares no stop_grace_period for the driver service."
        fi

        if [ "${kind}" = "unreadable" ]; then
            fail "${compose_file} declares stop_grace_period '${value}', which is not a duration this check understands."
        fi

        grace="${value%.*}"

        if ! [[ "${budget}" =~ ^[0-9]+$ ]]; then
            fail "the driver reported '${budget}' as its shutdown budget, which is not a number of seconds."
        fi

        if ! [[ "${grace}" =~ ^[0-9]+$ ]]; then
            fail "${compose_file} yielded '${value}' as its stop_grace_period, which is not a number of seconds."
        fi

        if [ "${grace}" -le "${budget}" ]; then
            fail "stop_grace_period ${rendered} (${grace}s) does not exceed the driver's own shutdown budget of ${budget}s; the runtime would SIGKILL the driver while it was still closing a recording. 'grace-period.sh derive' prints a value that does."
        fi

        echo "OK: stop_grace_period ${rendered} (${grace}s) exceeds the driver's shutdown budget of ${budget}s, taken from the driver itself with --shutdown-budget."
        ;;
    *)
        usage
        ;;
esac
