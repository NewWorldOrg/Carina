import errno
import socket
import sys


def main(path):
    client = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
    client.settimeout(5)

    try:
        client.connect(path)
    except OSError as failure:
        print(errno.errorcode.get(failure.errno, str(failure.errno)))
        return 0
    finally:
        client.close()

    print("connected")
    return 0


sys.exit(main(sys.argv[1]))
