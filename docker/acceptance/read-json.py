import json
import sys


def render(value):
    if isinstance(value, bool):
        return "true" if value else "false"
    if value is None:
        return "null"
    if isinstance(value, (dict, list)):
        return json.dumps(value, sort_keys=True)
    return str(value)


def walk(document, keys):
    current = document
    for key in keys:
        if isinstance(current, list):
            current = current[int(key)]
        else:
            current = current[key]
    return current


def session(document, wanted, keys):
    for entry in document:
        if entry.get("sessionId") == wanted:
            return walk(entry, keys)
    raise KeyError(f"no session called {wanted} among {[e.get('sessionId') for e in document]}")


def diagnostics(document, reason, session_id):
    matches = [
        entry
        for entry in document
        if entry.get("reason") == reason
        and (session_id is None or entry.get("sessionId") == session_id)
    ]
    return matches


def main(argv):
    document = json.load(sys.stdin)
    command = argv[0]
    rest = argv[1:]

    if command == "field":
        print(render(walk(document, rest)))
        return 0

    if command == "session":
        print(render(session(document, rest[0], rest[1:])))
        return 0

    if command == "sessions":
        print(" ".join(sorted(entry.get("sessionId", "") for entry in document)))
        return 0

    if command == "tuner":
        for entry in document:
            if entry.get("deviceId") == rest[0]:
                print(render(walk(entry, rest[1:])))
                return 0
        raise KeyError(f"no tuner called {rest[0]}")

    if command == "diagnostics-count":
        wanted = rest[1] if len(rest) > 1 else None
        print(len(diagnostics(document, rest[0], wanted)))
        return 0

    if command == "diagnostics-detail":
        wanted = rest[1] if len(rest) > 1 else None
        found = diagnostics(document, rest[0], wanted)
        if not found:
            raise KeyError(f"no diagnostic with reason {rest[0]} among {[e.get('reason') for e in document]}")
        print(render(found[0].get("detail")))
        return 0

    if command == "has-key":
        print("true" if rest[0] in document else "false")
        return 0

    if command == "openapi-paths":
        for path in sorted(document.get("paths", {})):
            print(path)
        return 0

    raise SystemExit(f"unknown query '{command}'")


sys.exit(main(sys.argv[1:]))
