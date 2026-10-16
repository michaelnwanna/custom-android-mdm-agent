import argparse
import datetime
import hashlib
import html
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile


ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description="Run recorded claims tests against a private, temporary PostgreSQL cluster.")
    parser.add_argument("--bin-dir", type=Path, default=ROOT / ".lab-private/postgres-install/17.11/bin")
    args = parser.parse_args()
    started = datetime.datetime.now(datetime.timezone.utc)
    stamp = started.strftime("%Y%m%dT%H%M%S.%fZ")
    output = ROOT / "artifacts/postgres" / stamp
    output.mkdir(parents=True, exist_ok=False)
    private = ROOT / ".lab-private/postgres" / stamp
    private.mkdir(parents=True, mode=0o700, exist_ok=False)
    data = private / "data"
    server_log = private / "server.log"
    socket_directory = Path(tempfile.mkdtemp(prefix="fleet-pg-"))
    records = []
    backend_bundle = output / "backend"
    success = False
    shutdown = False
    source = Path(__file__).resolve()
    shutil.copyfile(source, output / source.name)

    def execute(command, env=None, timeout=120):
        try:
            process = subprocess.run([str(value) for value in command], cwd=ROOT,
                                     env=env, text=True, capture_output=True, timeout=timeout)
            entry = {"argv": [str(value) for value in command], "returncode": process.returncode,
                     "stdout": process.stdout, "stderr": process.stderr}
        except subprocess.TimeoutExpired as error:
            entry = {"argv": [str(value) for value in command], "returncode": 124,
                     "stdout": (error.stdout or b"").decode(errors="replace"),
                     "stderr": (error.stderr or b"").decode(errors="replace") + "\nCommand timed out"}
        except OSError as error:
            entry = {"argv": [str(value) for value in command], "returncode": 127,
                     "stdout": "", "stderr": str(error)}
        records.append(entry)
        (output / f"command-{len(records):02d}.json").write_text(json.dumps(entry, indent=2) + "\n")
        print(entry["stdout"] + entry["stderr"], flush=True)
        if entry["returncode"]:
            raise RuntimeError(f"Recorded command failed with code {entry['returncode']}")
        return entry

    try:
        for executable in ("initdb", "pg_ctl", "psql", "postgres"):
            if not (args.bin_dir / executable).is_file():
                raise RuntimeError(f"Missing PostgreSQL executable: {args.bin_dir / executable}")
        execute([args.bin_dir / "postgres", "--version"])
        execute([args.bin_dir / "initdb", "-D", data, "--username=fleet_lab", "--encoding=UTF8",
                 "--locale=C", "--auth-local=trust", "--auth-host=reject"])
        execute([args.bin_dir / "pg_ctl", "-D", data, "-l", server_log, "-w", "-t", "60",
                 "-o", f"-c listen_addresses='' -c unix_socket_directories='{socket_directory}' -c unix_socket_permissions=0700",
                 "start"])
        execute([args.bin_dir / "psql", "-h", socket_directory, "-U", "fleet_lab", "-d", "postgres",
                 "-X", "-v", "ON_ERROR_STOP=1", "-c",
                 "SELECT version(); SHOW listen_addresses; SHOW unix_socket_permissions; SHOW fsync; SHOW synchronous_commit;"])
        env = os.environ.copy()
        env["FLEET_TEST_POSTGRES"] = f"Host={socket_directory};Database=postgres;Username=fleet_lab;Timeout=10;Command Timeout=30"
        execute([sys.executable, ROOT / "tools/check_backend.py", "--postgres", "--output-directory", backend_bundle],
                env=env, timeout=600)
        cleanup = execute([args.bin_dir / "psql", "-h", socket_directory, "-U", "fleet_lab", "-d", "postgres",
                   "-X", "-v", "ON_ERROR_STOP=1", "--tuples-only", "--no-align", "-c",
                   "SELECT count(*) FROM pg_namespace WHERE nspname ~ '^fleet_test_[0-9a-f]{32}$';"])
        if cleanup["stdout"].strip() != "0":
            raise RuntimeError("Integration test schemas remain after the database checks")
        success = json.loads((backend_bundle / "manifest.json").read_text())["result"] == "passed"
    except (RuntimeError, OSError, ValueError) as error:
        (output / "failure.txt").write_text(str(error) + "\n")
        print(str(error), flush=True)
    finally:
        if (data / "postmaster.pid").exists():
            try:
                execute([args.bin_dir / "pg_ctl", "-D", data, "-w", "-t", "60", "-m", "fast", "stop"])
            except RuntimeError:
                pass
        shutdown = not (data / "postmaster.pid").exists()
        if shutdown:
            shutil.rmtree(socket_directory)
        if server_log.exists():
            shutil.copyfile(server_log, output / "server.log")
    passed = success and shutdown
    report = {
        "schemaVersion": 1, "startedAt": started.isoformat(),
        "finishedAt": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "result": "passed" if passed else "failed", "serverStopped": shutdown,
        "privateDataDirectory": str(data), "socketDirectory": str(socket_directory),
        "commands": records, "scope": "Real local PostgreSQL enrollment claim integration; no signing, HTTP or power-loss guarantee",
        "fileSha256": {str(filename.relative_to(output)): hashlib.sha256(filename.read_bytes()).hexdigest()
                       for filename in sorted(output.rglob("*")) if filename.is_file()
                       and not {"bin", "obj"}.intersection(filename.relative_to(output).parts)},
    }
    (output / "manifest.json").write_text(json.dumps(report, indent=2) + "\n")
    details = "\n".join(entry["stdout"] + entry["stderr"] for entry in records)
    (output / "replay.html").write_text(
        "<!doctype html><html lang='en'><meta charset='utf-8'><title>Fleet PostgreSQL checks</title>"
        f"<h1>Fleet PostgreSQL checks</h1><p>{report['result']}</p>"
        f"<p>Server stopped: {shutdown}</p><p>{html.escape(report['scope'])}</p>"
        "<p><a href='manifest.json'>Setup, shutdown and hashes</a></p>"
        "<p><a href='backend/replay.html'>Backend test replay, if reached</a></p>"
        f"<pre>{html.escape(details)}</pre></html>"
    )
    print(f"{report['result'].upper()}: {output / 'replay.html'}")
    return 0 if passed else 1


if __name__ == "__main__":
    sys.exit(main())