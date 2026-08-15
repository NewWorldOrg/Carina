#!/usr/bin/env bash
set -euo pipefail

readonly project=src/Carina.Driver/Carina.Driver.csproj
readonly output=/driver
readonly cooldown="${CARINA_DEV_BUILD_COOLDOWN_SECONDS:-30}"

if ! dotnet build "${project}" --artifacts-path "${output}"; then
    echo "The driver did not compile. Fix the errors above and this container picks the fix up by itself; it waits ${cooldown}s between attempts so a broken tree does not build in a loop." >&2
    sleep "${cooldown}"
    exit 1
fi

exec dotnet "${output}/bin/Carina.Driver/debug/Carina.Driver.dll"
