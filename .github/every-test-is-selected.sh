#!/bin/sh
set -eu

: "${UNIT_FILTER:?}"
: "${FEATURE_FILTER:?}"
: "${DB_INTEGRATION_FILTER:?}"
: "${SCALE_FILTER:?}"

held="$(mktemp -d)"
trap 'rm -rf "${held}"' EXIT

listed() {
  dotnet test --no-build -m:1 "$@" --list-tests | sed -n 's/^    \(Carina\..*\)$/\1/p' | LC_ALL=C sort -u
}

listed > "${held}/every-test"

if [ ! -s "${held}/every-test" ]; then
  echo "nothing was listed at all, so this check would pass having compared nothing" >&2
  exit 1
fi

{
  listed --filter "${UNIT_FILTER}"
  listed --filter "${FEATURE_FILTER}"
  listed --filter "${DB_INTEGRATION_FILTER}"
  listed --filter "${SCALE_FILTER}"
} | LC_ALL=C sort -u > "${held}/selected-by-a-job"

if [ ! -s "${held}/selected-by-a-job" ]; then
  echo "the filters between them selected nothing, so every test there is would be reported below" >&2
  exit 1
fi

unselected="$(LC_ALL=C comm -23 "${held}/every-test" "${held}/selected-by-a-job")"

if [ -n "${unselected}" ]; then
  echo "no job's filter selects these tests, so no job runs them:" >&2
  echo "${unselected}" >&2
  exit 1
fi

echo "$(wc -l < "${held}/every-test") tests, every one of them selected by a job."
