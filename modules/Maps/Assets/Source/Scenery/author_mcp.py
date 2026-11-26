"""Run the scenery authoring script in Blender Lab's local MCP bridge."""

import argparse
import json
from pathlib import Path
import socket
import subprocess
import time


def connect(process, port, deadline):
    while time.monotonic() < deadline:
        if process.poll() is not None:
            raise RuntimeError(f"Blender exited with code {process.returncode}")
        try:
            return socket.create_connection(("127.0.0.1", port), timeout=.3)
        except OSError:
            time.sleep(.25)
    raise TimeoutError(f"Blender MCP bridge never accepted a connection on port {port}")


def request(connection, code):
    connection.settimeout(180)
    command = {"type": "execute", "strict_json": True, "code": code}
    connection.sendall(json.dumps(command).encode() + b"\0")
    response = bytearray()
    while not response.endswith(b"\0"):
        part = connection.recv(65536)
        if not part:
            raise RuntimeError("Blender MCP bridge closed before returning a result")
        response.extend(part)
    result = json.loads(response[:-1])
    if result["status"] != "ok":
        raise RuntimeError(result["message"])
    return result["result"]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("blender", type=Path)
    parser.add_argument("stock_meshes", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--previews", type=Path)
    parser.add_argument("--port", default=19879, type=int)
    args = parser.parse_args()
    source = Path(__file__).with_name("author_scenery.py").resolve()
    paths = (str(args.stock_meshes.resolve()), str(args.output.resolve()),
             str(args.previews.resolve()) if args.previews else None)
    code = (f"namespace = {{'__name__': 'scenery_authoring'}}\n"
            f"exec(compile(open({str(source)!r}, encoding='utf-8').read(), {str(source)!r}, 'exec'), namespace)\n"
            f"result = namespace['author'](*{paths!r})")
    with args.output.with_suffix(".blender.log").open("wb") as log:
        process = subprocess.Popen([str(args.blender), "--command", "blender_mcp", "--host", "127.0.0.1",
                                    "--port", str(args.port)], stdout=log, stderr=subprocess.STDOUT)
        try:
            with connect(process, args.port, time.monotonic() + 20) as connection:
                print(json.dumps(request(connection, code), indent=2))
        finally:
            if process.poll() is None:
                process.terminate()
                process.wait(timeout=10)


if __name__ == "__main__":
    main()
