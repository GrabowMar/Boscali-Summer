"""Standard-library protocol check; fake inbox consumer, no running game required."""
import importlib.util
import json
from pathlib import Path
import tempfile
import threading
import time
import unittest

spec = importlib.util.spec_from_file_location("cinematic_client", Path(__file__).resolve().parents[2] / "tools/Cinematic-Command.py")
client = importlib.util.module_from_spec(spec)
spec.loader.exec_module(client)


class ClientTests(unittest.TestCase):
    def test_request_reply_and_timeout(self):
        with tempfile.TemporaryDirectory(prefix="CinematicClient-") as folder:
            root = Path(folder)
            def consume():
                until = time.monotonic() + 3
                while time.monotonic() < until:
                    requests = list((root / "Inbox").glob("*.json"))
                    if requests:
                        request = requests[0]
                        self.assertEqual(json.loads(request.read_text())["action"], "schema")
                        (root / "Outbox").mkdir(exist_ok=True)
                        (root / "Outbox" / request.name).write_text('{"ok":true,"api_version":1}')
                        return
                    time.sleep(.02)
                raise AssertionError("No atomic command received")
            worker = threading.Thread(target=consume)
            worker.start()
            self.assertTrue(client.send(root, {"action": "schema"}, 2)["ok"])
            worker.join()
            result = client.send(root, {"action": "capture"}, .1)
            self.assertIn("TIMEOUT_AMBIGUOUS", result["error"])
            self.assertTrue((root / "Inbox" / (result["request_id"] + ".json")).exists())
            with self.assertRaises(ValueError):
                client.send(root, {"action": "seek", "seconds": float("nan")})


if __name__ == "__main__":
    unittest.main()
