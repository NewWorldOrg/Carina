#!/bin/sh
set -eu

: "${FEATURE_FILTER:?}"
: "${FEATURE_PROJECT:?}"

shard="${1:?the shard to print the filter of, counted from 1}"
count="${2:?how many shards the feature tests are split into}"

if [ "${shard}" -lt 1 ] || [ "${shard}" -gt "${count}" ]; then
  echo "shard ${shard} is not one of the ${count} shards" >&2
  exit 1
fi

project="$(dirname "${FEATURE_PROJECT}")"

classes="$(for folder in FeatureTest Feature; do
  if [ -d "${project}/${folder}" ]; then
    (cd "${project}" && grep -rHoE --include='*.cs' \
      '^[[:space:]]*public[[:space:]]+([a-z]+[[:space:]]+)*class[[:space:]]+[A-Za-z_][A-Za-z0-9_]*' \
      "${folder}")
  fi
done \
  | awk '{ folder = $1; sub(/\/[^\/]*$/, "", folder); sub(/^FeatureTest(\/.*)?$/, "FeatureTest", folder); gsub(/\//, ".", folder); print folder "." $NF }' \
  | LC_ALL=C sort -u)"

if [ -z "${classes}" ]; then
  echo "no public class was found beside ${FEATURE_PROJECT}, so every shard would select nothing" >&2
  exit 1
fi

picked=""

for class in ${classes}; do
  hash="$(printf '%s' "${class##*.}" | cksum | cut -d ' ' -f 1)"

  if [ $((hash % count + 1)) -eq "${shard}" ]; then
    picked="${picked:+${picked}|}FullyQualifiedName~.${class}."
  fi
done

if [ -z "${picked}" ]; then
  echo "shard ${shard} of ${count} was handed no class at all" >&2
  exit 1
fi

printf '(%s)&(%s)\n' "${FEATURE_FILTER}" "${picked}"
