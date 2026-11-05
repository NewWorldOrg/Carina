#!/bin/sh
set -eu

reference="${1:?the tag in the registry, such as ghcr.io/owner/carina:sha-0123456789ab}"
what="${2:?'there' to ask whether the tag is already in the registry, or 'push'}"
built="${3:-}"

fail() {
  echo "$*" >&2
  exit 1
}

case "${what}" in
  there)
    if answer="$(docker buildx imagetools inspect "${reference}" 2>&1)"; then
      echo "${reference} is already in the registry and is left as it is." >&2
      echo yes
      exit 0
    fi

    case "${answer}" in
      *": not found"*) echo no ;;
      *)
        echo "whether ${reference} is in the registry could not be told, so nothing is pushed:" >&2
        fail "${answer}"
        ;;
    esac
    ;;
  push)
    [ -n "${built}" ] || fail "push needs the name the image was built under on this machine"
    docker tag "${built}" "${reference}"
    docker push --quiet "${reference}"
    echo "${reference} pushed."
    ;;
  *)
    fail "'${what}' is neither there nor push"
    ;;
esac
