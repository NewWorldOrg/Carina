#!/usr/bin/env bash
set -euo pipefail

readonly driver_entry=/opt/carina/driver/Carina.Driver
readonly app_entry=/opt/carina/app/Carina.Api.dll
readonly migrate_entry=/opt/carina/db/Carina.Db.dll
readonly render_nodes=/dev/dri
readonly carina_uid=10001
readonly carina_gid=10001

drop_web_server_variables() {
    local name
    for name in ASPNETCORE_URLS ASPNETCORE_HTTP_PORTS ASPNETCORE_HTTPS_PORTS; do
        if [ -n "${!name:-}" ]; then
            echo "role=driver ignores ${name}=${!name}; the driver answers on a Unix socket only." >&2
        fi
        unset "${name}"
    done
}

drop_database_variables() {
    local name
    for name in ConnectionStrings__Carina CARINA_DB_CONNECTION; do
        if [ -n "${!name:-}" ]; then
            echo "role=driver ignores ${name}; the driver holds no secrets." >&2
        fi
        unset "${name}"
    done
}

adopt_shared_connection_string() {
    if [ -z "${CARINA_DB_CONNECTION:-}" ] && [ -n "${ConnectionStrings__Carina:-}" ]; then
        export CARINA_DB_CONNECTION="${ConnectionStrings__Carina}"
    fi
}

run_as_carina() {
    if [ "$(id -u)" = 0 ]; then
        exec setpriv --reuid "${carina_uid}" --regid "${carina_gid}" --clear-groups --no-new-privs "$@"
    fi
    exec "$@"
}

render_node_groups() {
    local node
    local gid
    for node in "${render_nodes}"/card* "${render_nodes}"/renderD*; do
        [ -c "${node}" ] || continue
        if ! gid="$(stat -c %g "${node}")"; then
            echo "${node} could not be read; role=app is not given its group." >&2
            continue
        fi
        echo "${gid}"
    done
}

app_groups() {
    local list=''
    local gid
    for gid in "${carina_gid}" $(render_node_groups); do
        if [ "${gid}" = 0 ]; then
            echo "role=app is not given group 0." >&2
            continue
        fi
        case ",${list}," in
            *",${gid},"*) continue ;;
        esac
        list="${list:+${list},}${gid}"
    done
    printf '%s' "${list}"
}

require_writable_keys() {
    local keys="${CARINA_DATA_PROTECTION_KEYS:-}"
    [ -n "${keys}" ] || return 0
    mkdir -p -- "${keys}" 2>/dev/null || true
    if [ -d "${keys}" ] && [ -w "${keys}" ] && [ -x "${keys}" ]; then
        return 0
    fi
    echo "role=app runs as uid $(id -u) with groups $(id -G | tr ' ' ',') and cannot write CARINA_DATA_PROTECTION_KEYS=${keys}; mount a directory there that this uid or one of these groups can write." >&2
    exit 77
}

run_app() {
    if [ "$(id -u)" = 0 ]; then
        local groups
        groups="$(app_groups)"
        exec setpriv --reuid "${carina_uid}" --regid "${carina_gid}" --groups "${groups}" --no-new-privs "$0" app
    fi
    require_writable_keys
    exec dotnet "${app_entry}"
}

run_all() {
    ( drop_web_server_variables; drop_database_variables; exec "${driver_entry}" ) &
    local driver_pid=$!

    ( run_app ) &
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
            drop_database_variables
            exec "${driver_entry}"
            ;;
        app) run_app ;;
        migrate)
            adopt_shared_connection_string
            run_as_carina dotnet "${migrate_entry}" --migrate
            ;;
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
