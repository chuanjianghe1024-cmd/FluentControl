#!/usr/bin/env python3
"""Skip Windows only for a known documentation/metadata-only change set."""

import json
import os
from pathlib import Path
import re
import subprocess


LIGHT_FILES = {
    "LICENSE", ".github/CODEOWNERS", ".github/PULL_REQUEST_TEMPLATE.md",
    ".github/dependabot.yml", ".github/requirements-ci.txt",
    ".github/release.json", ".github/repository-metadata.json",
    "scripts/publish-release.py", "scripts/publish-wiki.py",
}
LIGHT_PREFIXES = ("docs/", ".github/ISSUE_TEMPLATE/", ".github/rulesets/",
                  "tests/release/", "tests/wiki/")
SHA = re.compile(r"[0-9a-fA-F]{40}")


def requires_windows(paths):
    # Unknown locations intentionally run Windows, including CI/build changes.
    return any(not (
        path in LIGHT_FILES or path.startswith(LIGHT_PREFIXES)
        or ("/" not in path and path.endswith(".md"))
    ) for path in paths)


def changed_paths(event_name, event):
    if event_name == "pull_request":
        base = event["pull_request"]["base"]["sha"]
        head = event["pull_request"]["head"]["sha"]
        separator = "..."
    elif event_name == "push":
        base, head = event["before"], event["after"]
        separator = ".."
    else:
        # Manual runs and merge groups get the full gate.
        raise ValueError("Full Windows validation requested")
    if any(not SHA.fullmatch(sha) or sha == "0" * 40 for sha in (base, head)):
        raise ValueError("Missing or invalid comparison commits")
    result = subprocess.run(
        ["git", "diff", "--name-only", "--no-renames", "-z", base + separator + head, "--"],
        check=True, capture_output=True, timeout=30,
    )
    return [name.decode("utf-8") for name in result.stdout.split(b"\0") if name]


def decide(event_name, event):
    try:
        paths = changed_paths(event_name, event)
        return requires_windows(paths), f"Compared {len(paths)} changed paths"
    except (KeyError, TypeError, ValueError, OSError, subprocess.SubprocessError):
        return True, "Comparison unavailable or full run requested; running Windows"


def main():
    try:
        event = json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_text(encoding="utf-8"))
        windows, reason = decide(os.environ.get("GITHUB_EVENT_NAME", ""), event)
    except (KeyError, OSError, ValueError):
        windows, reason = True, "Event unavailable; running Windows"
    result = str(windows).lower()
    print(f"windows={result}: {reason}")
    if os.environ.get("GITHUB_OUTPUT"):
        with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
            output.write(f"windows={result}\n")


if __name__ == "__main__":
    main()
