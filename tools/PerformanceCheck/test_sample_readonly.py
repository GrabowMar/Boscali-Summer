"""Offline client-contract tests: no sockets, game process, native calls or compilation."""
import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import sys
import tempfile
import types
import unittest
from unittest.mock import patch


class SamplerReadFailureTests(unittest.TestCase):
    def collect(self, fail_field=None):
        spec = importlib.util.spec_from_file_location("readonly_sampler", Path(__file__).with_name("sample-readonly.py"))
        sampler = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(sampler)
        clock = [0.0]

        def sleep(seconds):
            clock[0] += seconds

        class FakeOpener:
            def open(inner, request, timeout):
                route = request.full_url.split(":9", 1)[1]
                if route == "/health":
                    self.assertEqual(request.get_method(), "GET")
                    result = {"mission_running": True}
                elif route == "/batch":
                    self.assertEqual(request.get_method(), "POST")
                    queries = json.loads(request.data)["requests"]
                    self.assertEqual([q["path"] for q in queries], ["/read"] * len(sampler.FIELDS) + ["/perf"])
                    self.assertTrue(all("body" not in q and q.get("method", "GET") == "GET" for q in queries))
                    results = []
                    for name in sampler.FIELDS:
                        results.append({"status": 404, "body": {"error": "injected missing member"}} if name == fail_field
                                       else {"status": 200, "body": {"value": clock[0] if name in ("realtime", "mission_time") else 1}})
                    results.append({"status": 200, "body": {"gc_memory_bytes": 1024, "gc_collections": {"gen0": 0}}})
                    result = {"results": results}
                elif route == "/telemetry?mod=boscalisummer":
                    self.assertEqual(request.get_method(), "GET")
                    result = {"mods": []}
                else:
                    self.fail("unexpected runtime route: " + route)
                return io.BytesIO(json.dumps(result).encode())

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            state = root / "instances" / "offline-check"
            state.mkdir(parents=True)
            (state / "runtime.json").write_text(json.dumps({"port": 9, "token": "offline-test-only"}))
            (state / "proc.json").write_text(json.dumps({"pid": 4242}))
            output = root / "sample.json"
            argv = ["sample-readonly.py", "--inst", "offline-check", "--phase", "offline", "--out", str(output),
                    "--seconds", "0.51", "--interval", "0.25"]
            with patch.dict(os.environ, {"NOMOD_HOME": str(root)}), patch.object(sys, "argv", argv), \
                 patch.object(sampler.urllib.request, "build_opener", return_value=FakeOpener()), \
                 patch.object(sampler, "time", types.SimpleNamespace(monotonic=lambda: clock[0], sleep=sleep)), \
                 patch.object(sampler, "foreground", return_value={"available": False}), \
                 patch.object(sampler, "process_cpu", return_value=0), contextlib.redirect_stdout(io.StringIO()):
                exit_code = 0
                try:
                    sampler.main()
                except SystemExit as error:
                    exit_code = error.code
            return exit_code, json.loads(output.read_text()), sampler.FIELDS

    def test_successful_read_only_batch(self):
        exit_code, report, _ = self.collect()
        self.assertEqual(exit_code, 0)
        self.assertEqual(report["summary"]["errors"], 0)
        self.assertEqual(report["summary"]["samples"], 3)

    def test_each_missing_field_fails_and_retains_diagnostic(self):
        _, _, fields = self.collect()
        for field in fields:
            with self.subTest(field=field):
                exit_code, report, _ = self.collect(field)
                self.assertEqual(exit_code, 1)
                self.assertEqual(report["summary"]["errors"], 3)
                self.assertEqual(report["errors"][0]["field"], field)
                self.assertEqual(report["samples"][0][field + "_error"]["error"], "injected missing member")


if __name__ == "__main__":
    unittest.main()
