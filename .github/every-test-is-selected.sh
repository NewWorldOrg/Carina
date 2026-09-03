#!/bin/sh
set -eu

: "${UNIT_FILTER:?}"
: "${FEATURE_FILTER:?}"
: "${DB_INTEGRATION_FILTER:?}"
: "${SCALE_FILTER:?}"
: "${MATERIAL_FILTER:?}"

held="$(mktemp -d)"
trap 'rm -rf "${held}"' EXIT

projects="$(find tests -mindepth 2 -maxdepth 2 -name '*.Tests.csproj' | LC_ALL=C sort)"

if [ -z "${projects}" ]; then
  echo "no test project was found on disk, so this check would pass having compared nothing" >&2
  exit 1
fi

listed() {
  project="$1"
  shift

  dotnet test "${project}" --no-build --list-tests "$@" > "${held}/raw" 2>&1 || true

  strange="$(grep -vE \
    '^$|^    [^ ]|^Test run for .*\(.*\)$|^The following Tests are available:$|^No test matches the given testcase filter .* in .*$' \
    "${held}/raw" || true)"

  if [ -n "${strange}" ]; then
    echo "listing ${project} said something other than a list of tests, so the population cannot be trusted:" >&2
    cat "${held}/raw" >&2
    exit 1
  fi

  sed -n 's/^    \([^ ].*\)$/\1/p' "${held}/raw"
}

: > "${held}/every-test"

for project in ${projects}; do
  held_before="$(wc -l < "${held}/every-test")"
  listed "${project}" >> "${held}/every-test"

  if [ "$(wc -l < "${held}/every-test")" -le "${held_before}" ]; then
    echo "${project} listed no test at all, so the population silently shrank by a whole project" >&2
    exit 1
  fi
done

: > "${held}/selected-by-a-job"

for project in ${projects}; do
  if ! grep -qF "Path=\"${project}\"" Carina.slnx; then
    echo "${project} is on disk but not in the solution, so no job builds or runs it" >&2
    continue
  fi

  listed "${project}" --filter "${UNIT_FILTER}" >> "${held}/selected-by-a-job"
  listed "${project}" --filter "${FEATURE_FILTER}" >> "${held}/selected-by-a-job"
  listed "${project}" --filter "${DB_INTEGRATION_FILTER}" >> "${held}/selected-by-a-job"
  listed "${project}" --filter "${SCALE_FILTER}" >> "${held}/selected-by-a-job"
  listed "${project}" --filter "${MATERIAL_FILTER}" >> "${held}/selected-by-a-job"
done

LC_ALL=C sort -u -o "${held}/every-test" "${held}/every-test"
LC_ALL=C sort -u -o "${held}/selected-by-a-job" "${held}/selected-by-a-job"

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

echo "$(wc -l < "${held}/every-test") tests across $(echo "${projects}" | wc -l) test projects on disk, every one of them selected by a job."
