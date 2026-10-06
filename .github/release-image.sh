#!/bin/sh
set -eu

repository="${1:?the repository the image is in}"
published="${2:?the tag this commit was already published under}"
release="${3:?the release, such as v0.1.0}"
prefix="${4-}"

fail() {
  echo "$*" >&2
  exit 1
}

digest_of() {
  docker buildx imagetools inspect --format '{{.Manifest.Digest}}' "$1" 2>&1
}

if ! digest="$(digest_of "${repository}:${published}")"; then
  case "${digest}" in
    *": not found"*) fail "${repository}:${published} is not in the registry yet. The image workflow publishes it once this commit is on master, so run this job again after that has finished." ;;
    *) fail "whether ${repository}:${published} is there could not be told: ${digest}" ;;
  esac
fi

name() {
  docker buildx imagetools create --prefer-index=false --tag "$1" "${repository}@${digest}"
  named="$(digest_of "$1")" || fail "$1 was pushed but cannot be read back: ${named}"
  [ "${named}" = "${digest}" ] || fail "$1 was pushed but names ${named}, not ${digest}"
  echo "$1 names ${digest}, the image of ${published}."
}

pinned="${repository}:${prefix}${release}"

if there="$(digest_of "${pinned}")"; then
  [ "${there}" = "${digest}" ] || fail "${pinned} is already there as ${there}, not ${digest}, and a version tag is never moved"
  echo "${pinned} is already there and is left as it is."
else
  case "${there}" in
    *": not found"*) name "${pinned}" ;;
    *) fail "whether ${pinned} is there could not be told: ${there}" ;;
  esac
fi

takes_latest="$(git tag --list 'v*' | "$(dirname "$0")/release-latest.sh" answer "${release}")"

if [ "${takes_latest}" = yes ]; then
  name "${repository}:${prefix}latest"
else
  echo "${takes_latest}, so ${repository}:${prefix}latest stays where it is."
fi
