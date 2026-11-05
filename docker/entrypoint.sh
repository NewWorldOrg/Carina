#!/usr/bin/env bash
set -euo pipefail

readonly app_entry=/opt/carina/app/Carina.Api.dll
readonly migrate_entry=/opt/carina/db/Carina.Db.dll
readonly render_nodes=/dev/dri
readonly carina_uid=10001
readonly carina_gid=10001

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

main() {
    local role="${1:-${CARINA_ROLE:-app}}"

    case "${role}" in
        app) run_app ;;
        migrate)
            adopt_shared_connection_string
            run_as_carina dotnet "${migrate_entry}" --migrate
            ;;
        *)
            echo "unknown role '${role}': expected app or migrate." >&2
            exit 64
            ;;
    esac
}

main "$@"
