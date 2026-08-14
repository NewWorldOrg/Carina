#!/usr/bin/env bash
set -euo pipefail

repo_root="${CARINA_TAG_REPO:-$(cd "$(dirname "$0")/.." && pwd)}"
mode="${1:-}"

driver_roots=(src/Carina.Driver)
app_roots=(src/Carina.Api src/Carina.Db)
shared_inputs=(.dockerignore Directory.Build.props Directory.Packages.props Dockerfile docker/entrypoint.sh)
workflow=.github/workflows/ci.yml

fail() {
    echo "FAIL: $*" >&2
    exit 1
}

usage() {
    echo "usage: image-tags.sh [inputs <driver|app>|tag <driver|app>|check|prove]" >&2
    exit 64
}

project_closure() {
    python3 - "${repo_root}" "$@" <<'PY'
import pathlib
import sys
import xml.etree.ElementTree as ElementTree

root = pathlib.Path(sys.argv[1])
pending = list(sys.argv[2:])
seen = set()

while pending:
    project = pending.pop()
    if project in seen:
        continue
    seen.add(project)
    candidates = sorted((root / project).glob("*.csproj"))
    if len(candidates) != 1:
        print(f"error {project} holds {len(candidates)} project files, expected exactly one", file=sys.stderr)
        raise SystemExit(1)
    document = ElementTree.parse(candidates[0])
    for reference in document.iter("ProjectReference"):
        include = reference.get("Include")
        if include is None:
            print(f"error {candidates[0]} has a ProjectReference without an Include", file=sys.stderr)
            raise SystemExit(1)
        target = (candidates[0].parent / include.replace("\\", "/")).resolve().parent
        pending.append(str(target.relative_to(root.resolve())))

for project in sorted(seen):
    print(project)
PY
}

stream_inputs() {
    local stream="$1"
    local projects

    case "${stream}" in
        driver) projects="$(project_closure "${driver_roots[@]}")" ;;
        app) projects="$(project_closure "${app_roots[@]}")" ;;
        *) fail "unknown stream '${stream}': expected driver or app." ;;
    esac

    if [ -z "${projects}" ]; then
        fail "the ${stream} stream resolved to no projects at all; its tag would be keyed on nothing."
    fi

    printf '%s\n' "${projects}"
    printf '%s\n' "${shared_inputs[@]}"
}

require_full_history() {
    if ! git -C "${repo_root}" rev-parse --git-dir >/dev/null 2>&1; then
        fail "${repo_root} is not a git repository; the tags are the commits that last touched each stream."
    fi

    if [ "$(git -C "${repo_root}" rev-parse --is-shallow-repository)" = "true" ]; then
        fail "this clone is shallow: 'the commit that last touched these paths' collapses onto HEAD, so both streams would move on every push and the two roles would silently stop being releasable one at a time. Check out with fetch-depth: 0."
    fi
}

tag_for() {
    local stream="$1"
    local listed
    local -a inputs
    local commit

    require_full_history
    listed="$(stream_inputs "${stream}")"
    mapfile -t inputs <<<"${listed}"
    commit="$(git -C "${repo_root}" log -1 --format=%H -- "${inputs[@]}")"

    if [ -z "${commit}" ]; then
        fail "no commit in this history touches the ${stream} inputs; the stream has no tag to name."
    fi

    printf '%s-sha-%s\n' "${stream}" "${commit:0:12}"
}

check_declared_inputs_exist() {
    local path

    for path in "${shared_inputs[@]}" "${workflow}"; do
        if [ ! -e "${repo_root}/${path}" ]; then
            fail "declared input '${path}' does not exist; a rename would drop it from the tag inputs without anything failing."
        fi
    done
}

check_every_project_belongs_to_a_stream() {
    local claimed
    local present
    local orphans

    claimed="$(printf '%s\n%s\n' "$(project_closure "${driver_roots[@]}")" "$(project_closure "${app_roots[@]}")" | sort -u)"
    present="$(cd "${repo_root}" && find src -mindepth 2 -maxdepth 2 -name '*.csproj' -printf '%h\n' | sort -u)"
    orphans="$(comm -13 <(printf '%s\n' "${claimed}") <(printf '%s\n' "${present}"))"

    if [ -n "${orphans}" ]; then
        fail "these projects belong to no tag stream, so a change to them would move neither tag: ${orphans//$'\n'/, }. Add them to a role's roots, or to the role that reaches them."
    fi
}

check_the_streams_are_separable() {
    local driver_projects
    local app_projects

    driver_projects="$(project_closure "${driver_roots[@]}")"
    app_projects="$(project_closure "${app_roots[@]}")"

    if grep -qxF "${app_roots[0]}" <<<"${driver_projects}"; then
        fail "the driver stream reaches ${app_roots[0]}; every app change would move the driver tag, so releasing the app would recreate the driver and end a recording in progress."
    fi

    if grep -qxF "${driver_roots[0]}" <<<"${app_projects}"; then
        fail "the app stream reaches ${driver_roots[0]}; every driver change would move the app tag."
    fi
}

dockerfile_stage_projects() {
    python3 - "${repo_root}/Dockerfile" "$1" <<'PY'
import pathlib
import re
import sys

stage = sys.argv[2]
current = None
found = set()

for line in pathlib.Path(sys.argv[1]).read_text().splitlines():
    stripped = line.strip()
    heading = re.match(r"FROM\s+\S+(?:\s+AS\s+(\S+))?$", stripped, re.IGNORECASE)
    if heading:
        current = heading.group(1)
        continue
    if current != stage or not stripped.upper().startswith("COPY "):
        continue
    for match in re.finditer(r"(?<![\w./-])(src/[A-Za-z0-9._-]+)/", stripped):
        found.add(match.group(1))

for project in sorted(found):
    print(project)
PY
}

check_the_image_build_copies_what_the_stream_hashes() {
    local stream="$1"
    local stage="$2"
    local copied
    local projects
    local difference

    copied="$(dockerfile_stage_projects "${stage}")"

    case "${stream}" in
        driver) projects="$(project_closure "${driver_roots[@]}")" ;;
        app) projects="$(project_closure "${app_roots[@]}")" ;;
        *) fail "unknown stream '${stream}': expected driver or app." ;;
    esac

    if [ -z "${copied}" ]; then
        fail "the Dockerfile stage '${stage}' names no project of its own; this check cannot tell whether the ${stream} inputs still cover the build."
    fi

    difference="$(diff <(printf '%s\n' "${copied}") <(printf '%s\n' "${projects}" | sort -u) || true)"

    if [ -n "${difference}" ]; then
        fail "the Dockerfile stage '${stage}' and the ${stream} tag inputs disagree about which projects build that role:"$'\n'"${difference}"$'\n'"< is what the image copies, > is what the tag is keyed on. Whichever is stale, they cannot differ."
    fi
}

check_the_build_takes_no_arguments_the_inputs_miss() {
    local offending

    offending="$(grep -n -- '--build-arg' "${repo_root}/${workflow}" || true)"

    if [ -n "${offending}" ]; then
        fail "${workflow} passes build arguments the tag inputs cannot see, so two different images could share one tag:"$'\n'"${offending}"
    fi
}

probe_file() {
    local project="$1"
    local candidate

    candidate="$(git -C "${repo_root}" ls-files "${project}" | grep -E '\.cs$' | head -1)"

    if [ -z "${candidate}" ]; then
        fail "no tracked source file under ${project} to change; the demonstration would prove nothing."
    fi

    printf '%s\n' "${candidate}"
}

scratch=

remove_scratch_worktree() {
    if [ -n "${scratch}" ]; then
        git -C "${repo_root}" worktree remove --force "${scratch}" >/dev/null 2>&1 || true
        rm -rf "${scratch}"
    fi
}

prove_the_streams_move_independently() {
    local driver_probe
    local app_probe
    local before_driver
    local before_app
    local after_driver
    local after_app

    require_full_history
    driver_probe="$(probe_file "${driver_roots[0]}")"
    app_probe="$(probe_file "${app_roots[0]}")"
    scratch="$(mktemp -d)"
    trap remove_scratch_worktree EXIT

    git -C "${repo_root}" worktree add --detach "${scratch}" HEAD >/dev/null 2>&1 \
        || fail "could not create a scratch worktree to run the demonstration in."

    before_driver="$(CARINA_TAG_REPO="${scratch}" "$0" tag driver)"
    before_app="$(CARINA_TAG_REPO="${scratch}" "$0" tag app)"
    echo "at HEAD: ${before_driver} / ${before_app}"

    commit_probe "${scratch}" "${app_probe}" "an app-only change"
    after_driver="$(CARINA_TAG_REPO="${scratch}" "$0" tag driver)"
    after_app="$(CARINA_TAG_REPO="${scratch}" "$0" tag app)"

    if [ "${after_driver}" != "${before_driver}" ]; then
        fail "a change touching only ${app_probe} moved the driver tag ${before_driver} to ${after_driver}."
    fi

    if [ "${after_app}" = "${before_app}" ]; then
        fail "a change touching only ${app_probe} left the app tag at ${before_app}; the driver tag holding still therefore proves nothing about the streams."
    fi

    echo "after ${app_probe}: driver stays ${after_driver}, app moves to ${after_app}"

    git -C "${scratch}" reset --hard HEAD~1 >/dev/null
    commit_probe "${scratch}" "${driver_probe}" "a driver-only change"
    after_driver="$(CARINA_TAG_REPO="${scratch}" "$0" tag driver)"
    after_app="$(CARINA_TAG_REPO="${scratch}" "$0" tag app)"

    if [ "${after_app}" != "${before_app}" ]; then
        fail "a change touching only ${driver_probe} moved the app tag ${before_app} to ${after_app}."
    fi

    if [ "${after_driver}" = "${before_driver}" ]; then
        fail "a change touching only ${driver_probe} left the driver tag at ${before_driver}; the app tag holding still therefore proves nothing."
    fi

    echo "after ${driver_probe}: app stays ${after_app}, driver moves to ${after_driver}"

    git -C "${scratch}" reset --hard HEAD~1 >/dev/null
    commit_probe "${scratch}" Directory.Build.props "a change to the shared build settings"
    after_driver="$(CARINA_TAG_REPO="${scratch}" "$0" tag driver)"
    after_app="$(CARINA_TAG_REPO="${scratch}" "$0" tag app)"

    if [ "${after_driver}" = "${before_driver}" ] || [ "${after_app}" = "${before_app}" ]; then
        fail "a change to Directory.Build.props left ${before_driver} or ${before_app} where it was, although both roles are built with it."
    fi

    echo "after Directory.Build.props: both move, to ${after_driver} and ${after_app}"
    echo "OK: an app-only change moves only the app tag and a driver-only change only the driver tag, and the shared build settings move both streams."
}

commit_probe() {
    local tree="$1"
    local path="$2"
    local subject="$3"

    printf '\n' >>"${tree}/${path}"
    git -C "${tree}" add "${path}"
    git -C "${tree}" \
        -c user.name=carina-ci \
        -c user.email=carina-ci@invalid \
        commit --quiet -m "${subject} for the tag demonstration"
}

case "${mode}" in
    inputs)
        stream_inputs "${2:-}"
        ;;
    tag)
        tag_for "${2:-}"
        ;;
    check)
        check_declared_inputs_exist
        check_every_project_belongs_to_a_stream
        check_the_streams_are_separable
        check_the_image_build_copies_what_the_stream_hashes driver driver-build
        check_the_image_build_copies_what_the_stream_hashes app app-build
        check_the_build_takes_no_arguments_the_inputs_miss
        echo "OK: every project under src belongs to a stream, the two streams are separable, and each Dockerfile build stage copies exactly the projects its stream is keyed on."
        ;;
    prove)
        prove_the_streams_move_independently
        ;;
    *)
        usage
        ;;
esac
