#!/usr/bin/env bash
set -euo pipefail

scenario_id="09-fail-closed"
. "$(cd "$(dirname "$0")" && pwd)/lib.sh"

heading "受入基準 9 — everything the app publishes is denied until authentication exists"

stack_up

set +e
published="$(stack port app 8080 2>&1)"
port_status=$?
set -e

if [ "${port_status}" -ne 0 ] || [ -z "${published}" ]; then
    fail "the app's published port could not be found: ${published}"
fi

port="${published##*:}"
require_number "the app's published port" "${port}"
base="http://127.0.0.1:${port}"
note "the app answers on ${base}"

status_of() {
    local status

    set +e
    text_value="$(curl -s -o /dev/null -w '%{http_code}' --max-time 10 "$@" 2>&1)"
    status=$?
    set -e

    if [ "${status}" -ne 0 ]; then
        fail "asking ${*} failed at the transport (curl ${status}): ${text_value}"
    fi
}

status_of "${base}/api/health"
if [ "${text_value}" != "200" ]; then
    fail "the health probe answered ${text_value}; if it is denied too, this scenario cannot tell fail-closed from a dead app."
fi
pass "the health probe is the one anonymous endpoint and answers 200"

set +e
document="$(cat "${repo_root}/openapi/Carina.Api.json" 2>&1)"
document_status=$?
set -e

if [ "${document_status}" -ne 0 ]; then
    fail "the published contract could not be read: ${document}"
fi

json "${document}" openapi-paths
paths="${text_value}"

checked=0
for path in ${paths}; do
    if [ "${path}" = "/api/health" ]; then
        continue
    fi

    status_of "${base}${path}"

    if [ "${text_value}" != "401" ]; then
        fail "${path} answered ${text_value} without credentials; the default-deny seam has a hole in it."
    fi

    checked=$((checked + 1))
    note "${path} -> 401"
done

if [ "${checked}" -lt 1 ]; then
    fail "the published contract yielded no endpoint besides health, so nothing was actually checked."
fi
pass "every endpoint the published contract declares, ${checked} of them, is denied without credentials"

status_of "${base}/openapi/v1.json"
if [ "${text_value}" != "401" ]; then
    fail "the OpenAPI document answered ${text_value}; the contract is readable from a running deployment."
fi
pass "the OpenAPI document itself is denied (401)"

status_of "${base}/api/nothing-here"
if [ "${text_value}" != "401" ]; then
    fail "an unknown path answered ${text_value}; routing is resolving before the seam denies."
fi
pass "an unknown path is denied rather than described (401)"

status_of -X POST "${base}/api/health"
if [ "${text_value}" != "401" ]; then
    fail "POST on the anonymous health path answered ${text_value}; the anonymous exception is wider than one endpoint."
fi
pass "the anonymous exception is exactly one method on one path: POST /api/health is denied (401)"
