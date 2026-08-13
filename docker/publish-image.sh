#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
image="${1:-}"
mode="${2:---dry-run}"
registry="${CARINA_REGISTRY:-}"

fail() {
    echo "FAIL: $*" >&2
    exit 1
}

usage() {
    echo "usage: CARINA_REGISTRY=<host/repository> publish-image.sh <local-image> [--dry-run|--push]" >&2
    exit 64
}

if [ -z "${image}" ]; then
    usage
fi

case "${mode}" in
    --dry-run | --push) ;;
    *) usage ;;
esac

if [ -z "${registry}" ]; then
    fail "CARINA_REGISTRY names the repository the two streams are published to, for example ghcr.io/owner/carina; set it."
fi

insecure_flag() {
    case "$1" in
        localhost:* | 127.0.0.1:*) printf '%s' --insecure ;;
        *) printf '%s' '' ;;
    esac
}

unreadable_reason=

remote_state() {
    local ref="$1"
    local -a flags=()
    local flag
    local output
    local status

    flag="$(insecure_flag "${ref}")"
    unreadable_reason=

    if [ -n "${flag}" ]; then
        flags=("${flag}")
    fi

    set +e
    output="$(docker manifest inspect "${flags[@]}" "${ref}" 2>&1)"
    status=$?
    set -e

    if [ "${status}" -eq 0 ]; then
        printf 'present'
        return
    fi

    if grep -qiE 'manifest unknown|manifest_unknown|no such manifest|not found|repository name not known' <<<"${output}"; then
        printf 'absent'
        return
    fi

    unreadable_reason="${output}"
    printf 'unreadable'
}

require_local_image() {
    if ! docker image inspect "${image}" >/dev/null 2>&1; then
        fail "the image '${image}' is not built; there is nothing to publish."
    fi
}

publish_stream() {
    local stream="$1"
    local tag
    local ref
    local state

    tag="$("${repo_root}/docker/image-tags.sh" tag "${stream}")"
    ref="${registry}:${tag}"
    state="$(remote_state "${ref}")"

    if [ "${state}" = present ]; then
        echo "skip ${ref}: already published. The tag is immutable (BR-D-005), so this build is not pushed over it."
        return
    fi

    if [ "${state}" = unreadable ] && [ "${mode}" = --push ]; then
        fail "could not tell whether ${ref} exists, so pushing would risk writing over an immutable tag: ${unreadable_reason}"
    fi

    if [ "${state}" = unreadable ]; then
        echo "cannot tell whether ${ref} exists without credentials for the registry, so this plan cannot say whether it would be pushed or skipped: ${unreadable_reason}"
        return
    fi

    if [ "${mode}" = --dry-run ]; then
        echo "would push ${ref}: absent from the registry."
        return
    fi

    require_local_image
    docker tag "${image}" "${ref}"
    docker push "${ref}"

    if [ "$(remote_state "${ref}")" != present ]; then
        fail "pushed ${ref}, but the registry does not report it; treat this build as unpublished."
    fi

    echo "published ${ref}."
}

publish_stream driver
publish_stream app
