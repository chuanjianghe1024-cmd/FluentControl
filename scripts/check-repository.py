#!/usr/bin/env python3
"""Check repository metadata, community navigation and required CI check wiring."""

import json
from pathlib import Path
import re
from urllib.parse import unquote, urlsplit

import yaml


ROOT = Path(__file__).resolve().parents[1]
REQUIRED_CHECKS = {"Repository checks", "Windows validation"}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def load_yaml(path):
    # BaseLoader keeps GitHub's `on` key a string (not YAML 1.1 boolean True).
    value = yaml.load(path.read_text(encoding="utf-8"), Loader=yaml.BaseLoader)
    require(isinstance(value, dict), f"Expected YAML object: {path}")
    return value


def check_ruleset(rule, workflows):
    require(rule["target"] == "branch", "Ruleset must target branches")
    require(rule["enforcement"] == "disabled", "Template must stay disabled; activation is a separate maintainer action")
    require(rule["bypass_actors"] == [], "Strict template must not silently exempt maintainers or bots")
    require(rule["conditions"]["ref_name"] == {"include": ["refs/heads/main"], "exclude": []}, "Ruleset must target main only")
    rules = {entry["type"]: entry.get("parameters", {}) for entry in rule["rules"]}
    require({"deletion", "non_fast_forward", "pull_request", "required_status_checks"} <= rules.keys(), "Incomplete strict rules")
    review = rules["pull_request"]
    require(review["required_approving_review_count"] >= 1, "An approval is required")
    require(review["dismiss_stale_reviews_on_push"] and review["require_last_push_approval"]
            and review["required_review_thread_resolution"], "Review freshness and resolved conversations are required")
    checks = rules["required_status_checks"]
    require(checks["strict_required_status_checks_policy"], "Strict checks must include current base")
    require({item["context"] for item in checks["required_status_checks"]} == REQUIRED_CHECKS, "Required check names changed")
    require(all(item.get("integration_id") == 15368 for item in checks["required_status_checks"]), "Checks must come from GitHub Actions")
    available = {}
    for workflow in workflows:
        for job_id, job in workflow["jobs"].items():
            name = job.get("name", job_id)
            require(name not in available, f"Ambiguous check name: {name}")
            available[name] = workflow
    for name in REQUIRED_CHECKS:
        require(name in available, f"No workflow produces {name}")
        triggers = available[name]["on"]
        require("pull_request" in triggers and "merge_group" in triggers, f"{name} must run on PRs and merge groups")
        trigger = triggers["pull_request"] or {}
        require(not {"paths", "paths-ignore"} & trigger.keys(), f"Required workflow {name} must not be path-filtered")


def check_links(path, root=ROOT):
    # Our guides use inline Markdown links; external URLs and anchors are not fetched.
    text = re.sub(r"```.*?```", "", path.read_text(encoding="utf-8"), flags=re.S)
    text = re.sub(r"`+[^`]*`+", "", text)
    for target in re.findall(r"\]\(([^\s)]+)\)", text):
        url = urlsplit(target)
        if url.scheme or url.netloc or not url.path:
            continue
        destination = (path.parent / unquote(url.path)).resolve()
        require(destination.is_relative_to(root.resolve()), f"Link escapes repository: {path}: {target}")
        require(destination.exists(), f"Broken local link: {path}: {target}")


def main():
    workflows = []
    for path in sorted((ROOT / ".github/workflows").glob("*.yml")):
        data = load_yaml(path)
        require("on" in data and isinstance(data.get("jobs"), dict), f"Invalid workflow: {path}")
        require("pull_request_target" not in data["on"], "Do not execute contributor code with privileged PR events")
        require(data.get("permissions", {}).get("contents") == "read", f"Default token must be read-only: {path}")
        if "pull_request" in data["on"]:
            for job in data["jobs"].values():
                require("write" not in job.get("permissions", {}).values(), f"PR job has write permission: {path}")
        workflows.append(data)
    for path in sorted((ROOT / ".github").rglob("*.json")):
        json.loads(path.read_text(encoding="utf-8"))
    for path in sorted((ROOT / ".github/ISSUE_TEMPLATE").glob("*.yml")):
        data = load_yaml(path)
        if path.name != "config.yml":
            require(data.get("name") and data.get("description") and data.get("body"), f"Incomplete issue form: {path}")
            ids = [field["id"] for field in data["body"] if "id" in field]
            require(len(ids) == len(set(ids)), f"Duplicate issue field id: {path}")
    load_yaml(ROOT / ".github/dependabot.yml")
    check_ruleset(json.loads((ROOT / ".github/rulesets/community-main.json").read_text()), workflows)
    guides = [ROOT / name for name in (
        "README.md", "CONTRIBUTING.md", "CODE_OF_CONDUCT.md", "SECURITY.md", "agent.md",
        "docs/maintaining.md", "docs/development.md", "docs/wiki-maintenance.md",
    )]
    guides.extend((ROOT / "docs/wiki").glob("*.md"))
    for path in guides:
        check_links(path)
    print(f"Repository checks passed: {len(workflows)} workflows, ruleset wiring, issue forms and {len(guides)} guides")


if __name__ == "__main__":
    main()
