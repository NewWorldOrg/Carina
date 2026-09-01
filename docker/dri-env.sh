#!/usr/bin/env bash
set -euo pipefail

readonly dri=/dev/dri
readonly card="${dri}/card0"
readonly render="${dri}/renderD128"

[ -d "${dri}" ] || exit 0

echo "CARINA_DRI=${dri}"
[ -c "${card}" ] && echo "CARINA_DRI_VIDEO_GID=$(stat -c %g "${card}")"
[ -c "${render}" ] && echo "CARINA_DRI_RENDER_GID=$(stat -c %g "${render}")"

exit 0
