#!/usr/bin/env bash
set -euo pipefail

readonly driver_entry=/opt/carina/driver/Carina.Driver
readonly app_entry=/opt/carina/app/Carina.Api.dll
readonly migrate_entry=/opt/carina/db/Carina.Db.dll

drop_web_server_variables() {
    local name
    for name in ASPNETCORE_URLS ASPNETCORE_HTTP_PORTS ASPNETCORE_HTTPS_PORTS; do
        if [ -n "${!name:-}" ]; then
            echo "role=driver ignores ${name}=${!name}; the driver answers on a Unix socket only." >&2
        fi
        unset "${name}"
    done
}

run_as_carina() {
    if [ "$(id -u)" = 0 ]; then
        exec setpriv --reuid carina --regid carina --init-groups "$@"
    fi
    exec "$@"
}

run_all() {
    ( drop_web_server_variables; exec "${driver_entry}" ) &
    local driver_pid=$!

    ( run_as_carina dotnet "${app_entry}" ) &
    local app_pid=$!

    trap 'kill -TERM "${driver_pid}" "${app_pid}" 2>/dev/null || true' TERM INT

    set +e
    wait -n "${driver_pid}" "${app_pid}"
    local status=$?
    set -e

    kill -TERM "${driver_pid}" "${app_pid}" 2>/dev/null || true
    wait "${driver_pid}" "${app_pid}" 2>/dev/null || true

    exit "${status}"
}

main() {
    local role="${1:-${CARINA_ROLE:-app}}"

    case "${role}" in
        driver)
            drop_web_server_variables
            exec "${driver_entry}"
            ;;
        app) run_as_carina dotnet "${app_entry}" ;;
        migrate) run_as_carina dotnet "${migrate_entry}" --migrate ;;
        web)
            echo "role=web carries no asset in this image; the distribution image build supplies it." >&2
            exec sleep infinity
            ;;
        all) run_all ;;
        *)
            echo "unknown role '${role}': expected driver, app, web, all or migrate." >&2
            exit 64
            ;;
    esac
}

main "$@"
