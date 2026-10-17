import datetime
import hashlib
import html
import importlib.metadata
import json
from pathlib import Path
import shutil
import subprocess
import sys


ROOT = Path(__file__).resolve().parents[1]


def main():
    started = datetime.datetime.now(datetime.timezone.utc)
    output = ROOT / "artifacts" / "contracts" / started.strftime("%Y%m%dT%H%M%S.%fZ")
    output.mkdir(parents=True, exist_ok=False)
    sources = sorted((ROOT / "contracts").rglob("*.json"))
    sources.extend(ROOT / relative for relative in (
        "tools/test_contracts.py", "tools/check_contracts.py", "tools/contracts-requirements.txt",
    ))
    hashes = {}
    for source in sources:
        relative = source.relative_to(ROOT)
        destination = output / "inputs" / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, destination)
        hashes[str(relative)] = hashlib.sha256(destination.read_bytes()).hexdigest()
    command = [sys.executable, "-m", "unittest", "discover", "-s", "tools", "-p", "test_contracts.py", "-v"]
    try:
        process = subprocess.run(command, cwd=output / "inputs", capture_output=True, text=True, timeout=120)
        returncode, stdout, stderr = process.returncode, process.stdout, process.stderr
    except subprocess.TimeoutExpired as error:
        returncode = 124
        stdout = (error.stdout or b"").decode(errors="replace")
        stderr = (error.stderr or b"").decode(errors="replace") + "\nTimed out after 120 seconds"
    fixtures = [json.loads(source.read_text()) for source in (output / "inputs/contracts/fixtures").glob("*-v1.json")]
    report = {
        "schemaVersion": 1,
        "startedAt": started.isoformat(),
        "finishedAt": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "result": "passed" if returncode == 0 else "failed",
        "command": command,
        "workingDirectory": "inputs",
        "pythonVersion": sys.version,
        "dependencies": dict(sorted(
            (distribution.metadata["Name"], distribution.version)
            for distribution in importlib.metadata.distributions()
        )),
        "inputSha256": hashes,
        "validCases": sum(len(suite["valid"]) for suite in fixtures),
        "rejectionCases": sum(len(suite["invalid"]) for suite in fixtures),
        "returncode": returncode,
        "stdout": stdout,
        "stderr": stderr,
    }
    (output / "manifest.json").write_text(json.dumps(report, indent=2) + "\n")
    (output / "replay.html").write_text(
        "<!doctype html><html lang='en'><meta charset='utf-8'>"
        "<title>Fleet contract checks</title><h1>Fleet contract checks</h1>"
        f"<p>{html.escape(report['result'])}: {report['validCases']} valid and "
        f"{report['rejectionCases']} rejection fixtures checked.</p>"
        "<p><a href='manifest.json'>Command, dependencies, hashes and result</a></p>"
        "<p>Schema validation only; not runtime, authorization or integration evidence.</p>"
        f"<pre>{html.escape(stdout + stderr)}</pre></html>"
    )
    print(stdout + stderr)
    print(f"{report['result'].upper()}: {output / 'replay.html'}")
    return returncode


if __name__ == "__main__":
    sys.exit(main())