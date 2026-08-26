#!/bin/sh
set -eu

results="${1:?the directory dotnet test wrote its trx files to}"

named="$(cat "${results}"/*.trx \
  | tr '<' '\n' \
  | grep 'outcome="Failed"' \
  | grep -o 'testName="[^"]*"' \
  | sed 's/^testName="//; s/"$//' \
  | sed 's/&lt;/</g; s/&gt;/>/g; s/&quot;/"/g; s/&apos;/'"'"'/g; s/&amp;/\&/g' \
  | LC_ALL=C sort -u || true)"

if [ -z "${named}" ]; then
  echo "nothing in ${results} is marked failed: the run fell over outside the tests themselves" >&2
  exit 0
fi

echo "these tests failed:"
echo "${named}"
