#!/usr/bin/env bash
set -euo pipefail

scenario_id="08-backpressure"
. "$(cd "$(dirname "$0")" && pwd)/lib.sh"

session="acc08"
volume="${project}_recordings"
reader="${prefix}-slow-reader"
window="${CARINA_ACCEPTANCE_BACKPRESSURE_WINDOW:-3}"

heading "a live reader that cannot keep up does not slow the recording down"

stack_up

start_recording "${session}" fake-terrestrial primary 240

if ! wait_until 30 bash -c "docker run --rm -v ${volume}:/rec ${python_image} test -s /rec/${session}.ts"; then
    fail "the recording file never appeared, so there was nothing for a reader to fall behind."
fi

measure_rate() {
    local first
    local second
    local began
    local ended

    began="$(date +%s%N)"
    recording_size "${volume}" "${session}.ts"
    first="${number_value}"

    sleep "${window}"

    recording_size "${volume}" "${session}.ts"
    second="${number_value}"
    ended="$(date +%s%N)"

    number_value=$(((second - first) / ((ended - began) / 1000000000)))
}

measure_rate
undisturbed="${number_value}"
require_number "the undisturbed recording rate" "${undisturbed}"

if [ "${undisturbed}" -le 0 ]; then
    fail "the recording was not growing before the reader arrived; there is no rate to compare against."
fi
pass "the recording writes ${undisturbed} bytes per second with nobody reading"

docker run -d --name "${reader}" --user 100:10001 -v "${socket_volume}:/run/carina" "${curl_image}" \
    -s --limit-rate 50k --max-time 300 --unix-socket /run/carina/driver.sock \
    "http://localhost/sessions/${session}/stream?as=viewer" -o /dev/null >/dev/null
track_container "${reader}"
note "a viewer limited to 50 kB/s is now attached to the same session"

sleep 3

inspect_field "${reader}" '{{.State.Running}}'
if [ "${text_value}" != "true" ]; then
    fail "the slow reader is not running: $(docker logs "${reader}" 2>&1)"
fi

measure_rate
disturbed="${number_value}"
require_number "the recording rate with a slow reader attached" "${disturbed}"

floor=$((undisturbed / 2))

if [ "${disturbed}" -lt "${floor}" ]; then
    fail "the recording fell from ${undisturbed} to ${disturbed} bytes per second while a 50 kB/s reader was attached; the live path is pushing back on the file."
fi
pass "the recording writes ${disturbed} bytes per second with the slow reader attached (floor was ${floor})"

driver_get /sessions
json "${reply}" session "${session}" droppedChunks
dropped="${text_value}"
require_number "the number of chunks dropped for the slow reader" "${dropped}"

if [ "${dropped}" -lt 1 ]; then
    fail "the driver dropped nothing for the reader, so the reader was keeping up and this scenario never applied any backpressure."
fi
pass "the reader really could not keep up: ${dropped} chunks were dropped for it"

json "${reply}" session "${session}" counters drops
drops="${text_value}"
require_number "the recording's dropped packet count" "${drops}"

json "${reply}" session "${session}" counters discontinuities
discontinuities="${text_value}"
require_number "the recording's discontinuity count" "${discontinuities}"

if [ "${drops}" -ne 0 ] || [ "${discontinuities}" -ne 0 ]; then
    fail "the recording measured ${drops} drops and ${discontinuities} discontinuities while the reader was falling behind."
fi
pass "the recording itself measured no drops and no discontinuities"

docker rm -f "${reader}" >/dev/null 2>&1 || true

stop_recording "${session}"

if ! wait_until 60 bash -c "docker run --rm --user 100:10001 -v ${socket_volume}:/run/carina ${curl_image} -s --max-time 15 --unix-socket /run/carina/driver.sock http://localhost/sessions | python3 ${acceptance_dir}/read-json.py session ${session} concluded | grep -qx true"; then
    fail "the recording never concluded after it was asked to stop."
fi

driver_get /sessions
json "${reply}" session "${session}" state
if [ "${text_value}" != "stopped" ]; then
    fail "the recording ended in state '${text_value}' after carrying a slow reader: ${reply}"
fi

json "${reply}" session "${session}" bytesRecorded
final_bytes="${text_value}"
require_number "the driver's final byte count" "${final_bytes}"

recording_size "${volume}" "${session}.ts"
if [ "${number_value}" -ne "${final_bytes}" ]; then
    fail "the file is ${number_value} bytes but the driver counted ${final_bytes} bytes into it; the tail was lost."
fi

check_continuity "${volume}" "${session}.ts"
pass "the recording that carried the slow reader is byte-continuous and exactly ${final_bytes} bytes"
