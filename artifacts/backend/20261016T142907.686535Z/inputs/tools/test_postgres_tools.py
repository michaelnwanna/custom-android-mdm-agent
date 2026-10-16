import contextlib
import io
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

import check_postgres
import install_postgres_lab


class PostgresRunnerTests(unittest.TestCase):
    def run_scenario(self, backend_passes=True, binaries=True, shutdown_succeeds=True):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        binary_directory = root / "postgres/bin"
        binary_directory.mkdir(parents=True)
        if binaries:
            for executable in ("initdb", "pg_ctl", "psql", "postgres"):
                (binary_directory / executable).touch()
        socket_directory = root / "socket"
        socket_directory.mkdir()

        def run(command, **kwargs):
            executable = Path(command[0]).name
            if executable == "initdb":
                data = Path(command[command.index("-D") + 1])
                data.mkdir()
            if executable == "pg_ctl":
                data = Path(command[command.index("-D") + 1])
                if command[-1] == "start":
                    (data / "postmaster.pid").touch()
                if command[-1] == "stop":
                    if not shutdown_succeeds:
                        return subprocess.CompletedProcess(command, 1, "", "test shutdown failure")
                    (data / "postmaster.pid").unlink()
            if "--output-directory" in command:
                backend = Path(command[command.index("--output-directory") + 1])
                backend.mkdir()
                (backend / "manifest.json").write_text(json.dumps({"result": "passed" if backend_passes else "failed"}))
                return subprocess.CompletedProcess(command, 0 if backend_passes else 1, "backend result", "")
            if executable == "psql" and "SELECT count(*)" in command[-1]:
                return subprocess.CompletedProcess(command, 0, "0\n", "")
            return subprocess.CompletedProcess(command, 0, "test command output", "")

        with patch.object(check_postgres, "ROOT", root), patch.object(check_postgres.tempfile, "mkdtemp", return_value=str(socket_directory)), \
                patch.object(check_postgres.subprocess, "run", side_effect=run), \
                patch("sys.argv", ["check_postgres.py", "--bin-dir", str(binary_directory)]), \
                contextlib.redirect_stdout(io.StringIO()):
            exit_code = check_postgres.main()
        manifest = next((root / "artifacts/postgres").glob("*/manifest.json"))
        return exit_code, json.loads(manifest.read_text()), socket_directory

    def test_success_requires_backend_pass_and_stopped_cluster(self):
        exit_code, report, socket_directory = self.run_scenario()
        self.assertEqual(exit_code, 0)
        self.assertEqual(report["result"], "passed")
        self.assertTrue(report["serverStopped"])
        self.assertFalse(socket_directory.exists())
        self.assertEqual(report["commands"][-1]["argv"][-1], "stop")
        initialization = next(entry["argv"] for entry in report["commands"] if Path(entry["argv"][0]).name == "initdb")
        self.assertIn("--auth-host=reject", initialization)
        startup = next(entry["argv"] for entry in report["commands"] if entry["argv"][-1] == "start")
        self.assertIn("listen_addresses=''", startup[startup.index("-o") + 1])
        self.assertIn("unix_socket_permissions=0700", startup[startup.index("-o") + 1])

    def test_backend_failure_still_stops_server_and_records_failure(self):
        exit_code, report, socket_directory = self.run_scenario(backend_passes=False)
        self.assertEqual(exit_code, 1)
        self.assertEqual(report["result"], "failed")
        self.assertTrue(report["serverStopped"])
        self.assertFalse(socket_directory.exists())
        self.assertEqual(report["commands"][-1]["argv"][-1], "stop")

    def test_missing_binaries_cannot_pass(self):
        exit_code, report, socket_directory = self.run_scenario(binaries=False)
        self.assertEqual(exit_code, 1)
        self.assertEqual(report["result"], "failed")
        self.assertEqual(report["commands"], [])
        self.assertFalse(socket_directory.exists())

    def test_shutdown_failure_cannot_pass_or_remove_live_socket(self):
        exit_code, report, socket_directory = self.run_scenario(shutdown_succeeds=False)
        self.assertEqual(exit_code, 1)
        self.assertEqual(report["result"], "failed")
        self.assertFalse(report["serverStopped"])
        self.assertTrue(socket_directory.exists())


class PostgresInstallerTests(unittest.TestCase):
    def test_bad_archive_is_rejected_before_extraction_or_compilation(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            archive = root / "invalid.tar.bz2"
            archive.write_bytes(b"not the PostgreSQL archive")
            with patch.object(install_postgres_lab, "ROOT", root), \
                    patch("sys.argv", ["install_postgres_lab.py", "--archive", str(archive)]), \
                    patch.object(install_postgres_lab.subprocess, "run") as run, \
                    contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(install_postgres_lab.main(), 1)
                run.assert_not_called()
            manifest = next((root / "artifacts/postgres-build").glob("*/manifest.json"))
            self.assertEqual(json.loads(manifest.read_text())["result"], "failed")

    def test_existing_installation_is_not_overwritten(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            prefix = root / ".lab-private/postgres-install" / install_postgres_lab.VERSION
            prefix.mkdir(parents=True)
            sentinel = prefix / "keep"
            sentinel.write_text("existing installation")
            with patch.object(install_postgres_lab, "ROOT", root), \
                    patch("sys.argv", ["install_postgres_lab.py"]), \
                    patch.object(install_postgres_lab.urllib.request, "urlopen") as download, \
                    contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(install_postgres_lab.main(), 1)
                download.assert_not_called()
            self.assertEqual(sentinel.read_text(), "existing installation")


if __name__ == "__main__":
    unittest.main()