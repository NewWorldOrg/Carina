#!/usr/bin/env bash
set -euo pipefail

readonly dri=/dev/dri
readonly card="${dri}/card0"
readonly render="${dri}/renderD128"

if [ ! -c "${card}" ] && [ ! -c "${render}" ]; then
    exit 0
fi

echo "CARINA_DRI=${dri}"

if [ -c "${card}" ]; then
    echo "CARINA_DRI_VIDEO_GID=$(stat -c %g "${card}")"
fi

if [ -c "${render}" ]; then
    echo "CARINA_DRI_RENDER_GID=$(stat -c %g "${render}")"
fi
