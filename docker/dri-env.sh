#!/usr/bin/env bash
set -euo pipefail

readonly dri=/dev/dri
readonly card="${dri}/card0"
readonly default_render="${dri}/renderD128"
readonly env_file="$(dirname "${BASH_SOURCE[0]}")/../.env"

render_node() {
    if [ -n "${CARINA_RENDER_NODE:-}" ]; then
        printf '%s' "${CARINA_RENDER_NODE}"
        return
    fi
    local written=''
    if [ -f "${env_file}" ]; then
        written="$(sed -n 's/^CARINA_RENDER_NODE=//p' "${env_file}" | tail -n 1 | tr -d "\"'")"
    fi
    printf '%s' "${written:-${default_render}}"
}

readonly render="$(render_node)"

if [ ! -c "${card}" ] && [ ! -c "${render}" ]; then
    exit 0
fi

echo "CARINA_DRI=${dri}"

if [ -c "${card}" ]; then
    echo "CARINA_DRI_VIDEO_GID=$(stat -c %g "${card}")"
fi

if [ -c "${render}" ]; then
    echo "CARINA_DRI_RENDER_GID=$(stat -c %g "${render}")"
else
    echo "${render} is not a render node on this host; app is not given its group and encodes on the processor." >&2
fi
