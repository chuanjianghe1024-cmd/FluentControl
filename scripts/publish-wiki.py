#!/usr/bin/env python3
"""Validate and publish repository-owned pages to this repository's GitHub Wiki."""

import argparse
import os
from pathlib import Path
import re
import subprocess
import tempfile


REPOSITORY = "chuanjianghe1024-cmd/FluentControl"
ROOT = Path(__file__).resolve().parents[1]
LINK = re.compile(r"\]\(([^\s)]+)\)")
PAGE_NAME = re.compile(r"(?:[A-Za-z0-9][A-Za-z0-9-]*|_Sidebar|_Footer)\.md")


def load_pages(source):
    if source.is_symlink() or not source.is_dir():
        raise ValueError("Wiki source must be a regular directory")
    pages = {}
    for path in sorted(source.iterdir()):
        if path.is_symlink() or not path.is_file() or not PAGE_NAME.fullmatch(path.name):
            raise ValueError(f"Unexpected Wiki source: {path.name}")
        content = path.read_text(encoding="utf-8")
        if not content.strip():
            raise ValueError(f"Empty Wiki page: {path.name}")
        pages[path.name] = content
    if not {"Home.md", "_Sidebar.md", "_Footer.md"} <= pages.keys():
        raise ValueError("Home, sidebar and footer are required")
    if len({name.lower() for name in pages}) != len(pages):
        raise ValueError("Wiki page names must be unique ignoring case")
    return {name: render_links(content, pages) for name, content in pages.items()}


def render_links(content, pages):
    def replace(match):
        target = match.group(1)
        if re.match(r"[A-Za-z][A-Za-z0-9+.-]*:", target) or target.startswith("#"):
            return match.group(0)
        page, separator, anchor = target.partition("#")
        if page not in pages:
            raise ValueError(f"Unknown local Wiki link: {target}")
        suffix = separator + anchor
        return f"](https://github.com/{REPOSITORY}/wiki/{page[:-3]}{suffix})"
    return LINK.sub(replace, content)


def git(args, *, cwd=None, env=None):
    result = subprocess.run(
        ["git", "-c", "credential.helper=", *args], cwd=cwd, env=env,
        text=True, capture_output=True, timeout=90, check=False,
    )
    if result.returncode:
        detail = result.stderr.strip()
        token = (env or {}).get("GH_TOKEN", "")
        if token:
            detail = detail.replace(token, "[redacted]")
        raise RuntimeError(f"git {args[0]} failed: {detail}")
    return result.stdout.strip()


def sync_pages(checkout, pages, env):
    """Only touch owned filenames; retain other Wiki pages and normal Git history."""
    branch = git(["symbolic-ref", "--short", "HEAD"], cwd=checkout, env=env)
    for name, content in pages.items():
        target = checkout / name
        if target.is_symlink() or (target.exists() and not target.is_file()):
            raise ValueError(f"Refusing non-file Wiki destination: {name}")
        target.write_text(content, encoding="utf-8")
    git(["add", "--", *pages], cwd=checkout, env=env)
    if not git(["diff", "--cached", "--name-only"], cwd=checkout, env=env):
        return git(["rev-parse", "HEAD"], cwd=checkout, env=env), False
    git([
        "-c", "user.name=github-actions[bot]",
        "-c", "user.email=41898282+github-actions[bot]@users.noreply.github.com",
        "commit", "-m", "docs: sync FluentControl user guide",
    ], cwd=checkout, env=env)
    sha = git(["rev-parse", "HEAD"], cwd=checkout, env=env)
    # Normal push deliberately refuses to overwrite concurrent Wiki changes.
    git(["push", "origin", f"HEAD:refs/heads/{branch}"], cwd=checkout, env=env)
    remote = git(["ls-remote", "origin", f"refs/heads/{branch}"], cwd=checkout, env=env)
    if not remote or remote.split()[0] != sha:
        raise RuntimeError("Wiki remote head changed; review before rerunning")
    return sha, True


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args()
    pages = load_pages(ROOT / "docs" / "wiki")
    print(f"Validated {len(pages)} Wiki files and their local links")
    if args.validate_only:
        return
    if os.environ.get("GITHUB_REPOSITORY") != REPOSITORY:
        raise ValueError("Publishing is restricted to the FluentControl repository")
    if os.environ.get("GITHUB_REF") != "refs/heads/main":
        raise ValueError("Publish only from main")
    if not os.environ.get("GH_TOKEN"):
        raise ValueError("The workflow's GitHub token is required")
    with tempfile.TemporaryDirectory(prefix="fluentcontrol-wiki-") as temporary:
        temp = Path(temporary)
        askpass = temp / "askpass.sh"
        askpass.write_text(
            '#!/bin/sh\ncase "$1" in\n'
            '  *Username*) printf "%s\\n" "x-access-token" ;;\n'
            '  *) printf "%s\\n" "$GH_TOKEN" ;;\nesac\n', encoding="utf-8",
        )
        askpass.chmod(0o700)
        env = dict(os.environ, GIT_ASKPASS=str(askpass), GIT_TERMINAL_PROMPT="0")
        checkout = temp / "wiki"
        try:
            git(["clone", "--single-branch", f"https://github.com/{REPOSITORY}.wiki.git",
                 str(checkout)], env=env)
        except RuntimeError as error:
            raise RuntimeError(
                "Wiki is inaccessible. Check that Wiki is enabled, an initial page has "
                "been saved on GitHub, and the workflow has contents: write. "
                "Then rerun the workflow. " + str(error)
            ) from error
        sha, changed = sync_pages(checkout, pages, env)
        action = "Published" if changed else "Already current"
        summary = f"{action}: {len(pages)} files, Wiki commit {sha}\nhttps://github.com/{REPOSITORY}/wiki\n"
        print(summary)
        if os.environ.get("GITHUB_STEP_SUMMARY"):
            with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as output:
                output.write(summary)


if __name__ == "__main__":
    main()
