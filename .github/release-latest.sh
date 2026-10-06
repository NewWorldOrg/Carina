#!/bin/sh
set -eu

fail() {
  echo "$*" >&2
  exit 1
}

answer() {
  release="$1"

  case "${release}" in
    *-*)
      echo "${release} is a prerelease"
      return
      ;;
  esac

  newest="$(grep -v -- '-' | sort -V | tail -n 1)"

  if [ "${release}" = "${newest}" ]; then
    echo yes
  else
    echo "${release} is older than ${newest}"
  fi
}

expect() {
  said="$(printf '%s\n' $3 | answer "$1")"
  [ "${said}" = "$2" ] || fail "${1} among ${3}: expected '${2}' and the answer was '${said}'"
  echo "${1} among ${3}: ${said}"
}

prove() {
  expect v0.1.2 yes "v0.1.0 v0.1.1 v0.1.2"
  expect v0.1.1 "v0.1.1 is older than v0.1.2" "v0.1.0 v0.1.1 v0.1.2"
  expect v0.2.0-rc.1 "v0.2.0-rc.1 is a prerelease" "v0.1.2 v0.2.0-rc.1"
  expect v0.2.0 yes "v0.1.2 v0.2.0-rc.1 v0.2.0-rc.2 v0.2.0"
  expect v0.1.3 yes "v0.1.2 v0.1.3 v0.2.0-rc.1"
  expect v0.10.0 yes "v0.9.0 v0.10.0"
}

case "${1-}" in
  answer) answer "${2:?the release, such as v0.1.0}" ;;
  prove) prove ;;
  *) fail "usage: $0 answer <release> (the version tags on standard input) | prove" ;;
esac
