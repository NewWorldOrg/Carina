import sys

PACKET_LENGTH = 188
BATCH = PACKET_LENGTH * 5000
BLIND_SPOT = 256 * PACKET_LENGTH


def inspect(path):
    problems = []
    packets = 0
    total = 0
    previous_cc = None
    previous_seed = None

    with open(path, "rb") as handle:
        while True:
            chunk = handle.read(BATCH)
            if not chunk:
                break
            total += len(chunk)
            for start in range(0, len(chunk) - PACKET_LENGTH + 1, PACKET_LENGTH):
                packet = chunk[start : start + PACKET_LENGTH]
                if packet[0] != 0x47:
                    problems.append(f"packet {packets}: sync byte {packet[0]:#04x}")
                    return problems, packets, total
                cc = packet[3] & 0x0F
                seed = packet[4]
                if previous_cc is not None and cc != (previous_cc + 1) % 16:
                    problems.append(
                        f"packet {packets}: continuity counter {previous_cc} -> {cc}"
                    )
                if previous_seed is not None and seed != (previous_seed + 1) % 256:
                    problems.append(
                        f"packet {packets}: payload counter {previous_seed} -> {seed}"
                    )
                previous_cc = cc
                previous_seed = seed
                packets += 1
            if len(problems) > 5:
                return problems, packets, total

    return problems, packets, total


def main():
    if len(sys.argv) != 2:
        print("usage: check-recording-continuity.py <recording.ts>", file=sys.stderr)
        return 64

    path = sys.argv[1]
    problems, packets, total = inspect(path)
    trailing = total % PACKET_LENGTH

    print(f"file: {path}")
    print(f"bytes: {total} packets: {packets} trailing bytes: {trailing}")
    print("problems: " + ("none" if not problems else "; ".join(problems)))
    print(
        f"limits: this reads the synthetic tuner's own counters, so it cannot see a gap of "
        f"exactly N x {BLIND_SPOT} bytes, and it cannot tell a truncated tail from a short recording."
    )

    return 1 if problems or trailing else 0


sys.exit(main())
