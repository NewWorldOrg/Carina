#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
compose_file="${1:-${repo_root}/compose.deploy.yml}"
reader_source="${repo_root}/src/Carina.Driver/Configuration/DriverConfigurationReader.cs"

fail() {
    echo "FAIL: $*" >&2
    exit 1
}

read_driver_service() {
    docker compose -f "${compose_file}" config --format json | python3 -c '
import json, re, sys

document = json.load(sys.stdin)
driver = document.get("services", {}).get("driver", {})
units = {"h": 3600, "m": 60, "s": 1}
grace = driver.get("stop_grace_period")
seconds = (
    "unset"
    if grace is None
    else sum(int(n) * units[u] for n, u in re.findall(r"(\d+)([hms])", grace))
)
mount = next(
    (
        volume.get("source", "unset")
        for volume in driver.get("volumes", [])
        if volume.get("target") == "/etc/carina/driver.json"
    ),
    "unset",
)
print(seconds)
print(mount)
'
}

mapfile -t driver_service < <(read_driver_service)
grace_seconds="${driver_service[0]:-unset}"
config_file="${driver_service[1]:-unset}"

if [ "${grace_seconds}" = "unset" ]; then
    fail "${compose_file} declares no stop_grace_period for the driver service."
fi

if [ "${config_file}" = "unset" ]; then
    fail "${compose_file} mounts no driver configuration at /etc/carina/driver.json."
fi

if [ ! -r "${config_file}" ]; then
    fail "the driver configuration ${config_file} is not readable."
fi

configured_hours="$(python3 -c '
import json, sys

print(json.load(open(sys.argv[1]))["shutdownGraceHours"])
' "${config_file}")"

max_hours="$(grep -oP 'MaxShutdownGraceHours\s*=\s*\K[0-9]+' "${reader_source}")"

if [ -z "${max_hours}" ]; then
    fail "could not read MaxShutdownGraceHours from ${reader_source}."
fi

if [ "${configured_hours}" -gt "${max_hours}" ]; then
    fail "shutdownGraceHours=${configured_hours} in ${config_file} exceeds the driver's own cap of ${max_hours}."
fi

linger_seconds=$((configured_hours * 3600))

if [ "${grace_seconds}" -le "${linger_seconds}" ]; then
    fail "stop_grace_period=${grace_seconds}s does not exceed the driver's linger cap of ${linger_seconds}s (shutdownGraceHours=${configured_hours} in ${config_file}); the runtime would SIGKILL a recording that the driver was still finishing."
fi

echo "OK: stop_grace_period=${grace_seconds}s > linger cap ${linger_seconds}s (shutdownGraceHours=${configured_hours}, driver cap ${max_hours}h) from ${config_file}."
