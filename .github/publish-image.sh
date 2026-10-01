#!/bin/sh
set -eu

built="${1:?the image already built on this machine}"
repository="${2:?the repository the tags are pushed to}"
shift 2

if [ "$#" -lt 1 ]; then
  echo "no tag was given, so nothing would be pushed and the job would be green having published nothing" >&2
  exit 1
fi

missing=""

for tag in "$@"; do
  if [ -z "${tag}" ]; then
    echo "one of the tags is empty, so the numbering did not answer and nothing is pushed" >&2
    exit 1
  fi

  reference="${repository}:${tag}"

  if answer="$(docker buildx imagetools inspect "${reference}" 2>&1)"; then
    echo "${reference} is already there and is left as it is."
    continue
  fi

  case "${answer}" in
    *": not found"*) missing="${missing:+${missing} }${reference}" ;;
    *)
      echo "whether ${reference} is already there could not be told, so nothing is pushed:" >&2
      echo "${answer}" >&2
      exit 1
      ;;
  esac
done

for reference in ${missing}; do
  docker tag "${built}" "${reference}"
  docker push --quiet "${reference}"
  echo "${reference} pushed."
done
