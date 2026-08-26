#!/bin/sh
set -eu

results="${1:?the directory dotnet test wrote its trx files to}"

unescaped() {
  sed 's/&lt;/</g; s/&gt;/>/g; s/&quot;/"/g; s/&apos;/'"'"'/g; s/&amp;/\&/g'
}

if [ ! -d "${results}" ]; then
  echo "there is no ${results} directory: the run fell over before a single test was started"
  exit 0
fi

files="$(find "${results}" -name '*.trx' | LC_ALL=C sort)"
count="$(printf '%s' "${files}" | grep -c . || true)"

if [ "${count}" -eq 0 ]; then
  echo "no result file was written to ${results}: the run fell over before a single test was started"
  exit 0
fi

echo "read ${count} result files from ${results}."

named="$(cat ${files} \
  | tr '<' '\n' \
  | grep '^UnitTestResult ' \
  | grep 'outcome="Failed"' \
  | grep -o 'testName="[^"]*"' \
  | sed 's/^testName="//; s/"$//' \
  | unescaped \
  | LC_ALL=C sort -u || true)"

reported="$(cat ${files} \
  | tr '<' '\n' \
  | sed -n 's/^Text>\[xUnit\.net [^]]*\][[:space:]]*\(.*\) \[FAIL\]$/\1/p' \
  | unescaped \
  | LC_ALL=C sort -u || true)"

unfinished=""

for file in ${files}; do
  reason="$(tr '<' '\n' < "${file}" \
    | sed -n 's/^Text>\(The active test run was aborted.*\)$/\1/p' \
    | head -1 \
    | unescaped || true)"

  if [ -z "${reason}" ]; then
    continue
  fi

  assembly="$(grep -o 'codeBase="[^"]*"' "${file}" | head -1 | sed 's/^codeBase="//; s/"$//' || true)"
  unfinished="${unfinished}$(basename "${assembly:-${file}}"): ${reason}
"
done

if [ -n "${unfinished}" ]; then
  echo "these runs never finished, so their counters record no failure however many there were:"
  printf '%s' "${unfinished}"
fi

if [ -n "${named}" ]; then
  echo "these tests are recorded as failed:"
  echo "${named}"
  exit 0
fi

if [ -n "${reported}" ]; then
  echo "no result row is marked failed, so these names come from the run log the results carry:"
  echo "${reported}"
  exit 0
fi

echo "a step before this one failed, yet none of the ${count} result files in ${results} names a"
echo "failed test and none carries a run log that does."
exit 1
