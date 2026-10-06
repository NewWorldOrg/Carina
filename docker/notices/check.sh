#!/usr/bin/env bash
set -euo pipefail

readonly usage="usage: $0 <dpkg status file of the base image>"
readonly base_status="${1:?${usage}}"
readonly doc=/usr/share/doc/carina
readonly notices="${doc}/THIRD-PARTY-NOTICES.md"
readonly nuget_section="NuGet packages"
readonly ubuntu_section="Ubuntu packages added to the base image"

fail() {
    echo "$*" >&2
    exit 1
}

rows_of() {
    awk -v wanted="## $1" '
        /^## / { inside = ($0 == wanted); next }
        inside && /^\| `/ {
            count = split($0, cells, "|")
            row = ""
            for (i = 2; i < count; i++) {
                cell = cells[i]
                gsub(/`/, "", cell)
                gsub(/^ +| +$/, "", cell)
                row = row (i > 2 ? "\t" : "") cell
            }
            print row
        }
    ' "${notices}" | LC_ALL=C sort
}

carried_nuget() {
    local index

    for index in "${doc}"/nuget/*.tsv; do
        awk -F '\t' -v role="$(basename "${index}" .tsv)" '{ print $1 "\t" $3 "\t" role }' "${index}"
    done | LC_ALL=C sort -u | awk -F '\t' '
        {
            key = $1 "\t" $2
            if (!(key in roles)) { order[++count] = key; roles[key] = $3 } else { roles[key] = roles[key] ", " $3 }
        }
        END { for (i = 1; i <= count; i++) print order[i] "\t" roles[order[i]] }
    ' | LC_ALL=C sort
}

installed_over_the_base() {
    local base
    base="$(awk '/^Package: / { name = $2 } /^Status: / && $NF == "installed" { print name }' "${base_status}" | LC_ALL=C sort -u)"
    [ -n "${base}" ] || fail "${base_status} names no installed package"

    dpkg-query -W -f '${db:Status-Status}\t${Package}\n' \
        | awk -F '\t' '$1 == "installed" { print $2 }' \
        | LC_ALL=C sort -u \
        | LC_ALL=C comm -13 <(echo "${base}") -
}

same_or_fail() {
    local what="$1"
    local declared="$2"
    local carried="$3"

    [ "${declared}" = "${carried}" ] && return 0

    echo "the '${what}' table of THIRD-PARTY-NOTICES.md is not what the image carries (< listed only, > carried only):" >&2
    diff <(echo "${declared}") <(echo "${carried}") | grep '^[<>]' >&2 || true
    exit 1
}

check_corresponding_sources() {
    local package source license version

    while IFS=$'\t' read -r package source license; do
        [ "$(dpkg-query -W -f '${source:Package}' "${package}")" = "${source}" ] \
            || fail "THIRD-PARTY-NOTICES.md names ${source} as the source package of ${package}, and dpkg does not"

        case "${license}" in
            *GPL*)
                version="$(dpkg-query -W -f '${source:Version}' "${package}")"
                [ -f "${doc}/${source}/source/${source}_${version#*:}.dsc" ] \
                    || fail "${package} is under ${license}, and the source of ${source} ${version} is not at ${doc}/${source}/source/"
                ;;
        esac
    done < <(rows_of "${ubuntu_section}")
}

main() {
    local nuget ubuntu role

    for role in app migrate driver; do
        [ -f "${doc}/nuget/${role}.tsv" ] || fail "the image holds no index of the NuGet packages ${role} carries"
    done
    [ -n "$(find "${doc}/dotnet" -type f -print -quit 2>/dev/null)" ] \
        || fail "the image holds no notice of the runtime packs the driver was compiled with"

    nuget="$(carried_nuget)"
    [ -n "${nuget}" ] || fail "no NuGet package index is in ${doc}/nuget"
    same_or_fail "${nuget_section}" "$(rows_of "${nuget_section}")" "${nuget}"

    ubuntu="$(installed_over_the_base)"
    same_or_fail "${ubuntu_section}" "$(rows_of "${ubuntu_section}" | cut -f 1)" "${ubuntu}"
    check_corresponding_sources

    echo "THIRD-PARTY-NOTICES.md lists the $(echo "${nuget}" | wc -l) NuGet packages and $(echo "${ubuntu}" | wc -l) Ubuntu packages the image carries"
}

main
