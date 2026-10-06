#!/usr/bin/env bash
set -euo pipefail

readonly usage="usage: $0 <output directory> <role> <published .deps.json | project.assets.json>"
readonly out="${1:?${usage}}"
readonly role="${2:?${usage}}"
readonly manifest="${3:?${usage}}"
readonly here="$(cd "$(dirname "$0")" && pwd)"
readonly packages="${NUGET_PACKAGES:-${HOME}/.nuget/packages}"
readonly index="${out}/nuget/${role}.tsv"

fail() {
    echo "$*" >&2
    exit 1
}

carried_packages() {
    case "${manifest}" in
        *.deps.json)
            jq -r '.libraries | to_entries[] | select(.value.type == "package") | .key' "${manifest}"
            ;;
        */project.assets.json)
            jq -r '
                .targets[] | to_entries[]
                | select(.value.type == "package")
                | select([(.value.runtime, .value.native, .value.runtimeTargets) | select(. != null) | keys[] | select(endswith("/_._") | not)] | length > 0)
                | .key
            ' "${manifest}"
            ;;
        *) fail "${manifest} is neither a published .deps.json nor a project.assets.json" ;;
    esac | LC_ALL=C sort -u
}

runtime_packs() {
    case "${manifest}" in
        */project.assets.json)
            jq -r '
                .project.frameworks[].downloadDependencies[]?
                | "\(.name)/\(.version | ltrimstr("[") | split(",")[0] | rtrimstr("]"))"
            ' "${manifest}"
            ;;
    esac | LC_ALL=C sort -u
}

package_directory() {
    local directory="${packages}/${1,,}/${2,,}"
    [ -d "${directory}" ] || fail "${1} ${2} is carried by ${role} but is not in ${packages}"
    echo "${directory}"
}

nuspec_value() {
    sed -n "s:.*<$1[^>]*>\([^<]*\)</$1>.*:\1:p" "$2" \
        | sed -n 1p \
        | sed -e 's/&lt;/</g' -e 's/&gt;/>/g' -e 's/&quot;/"/g' -e "s/&apos;/'/g" -e 's/&amp;/\&/g'
}

copy_notice_files() {
    local source="$1"
    local destination="$2"
    local file

    while IFS= read -r file; do
        mkdir -p "${destination}/$(dirname "${file}")"
        cp "${source}/${file}" "${destination}/${file}"
    done < <(cd "${source}" && find . -maxdepth 2 -type f \( -iname 'licen[cs]e*' -o -iname 'copying*' -o -iname '*notice*' \) | sed 's:^\./::')
}

carries_license_text() {
    [ -n "$(find "$1" -type f \( -iname 'licen[cs]e*' -o -iname 'copying*' \) -print -quit)" ]
}

write_package() {
    local id="${1%%/*}"
    local version="${1#*/}"
    local source
    source="$(package_directory "${id}" "${version}")"
    local nuspec="${source}/${id,,}.nuspec"
    local destination="${out}/nuget/${id}/${version}"
    local expression
    expression="$(nuspec_value license "${nuspec}")"
    local license_type
    license_type="$(sed -n 's:.*<license type="\([a-z]*\)".*:\1:p' "${nuspec}" | sed -n 1p)"

    local declared_file=""

    mkdir -p "${destination}"
    copy_notice_files "${source}" "${destination}"

    if [ "${license_type}" = file ]; then
        mkdir -p "${destination}/$(dirname "${expression}")"
        cp "${source}/${expression}" "${destination}/${expression}"
        declared_file="${expression}"
        expression="its own license file"
    elif [ "${license_type}" != expression ]; then
        expression=""
    fi

    if [ -f "${here}/nuget/${id}.txt" ]; then
        cp "${here}/nuget/${id}.txt" "${destination}/LICENSE"
    elif [ -z "${declared_file}" ] && ! carries_license_text "${destination}"; then
        [ -n "${expression}" ] \
            || fail "${id} ${version} carries no license text and declares no license expression: put its license at docker/notices/nuget/${id}.txt"
        [ -f "${here}/spdx/${expression}.txt" ] \
            || fail "${id} ${version} is licensed as '${expression}', which has no text at docker/notices/spdx/${expression}.txt: put it there, or put the package's own license at docker/notices/nuget/${id}.txt"
        local copyright
        copyright="$(nuspec_value copyright "${nuspec}")"
        [ -n "${copyright}" ] \
            || fail "${id} ${version} states no copyright, so the ${expression} text cannot name its holder: put its license at docker/notices/nuget/${id}.txt"
        { echo "${copyright}"; echo; cat "${here}/spdx/${expression}.txt"; } > "${destination}/LICENSE"
    fi

    printf '%s\t%s\t%s\n' "${id}" "${version}" "${expression:-unstated}" >> "${index}"
}

write_runtime_pack() {
    local id="${1%%/*}"
    local version="${1#*/}"
    local source
    source="$(package_directory "${id}" "${version}")"
    local destination="${out}/dotnet/${id}/${version}"

    mkdir -p "${destination}"
    copy_notice_files "${source}" "${destination}"
    [ -n "$(ls -A "${destination}")" ] || fail "the runtime pack ${id} ${version} carries no license or notice file"
}

main() {
    local package carried packs

    [ -f "${manifest}" ] || fail "${manifest} is not there to read what ${role} carries"
    carried="$(carried_packages)"
    packs="$(runtime_packs)"

    mkdir -p "${out}/nuget"
    : > "${index}"

    for package in ${carried}; do
        write_package "${package}"
    done

    for package in ${packs}; do
        write_runtime_pack "${package}"
    done

    echo "${role} carries $(wc -l < "${index}") NuGet packages and $(echo "${packs}" | grep -c .) runtime packs"
}

main
