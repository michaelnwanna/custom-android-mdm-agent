import contextlib
import hashlib
import io
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
from unittest.mock import patch

import check_postgres
import check_backend
import install_postgres_lab


class PostgresRunnerTests(unittest.TestCase):
    def run_scenario(self, backend_passes=True, binaries=True, shutdown_succeeds=True):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        binary_directory = root / "postgres/bin"
        binary_directory.mkdir(parents=True)
        if binaries:
            for executable in ("initdb", "pg_ctl", "psql", "postgres", "pg_config"):
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
                self.assertIn("--postgres-restart", command)
                self.assertEqual(kwargs["env"]["FLEET_TEST_POSTGRES_BIN"], str(binary_directory.resolve()))
                data = Path(kwargs["env"]["FLEET_TEST_POSTGRES_DATA"])
                self.assertTrue((data.parent / "restart-enabled").is_file())
                (data / "restart.log").write_text("test restart output")
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
        if binaries:
            self.assertEqual((manifest.parent / "restart.log").read_text(), "test restart output")
        return exit_code, json.loads(manifest.read_text()), socket_directory

    def test_success_requires_backend_pass_and_stopped_cluster(self):
        exit_code, report, socket_directory = self.run_scenario()
        self.assertEqual(exit_code, 0)
        self.assertEqual(report["result"], "passed")
        self.assertTrue(report["serverStopped"])
        self.assertFalse(socket_directory.exists())
        self.assertEqual(report["commands"][-1]["argv"][-1], "stop")
        self.assertEqual(report["binarySha256"], {
            executable: hashlib.sha256(b"").hexdigest()
            for executable in ("initdb", "pg_ctl", "psql", "postgres", "pg_config")
        })
        configure = next(entry["argv"] for entry in report["commands"] if Path(entry["argv"][0]).name == "pg_config")
        self.assertEqual(configure[1:], ["--configure"])
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


class BackendSelectionTests(unittest.TestCase):
    def run_scenario(self, restart=False, restart_result=True):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "backend").mkdir()
            (root / "tools").mkdir()
            for filename in ("check_backend.py", "check_postgres.py", "install_postgres_lab.py", "test_postgres_tools.py"):
                shutil.copyfile(Path(__file__).parent / filename, root / "tools" / filename)
            commands = []

            def run(command, **kwargs):
                commands.append(command)
                if command[:2] == ["dotnet", "test"]:
                    results = Path(command[command.index("--results-directory") + 1])
                    results.mkdir(parents=True)
                    name = ("FleetManagement.Postgres.Tests.EnrollmentRestartTests.CommittedClaimSurvivesFastPostgresRestart"
                            if restart_result else "FleetManagement.Postgres.Tests.EnrollmentClaimTests.RealPostgresMigrationConcurrencyRetryExpiryAndRollback")
                    (results / "tests.trx").write_text(
                        '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
                        f'<Results><UnitTestResult testName="{name}" outcome="Passed" /></Results>'
                        '<ResultSummary><Counters total="1" passed="1" /></ResultSummary></TestRun>')
                return subprocess.CompletedProcess(command, 0, "test command output", "")

            args = ["check_backend.py", "--postgres"] + (["--postgres-restart"] if restart else [])
            with patch.object(check_backend, "ROOT", root), patch("sys.argv", args), \
                    patch.object(check_backend.subprocess, "run", side_effect=run), \
                    contextlib.redirect_stdout(io.StringIO()):
                exit_code = check_backend.main()
            manifest = next((root / "artifacts/backend").glob("*/manifest.json"))
            return exit_code, json.loads(manifest.read_text()), commands

    def test_external_database_excludes_restart(self):
        exit_code, report, commands = self.run_scenario(restart_result=False)
        self.assertEqual(exit_code, 0)
        test = next(command for command in commands if command[:2] == ["dotnet", "test"])
        self.assertEqual(test[-2:], ["--filter", "FullyQualifiedName!~EnrollmentRestartTests"])
        self.assertFalse(report["restartVerified"])

    def test_managed_restart_requires_named_passing_test(self):
        exit_code, report, commands = self.run_scenario(restart=True)
        self.assertEqual(exit_code, 0)
        self.assertTrue(report["restartVerified"])
        test = next(command for command in commands if command[:2] == ["dotnet", "test"])
        self.assertNotIn("--filter", test)

    def test_missing_restart_result_cannot_pass(self):
        exit_code, report, _ = self.run_scenario(restart=True, restart_result=False)
        self.assertEqual(exit_code, 1)
        self.assertEqual(report["result"], "failed")
        self.assertFalse(report["restartVerified"])

    def test_restart_flag_without_postgres_is_rejected(self):
        with patch("sys.argv", ["check_backend.py", "--postgres-restart"]), \
                contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit) as rejected:
            check_backend.main()
        self.assertEqual(rejected.exception.code, 2)


if __name__ == "__main__":
    unittest.main()