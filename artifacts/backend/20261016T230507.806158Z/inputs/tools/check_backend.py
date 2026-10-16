import argparse
import datetime
import hashlib
import html
import json
from pathlib import Path
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]
TRX = {"test": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--postgres", action="store_true", help="Run real PostgreSQL tests using FLEET_TEST_POSTGRES; never skips")
    parser.add_argument("--postgres-restart", action="store_true", help="Include managed private-cluster restart tests; requires --postgres")
    parser.add_argument("--output-directory", type=Path, help="New evidence directory, which must not exist")
    args = parser.parse_args()
    if args.postgres_restart and not args.postgres:
        parser.error("--postgres-restart requires --postgres and a runner-managed private cluster")
    started = datetime.datetime.now(datetime.timezone.utc)
    output = (args.output_directory.resolve() if args.output_directory else
              ROOT / "artifacts" / "backend" / started.strftime("%Y%m%dT%H%M%S.%fZ"))
    output.mkdir(parents=True, exist_ok=False)
    sources = [
        filename for filename in (ROOT / "backend").rglob("*")
        if filename.is_file() and filename.suffix in (".cs", ".csproj", ".json", ".props")
        and not {"bin", "obj"}.intersection(filename.relative_to(ROOT / "backend").parts)
    ]
    sources.append(ROOT / "tools/check_backend.py")
    sources.extend(ROOT / "tools" / filename for filename in
                   ("check_postgres.py", "install_postgres_lab.py", "test_postgres_tools.py"))
    hashes = {}
    for source in sorted(sources):
        relative = source.relative_to(ROOT)
        destination = output / "inputs" / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, destination)
        hashes[str(relative)] = hashlib.sha256(destination.read_bytes()).hexdigest()
    working = output / "inputs/backend"
    project = ("tests/FleetManagement.Postgres.Tests/FleetManagement.Postgres.Tests.csproj" if args.postgres
               else "tests/FleetManagement.Tests/FleetManagement.Tests.csproj")
    commands = [
        ["dotnet", "--info"],
        [sys.executable, "-m", "unittest", "discover", "-s", "../tools", "-p", "test_postgres_tools.py", "-v"],
        ["dotnet", "build", "tests/FleetManagement.Postgres.Tests/FleetManagement.Postgres.Tests.csproj", "--verbosity", "minimal"],
        ["dotnet", "test", project,
         "--logger", "trx;LogFileName=tests.trx", "--results-directory", str(output / "results"),
         "--verbosity", "minimal"],
        ["dotnet", "list", project,
         "package", "--include-transitive", "--format", "json", "--no-restore"],
        ["dotnet", "list", "src/FleetManagement/FleetManagement.csproj",
         "package", "--include-transitive", "--format", "json", "--no-restore"],
    ]
    if args.postgres and not args.postgres_restart:
        commands[3].extend(["--filter", "FullyQualifiedName!~EnrollmentRestartTests"])
    records = []
    for command in commands:
        try:
            process = subprocess.run(command, cwd=working, capture_output=True, text=True, timeout=240)
            entry = {"argv": command, "returncode": process.returncode, "stdout": process.stdout, "stderr": process.stderr}
        except subprocess.TimeoutExpired as error:
            entry = {"argv": command, "returncode": 124,
                     "stdout": (error.stdout or b"").decode(errors="replace"),
                     "stderr": (error.stderr or b"").decode(errors="replace") + "\nTimed out after 240 seconds"}
        except OSError as error:
            entry = {"argv": command, "returncode": 127, "stdout": "", "stderr": str(error)}
        records.append(entry)
        (output / f"command-{len(records):02d}.json").write_text(json.dumps(entry, indent=2) + "\n")
        print(entry["stdout"] + entry["stderr"])
        if entry["returncode"]:
            break
    totals = None
    restart_verified = False
    result_file = output / "results/tests.trx"
    if result_file.exists():
        result_tree = ET.parse(result_file)
        counters = result_tree.find("test:ResultSummary/test:Counters", TRX)
        if counters is not None:
            totals = dict(counters.attrib)
        restart_verified = any(
            result.get("testName") == "FleetManagement.Postgres.Tests.EnrollmentRestartTests.CommittedClaimSurvivesFastPostgresRestart"
            and result.get("outcome") == "Passed"
            for result in result_tree.findall("test:Results/test:UnitTestResult", TRX))
    passed = (len(records) == len(commands) and all(entry["returncode"] == 0 for entry in records)
              and totals is not None and int(totals["total"]) > 0
              and int(totals["passed"]) == int(totals["total"])
              and (not args.postgres_restart or restart_verified))
    report = {
        "schemaVersion": 1, "startedAt": started.isoformat(),
        "finishedAt": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "result": "passed" if passed else "failed", "commands": records,
        "workingDirectory": "inputs/backend", "inputSha256": hashes, "testCounters": totals,
        "restartVerified": restart_verified,
          "scope": ("Real PostgreSQL claims, lab signing, in-process enrollment/provisioning HTTP, actor-audit rollback and controlled fast restart; no power-loss, network TLS, live identity-provider or device integration" if args.postgres_restart else
              "Real PostgreSQL claims, lab signing, in-process enrollment/provisioning HTTP and actor-audit rollback; no server restart, network TLS, live identity-provider or device integration" if args.postgres else
              "Component/model, signer process, administrator signed-token authorization, in-process HTTP and separate loopback Kestrel TLS tests plus PostgreSQL test compilation; no database execution, live identity-provider, combined TLS/database or device integration"),
        "resultSha256": {str(filename.relative_to(output)): hashlib.sha256(filename.read_bytes()).hexdigest()
                 for filename in sorted(output.rglob("*"))
                 if filename.is_file() and "inputs" not in filename.relative_to(output).parts},
    }
    (output / "manifest.json").write_text(json.dumps(report, indent=2) + "\n")
    details = "\n".join(entry["stdout"] + entry["stderr"] for entry in records)
    (output / "replay.html").write_text(
        "<!doctype html><html lang='en'><meta charset='utf-8'><title>Fleet backend checks</title>"
        f"<h1>Fleet backend checks</h1><p>{report['result']}</p><p>{html.escape(report['scope'])}</p>"
        "<p><a href='manifest.json'>Commands, input hashes and test counters</a></p>"
        "<p><a href='results/tests.trx'>TRX test results, if generated</a></p>"
        f"<pre>{html.escape(details)}</pre></html>"
    )
    print(f"{report['result'].upper()}: {output / 'replay.html'}")
    return 0 if passed else 1


if __name__ == "__main__":
    sys.exit(main())