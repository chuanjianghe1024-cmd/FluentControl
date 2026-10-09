#!/usr/bin/env python3
"""Promote an exact, tested MSI to a Release; never rebuild or replace assets."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def validate_manifest(m):
    require(re.fullmatch(r"0\.3\.\d+", m["version"]), "Unexpected MSI version")
    require(re.fullmatch(r"[0-9a-f]{40}", m["sourceCommit"]), "Invalid source commit")
    for field in ("sha256", "archiveSha256"):
        require(re.fullmatch(r"[0-9a-f]{64}", m[field]), f"Invalid {field}")
    for field in ("runId", "artifactId", "bytes"):
        require(type(m[field]) is int and m[field] > 0, f"Invalid {field}")
    require(m["fileName"] == f'FluentControl-{m["version"]}-x64.msi', "Unexpected MSI name")
    require(m["artifactName"] == "FluentControl-MSI-x64", "Unexpected artifact name")
    require(m["notes"] == f'docs/releases/v{m["version"]}.md', "Unexpected release notes path")
    require((ROOT / m["notes"]).is_file(), "Release notes are missing")


def validate_source(m, run, artifact, repo):
    require(run["status"] == "completed" and run["conclusion"] == "success", "Build did not pass")
    require(run["id"] == m["runId"] and run["head_sha"] == m["sourceCommit"], "Wrong build source")
    require(run["head_branch"] == "main" and run["event"] == "push", "Build must be a main push")
    require(run["path"] == ".github/workflows/build.yml", "Wrong build workflow")
    require(run["repository"]["full_name"] == repo and run["head_repository"]["full_name"] == repo,
            "Build belongs to a different repository")
    require(m["version"] == f'0.3.{run["run_number"]}', "Version differs from tested build")
    require(artifact["id"] == m["artifactId"] and artifact["name"] == m["artifactName"], "Wrong artifact")
    require(not artifact["expired"], "Build artifact has expired")
    origin = artifact["workflow_run"]
    require(origin["id"] == m["runId"] and origin["head_sha"] == m["sourceCommit"], "Artifact source mismatch")
    require(origin["repository_id"] == run["repository"]["id"] and
            origin["head_repository_id"] == run["repository"]["id"], "Artifact repository mismatch")
    require(artifact["digest"] == "sha256:" + m["archiveSha256"], "Artifact archive digest mismatch")


def unpack_msi(m, archive, destination):
    require(sha256(archive) == m["archiveSha256"], "Downloaded ZIP checksum mismatch")
    with zipfile.ZipFile(archive) as package:
        entries = package.infolist()
        require(len(entries) == 1 and entries[0].filename == m["fileName"], "Unexpected ZIP contents")
        require(entries[0].file_size == m["bytes"], "MSI size mismatch")
        # Never extract arbitrary archive paths, symlinks or additional files.
        target = destination / m["fileName"]
        target.write_bytes(package.read(entries[0]))
    require(sha256(target) == m["sha256"], "MSI checksum mismatch")
    return target


def gh(*args, output=None):
    if output is not None:
        with output.open("wb") as stream:
            subprocess.run(["gh", *args], stdout=stream, check=True)
        return None
    return subprocess.check_output(["gh", *args], text=True)


def api(endpoint, optional=False):
    result = subprocess.run(["gh", "api", endpoint], capture_output=True, text=True)
    if optional and result.returncode and "HTTP 404" in result.stderr:
        return None
    if result.returncode:
        raise RuntimeError(f"GitHub API failed for {endpoint}: {result.stderr.strip()}")
    return json.loads(result.stdout)


def verify_tag(base, tag, expected, optional=False):
    ref = api(f"{base}/git/ref/tags/{tag}", optional=optional)
    if ref is None:
        return
    obj = ref["object"]
    for _ in range(10):
        if obj["type"] != "tag":
            break
        obj = api(f'{base}/git/tags/{obj["sha"]}')["object"]
    require(obj["type"] == "commit" and obj["sha"] == expected, "Existing tag points to different source")


def find_release(base, tag):
    release = api(f"{base}/releases/tags/{tag}", optional=True)
    if release is not None:
        return release
    # The tag endpoint may not resolve an unpublished draft. List releases
    # with authenticated write access before deciding to create another one.
    page = 1
    while True:
        releases = api(f"{base}/releases?per_page=100&page={page}")
        matches = [r for r in releases if r["tag_name"] == tag]
        require(len(matches) <= 1, "Multiple drafts use the requested tag")
        if matches:
            return matches[0]
        if len(releases) < 100:
            return None
        page += 1


def expected_assets(m):
    checksum = f'{m["sha256"]}  {m["fileName"]}\n'.encode("utf-8")
    return {
        m["fileName"]: (m["sha256"], m["bytes"]),
        "SHA256SUMS.txt": (hashlib.sha256(checksum).hexdigest(), len(checksum)),
    }, checksum


def validate_assets(assets, expected, complete=False):
    names = [a["name"] for a in assets]
    require(len(names) == len(set(names)), "Duplicate release assets")
    require(set(names).issubset(expected), "Unexpected release assets")
    if complete:
        require(set(names) == set(expected), "Missing release assets")
    for asset in assets:
        digest, size = expected[asset["name"]]
        require(asset["state"] == "uploaded" and asset["size"] == size, "Asset upload/size mismatch")
        require(asset.get("digest") == "sha256:" + digest, "Release asset checksum mismatch")


def publish(m, repo):
    require(re.fullmatch(r"[\w.-]+/[\w.-]+", repo), "Invalid repository")
    base, tag = f"repos/{repo}", "v" + m["version"]
    expected, checksum = expected_assets(m)
    release = find_release(base, tag)
    if release and not release["draft"]:
        verify_tag(base, tag, m["sourceCommit"])
        require(not release["prerelease"], "Existing release is a prerelease")
        validate_assets(release["assets"], expected, complete=True)
        print(f'Already published and verified: {release["html_url"]}')
        return

    verify_tag(base, tag, m["sourceCommit"], optional=True)
    if release:
        require(release["target_commitish"] == m["sourceCommit"], "Draft has different source")
        require(not release["prerelease"], "Draft is a prerelease")
        validate_assets(release["assets"], expected)

    run = api(f'{base}/actions/runs/{m["runId"]}')
    artifact = api(f'{base}/actions/artifacts/{m["artifactId"]}')
    validate_source(m, run, artifact, repo)
    with tempfile.TemporaryDirectory(prefix="fluentcontrol-release-") as temporary:
        work = Path(temporary)
        archive = work / "artifact.zip"
        gh("api", f'{base}/actions/artifacts/{m["artifactId"]}/zip', output=archive)
        msi = unpack_msi(m, archive, work)
        sums = work / "SHA256SUMS.txt"
        sums.write_bytes(checksum)
        if release is None:
            gh("release", "create", tag, "--repo", repo, "--draft", "--target", m["sourceCommit"],
               "--title", f'FluentControl {tag}', "--notes-file", str(ROOT / m["notes"]))
            release = find_release(base, tag)
            require(release is not None, "Created draft could not be found")
        present = {a["name"] for a in release["assets"]}
        for asset in (msi, sums):
            if asset.name not in present:
                # No --clobber: a retry may resume, but never replace any asset.
                gh("release", "upload", tag, str(asset), "--repo", repo)
        release = api(f'{base}/releases/{release["id"]}')
        validate_assets(release["assets"], expected, complete=True)
        gh("release", "edit", tag, "--repo", repo, "--draft=false", "--latest",
           "--notes-file", str(ROOT / m["notes"]))

    release = api(f"{base}/releases/tags/{tag}")
    require(not release["draft"] and not release["prerelease"], "Release was not published")
    verify_tag(base, tag, m["sourceCommit"])
    validate_assets(release["assets"], expected, complete=True)
    print(f'Published and verified: {release["html_url"]}')
    if os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as summary:
            summary.write(f'Published [{tag}]({release["html_url"]}) from `{m["sourceCommit"]}`.\n\n'
                          f'MSI SHA-256: `{m["sha256"]}`\n')


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args()
    manifest = json.loads((ROOT / ".github/release.json").read_text(encoding="utf-8"))
    validate_manifest(manifest)
    if args.validate_only:
        print(f'Valid release manifest: v{manifest["version"]}')
    else:
        publish(manifest, os.environ["GITHUB_REPOSITORY"])
