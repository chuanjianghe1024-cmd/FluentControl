"""One-time owner-requested cleanup; exact branch leases, no tags or active PRs."""
import json, os, subprocess
from pathlib import Path
REPO = "chuanjianghe1024-cmd/FluentControl"
BRANCH = "codex/test-release-0.3.201"
def api(path):
    return json.loads(subprocess.check_output(["gh", "api", "repos/" + REPO + ("/" + path if path else "")], text=True))
def main():
    if os.environ.get("GITHUB_REPOSITORY") != REPO or os.environ.get("GITHUB_REF") != "refs/heads/" + BRANCH:
        raise SystemExit("Unexpected repository/ref")
    targets = json.loads(Path(".github/branch-cleanup-20261026.json").read_text())
    assert len(targets) == 11 and len({x["name"] for x in targets}) == 11
    default = api("")["default_branch"]
    open_heads = set()
    page = 1
    while True:
        prs = api("pulls?state=open&per_page=100&page=" + str(page))
        open_heads.update(p["head"]["ref"] for p in prs if p["head"]["repo"] and p["head"]["repo"]["full_name"] == REPO)
        if len(prs) < 100: break
        page += 1
    branches = {}
    page = 1
    while True:
        items = api("branches?per_page=100&page=" + str(page))
        branches.update({b["name"]: b for b in items})
        if len(items) < 100: break
        page += 1
    pending = []
    for item in targets:
        name, sha = item["name"], item["sha"]
        assert name not in {default, BRANCH} and name not in open_heads
        assert len(sha) == 40 and all(c in "0123456789abcdef" for c in sha)
        if name not in branches:
            print("Already absent:", name)
            continue
        assert not branches[name]["protected"], name
        assert branches[name]["commit"]["sha"] == sha, "Branch moved: " + name
        pending.append(item)
    if pending:
        subprocess.run(["gh", "auth", "setup-git"], check=True)
        cmd = ["git", "push", "--atomic"]
        cmd += ["--force-with-lease=refs/heads/" + b["name"] + ":" + b["sha"] for b in pending]
        cmd += ["origin"]
        cmd += [":refs/heads/" + b["name"] for b in pending]
        subprocess.run(cmd, check=True)
    print("Cleanup complete; main, active PRs, publishing branches, tags and releases retained.")
if __name__ == "__main__":
    main()
