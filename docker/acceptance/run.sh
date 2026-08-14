#!/usr/bin/env bash
set -uo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
image="${CARINA_ACCEPTANCE_IMAGE:-carina}"

if ! docker image inspect "${image}" >/dev/null 2>&1; then
    echo "FAIL: the image '${image}' is not built; run 'task image' or set CARINA_ACCEPTANCE_IMAGE." >&2
    exit 1
fi

scripts=()

if [ "$#" -gt 0 ]; then
    for wanted in "$@"; do
        matches=("${here}/${wanted}"*.sh)
        if [ ! -f "${matches[0]}" ]; then
            echo "FAIL: no scenario matches '${wanted}'." >&2
            exit 64
        fi
        scripts+=("${matches[0]}")
    done
else
    for candidate in "${here}"/[0-9][0-9]-*.sh; do
        scripts+=("${candidate}")
    done
fi

if [ "${#scripts[@]}" -eq 0 ]; then
    echo "FAIL: there are no scenarios to run." >&2
    exit 1
fi

names=()
verdicts=()
seconds=()
failures=0

for script in "${scripts[@]}"; do
    name="$(basename "${script}" .sh)"
    began="$(date +%s)"

    echo
    echo "======================================================================"
    echo "${name}"
    echo "======================================================================"

    if bash "${script}"; then
        verdict="PASS"
    else
        verdict="FAIL"
        failures=$((failures + 1))
    fi

    names+=("${name}")
    verdicts+=("${verdict}")
    seconds+=("$(($(date +%s) - began))")
done

echo
echo "======================================================================"
echo "acceptance summary against ${image}"
echo "======================================================================"

index=0
while [ "${index}" -lt "${#names[@]}" ]; do
    printf '%-4s %-28s %ss\n' "${verdicts[${index}]}" "${names[${index}]}" "${seconds[${index}]}"
    index=$((index + 1))
done

echo
if [ "${failures}" -ne 0 ]; then
    echo "${failures} of ${#names[@]} scenarios failed."
    exit 1
fi

echo "${#names[@]} scenarios passed. The two tag streams, the architecture test self-check and all-role zombie reaping are not in this suite; docker/acceptance/README.md says where they live."
