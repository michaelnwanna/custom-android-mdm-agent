import argparse
import datetime
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tarfile
import urllib.request


ROOT = Path(__file__).resolve().parents[1]
VERSION = "17.11"
SOURCE_URL = f"https://ftp.postgresql.org/pub/source/v{VERSION}/postgresql-{VERSION}.tar.bz2"
SOURCE_SHA256 = "dd27f2b3c59e73ed14aa3324901242bf69a032a6347805f274e6260322d42979"


def main():
    parser = argparse.ArgumentParser(description="Build a checksum-pinned, project-local PostgreSQL for Unix-socket lab tests.")
    parser.add_argument("--archive", type=Path, help="Use an existing source archive, still checking its pinned SHA-256")
    args = parser.parse_args()
    stamp = datetime.datetime.now(datetime.timezone.utc).strftime("%Y%m%dT%H%M%S.%fZ")
    output = ROOT / "artifacts/postgres-build" / stamp
    output.mkdir(parents=True, exist_ok=False)
    private = ROOT / ".lab-private/postgres-build" / stamp
    private.mkdir(parents=True, mode=0o700, exist_ok=False)
    prefix = ROOT / ".lab-private/postgres-install" / VERSION
    archive = args.archive or private / f"postgresql-{VERSION}.tar.bz2"
    commands = []
    passed = False
    shutil.copyfile(Path(__file__), output / Path(__file__).name)
    try:
        if prefix.exists():
            raise RuntimeError(f"Install prefix already exists; refusing to overwrite: {prefix}")
        if args.archive is None:
            with urllib.request.urlopen(SOURCE_URL, timeout=120) as response, archive.open("wb") as destination:
                shutil.copyfileobj(response, destination)
        if hashlib.sha256(archive.read_bytes()).hexdigest() != SOURCE_SHA256:
            raise RuntimeError("PostgreSQL source archive checksum mismatch")
        with tarfile.open(archive) as source:
            source.extractall(private, filter="data")
        working = private / f"postgresql-{VERSION}"
        planned = [
            ["./configure", f"--prefix={prefix}", "--without-icu", "--without-readline", "--without-ssl",
             "--without-gssapi", "--without-lz4", "--without-zstd"],
            ["make", "-j4"],
            ["make", "install"],
            [str(prefix / "bin/postgres"), "--version"],
            [str(prefix / "bin/pg_config"), "--configure"],
        ]
        for command in planned:
            log = output / f"command-{len(commands) + 1:02d}.log"
            print(f"Running {command[0]}: {log}", flush=True)
            with log.open("w") as stream:
                process = subprocess.run(command, cwd=working, stdout=stream, stderr=subprocess.STDOUT, timeout=1200)
            commands.append({"argv": command, "returncode": process.returncode, "log": log.name})
            if process.returncode:
                raise RuntimeError(f"Build command failed with code {process.returncode}: {log}")
        passed = True
    except (OSError, RuntimeError, subprocess.TimeoutExpired, tarfile.TarError) as error:
        (output / "failure.txt").write_text(str(error) + "\n")
        print(str(error), flush=True)
    report = {
        "schemaVersion": 1, "result": "passed" if passed else "failed", "version": VERSION,
        "sourceUrl": SOURCE_URL, "sourceSha256": SOURCE_SHA256, "prefix": str(prefix), "commands": commands,
        "scope": "Minimal local lab build without TLS/ICU/Readline; not a production server installation",
        "fileSha256": {filename.name: hashlib.sha256(filename.read_bytes()).hexdigest()
                       for filename in sorted(output.iterdir()) if filename.is_file()},
    }
    (output / "manifest.json").write_text(json.dumps(report, indent=2) + "\n")
    print(f"{report['result'].upper()}: {output / 'manifest.json'}")
    return 0 if passed else 1


if __name__ == "__main__":
    sys.exit(main())