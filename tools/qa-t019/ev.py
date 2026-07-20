#!/usr/bin/env python3
"""Thin client for UnityScriptHost eval socket (127.0.0.1:48210).
Reads C# code from stdin (or -e "code"), sends one JSON line, prints the JSON reply."""
import socket, json, sys

def ev(code, port=48210, timeout=40):
    s = socket.create_connection(("127.0.0.1", port), timeout=6)
    s.settimeout(timeout)
    s.sendall((json.dumps({"code": code}) + "\n").encode())
    buf = b""
    while b"\n" not in buf:
        chunk = s.recv(65536)
        if not chunk:
            break
        buf += chunk
    s.close()
    return buf.decode(errors="replace").split("\n")[0]

if __name__ == "__main__":
    if len(sys.argv) >= 3 and sys.argv[1] == "-e":
        code = sys.argv[2]
    else:
        code = sys.stdin.read()
    out = ev(code)
    try:
        obj = json.loads(out)
        print(json.dumps(obj, indent=2))
    except Exception:
        print(out)
