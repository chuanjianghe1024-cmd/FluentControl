import importlib.util
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[2]


def load(name, filename):
    spec = importlib.util.spec_from_file_location(name, ROOT / "scripts" / filename)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


changes = load("ci_changes", "ci-changes.py")
checks = load("check_repository", "check-repository.py")


class ChangeTests(unittest.TestCase):
    def test_documentation_and_mixed_changes(self):
        self.assertFalse(changes.requires_windows(["README.md", "docs/wiki/Home.md", ".github/ISSUE_TEMPLATE/bug_report.yml"]))
        self.assertTrue(changes.requires_windows(["README.md", "src/FluentControl/App.xaml.cs"]))
        self.assertTrue(changes.requires_windows(["new-unknown-build-input"]))
        self.assertTrue(changes.requires_windows([".github/workflows/build.yml"]))
        self.assertTrue(changes.requires_windows(["scripts/ci-changes.py"]))
        self.assertTrue(changes.requires_windows(["src/FluentControl/README.md"]))

    def test_missing_comparison_and_manual_runs_use_full_validation(self):
        for name, event in (("workflow_dispatch", {}), ("merge_group", {}),
                            ("pull_request", {}), ("push", {"before": "0" * 40, "after": "a" * 40})):
            self.assertTrue(changes.decide(name, event)[0])
        with patch.object(changes, "changed_paths", side_effect=OSError("git unavailable")):
            self.assertTrue(changes.decide("push", {})[0])

    def test_pr_uses_merge_base_and_rename_includes_removed_source(self):
        event = {"pull_request": {"base": {"sha": "a" * 40}, "head": {"sha": "b" * 40}}}
        with patch.object(changes.subprocess, "run") as run:
            run.return_value.stdout = b"src/deleted.cs\0docs/replacement.md\0docs/file with\nnewline.md\0"
            paths = changes.changed_paths("pull_request", event)
            self.assertIn("a" * 40 + "..." + "b" * 40, run.call_args.args[0])
            self.assertIn("--no-renames", run.call_args.args[0])
            self.assertTrue(changes.requires_windows(paths))
            self.assertEqual(paths[-1], "docs/file with\nnewline.md")


class PolicyTests(unittest.TestCase):
    def test_required_checks_cannot_disappear_or_be_path_filtered(self):
        rule = json.loads((ROOT / ".github/rulesets/community-main.json").read_text())
        workflows = [checks.load_yaml(p) for p in (ROOT / ".github/workflows").glob("*.yml")]
        checks.check_ruleset(rule, workflows)
        candidate = next(w for w in workflows if "repository-checks" in w["jobs"])
        candidate["on"]["pull_request"]["paths"] = ["src/**"]
        with self.assertRaisesRegex(ValueError, "path-filtered"):
            checks.check_ruleset(rule, workflows)

    def test_template_does_not_enable_enforcement_or_bypass(self):
        rule = json.loads((ROOT / ".github/rulesets/community-main.json").read_text())
        rule["enforcement"] = "active"
        with self.assertRaisesRegex(ValueError, "disabled"):
            checks.check_ruleset(rule, [])
        rule["enforcement"] = "disabled"
        rule["bypass_actors"] = [{"actor_type": "User", "actor_id": 1}]
        with self.assertRaisesRegex(ValueError, "exempt"):
            checks.check_ruleset(rule, [])

    def test_links_resolve_relative_to_each_guide(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "LICENSE").write_text("license")
            doc = root / "docs" / "guide.md"
            doc.parent.mkdir()
            doc.write_text("[license](../LICENSE) [web](https://example.org) [here](#section) `[example](Not-A-Page.md)`")
            checks.check_links(doc, root)
            doc.write_text("[missing](missing.md)")
            with self.assertRaisesRegex(ValueError, "Broken"):
                checks.check_links(doc, root)

    def test_windows_gate_fails_closed(self):
        workflow = checks.load_yaml(ROOT / ".github/workflows/build.yml")
        gate = workflow["jobs"]["windows-validation"]["steps"][0]["run"]
        cases = [
            ("success", "false", "skipped", 0),
            ("success", "true", "success", 0),
            ("success", "true", "failure", 1),
            ("success", "true", "cancelled", 1),
            ("success", "true", "skipped", 1),
            ("failure", "false", "skipped", 1),
            ("cancelled", "false", "skipped", 1),
            ("success", "", "skipped", 1),
        ]
        for classified, required, result, expected in cases:
            with self.subTest(classified=classified, required=required, result=result):
                env = dict(os.environ, CLASSIFICATION=classified, WINDOWS_REQUIRED=required, BUILD_RESULT=result)
                actual = subprocess.run(["bash", "-e", "-c", gate], env=env, capture_output=True, timeout=5)
                self.assertEqual(actual.returncode, expected)


if __name__ == "__main__":
    unittest.main()
