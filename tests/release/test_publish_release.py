"""Integrity and retry regressions. These tests never contact GitHub."""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("publish_release", ROOT / "scripts/publish-release.py")
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)
MANIFEST = json.loads((ROOT / ".github/release.json").read_text(encoding="utf-8"))


class ReleaseTests(unittest.TestCase):
    def setUp(self):
        self.m = copy.deepcopy(MANIFEST)
        self.repo = "owner/FluentControl"

    def assets(self):
        expected, _ = release.expected_assets(self.m)
        return [{"name": name, "state": "uploaded", "size": size, "digest": "sha256:" + digest}
                for name, (digest, size) in expected.items()]

    def source(self):
        run = {"id": self.m["runId"], "head_sha": self.m["sourceCommit"], "head_branch": "main",
               "event": "push", "path": ".github/workflows/build.yml", "status": "completed",
               "conclusion": "success", "run_number": int(self.m["version"].split(".")[-1]),
               "repository": {"id": 1, "full_name": self.repo}, "head_repository": {"full_name": self.repo}}
        artifact = {"id": self.m["artifactId"], "name": self.m["artifactName"], "expired": False,
                    "digest": "sha256:" + self.m["archiveSha256"],
                    "workflow_run": {"id": self.m["runId"], "head_sha": self.m["sourceCommit"],
                                     "repository_id": 1, "head_repository_id": 1}}
        return run, artifact

    def test_checked_in_manifest(self):
        release.validate_manifest(self.m)

    def test_build_provenance(self):
        run, artifact = self.source()
        release.validate_source(self.m, run, artifact, self.repo)
        for field, bad in (("conclusion", "failure"), ("head_sha", "0" * 40), ("event", "pull_request"),
                           ("head_branch", "other"), ("run_number", -1), ("path", "other.yml")):
            with self.subTest(field=field):
                mutated = copy.deepcopy(run)
                mutated[field] = bad
                with self.assertRaises(ValueError):
                    release.validate_source(self.m, mutated, artifact, self.repo)
        for field, bad in (("expired", True), ("digest", "sha256:" + "0" * 64)):
            mutated = copy.deepcopy(artifact)
            mutated[field] = bad
            with self.assertRaises(ValueError):
                release.validate_source(self.m, run, mutated, self.repo)
        artifact["workflow_run"]["head_repository_id"] = 2
        with self.assertRaises(ValueError):
            release.validate_source(self.m, run, artifact, self.repo)

    def test_archive_requires_exact_content_and_hash(self):
        with tempfile.TemporaryDirectory() as directory:
            work = Path(directory)
            archive = work / "test.zip"
            content = b"test MSI bytes"
            self.m["bytes"] = len(content)
            self.m["sha256"] = hashlib.sha256(content).hexdigest()
            for entry, extra in ((self.m["fileName"], False), ("../escape.msi", False),
                                 (self.m["fileName"], True)):
                with zipfile.ZipFile(archive, "w") as z:
                    z.writestr(entry, content)
                    if extra:
                        z.writestr("unexpected.txt", "not a release asset")
                self.m["archiveSha256"] = release.sha256(archive)
                if entry == self.m["fileName"] and not extra:
                    result = release.unpack_msi(self.m, archive, work)
                    self.assertEqual(result.read_bytes(), content)
                    with patch.dict(self.m, {"sha256": "0" * 64}):
                        with self.assertRaisesRegex(ValueError, "MSI checksum"):
                            release.unpack_msi(self.m, archive, work)
                else:
                    with self.assertRaisesRegex(ValueError, "ZIP contents"):
                        release.unpack_msi(self.m, archive, work)
            archive.write_bytes(b"corrupted archive")
            with self.assertRaisesRegex(ValueError, "ZIP checksum"):
                release.unpack_msi(self.m, archive, work)

    def test_asset_validation_rejects_replacement_and_incomplete_upload(self):
        expected, _ = release.expected_assets(self.m)
        assets = self.assets()
        release.validate_assets(assets, expected, complete=True)
        release.validate_assets(assets[:1], expected)  # Valid draft can resume.
        with self.assertRaisesRegex(ValueError, "Missing"):
            release.validate_assets(assets[:1], expected, complete=True)
        for field, value in (("digest", "sha256:" + "0" * 64), ("state", "starter"), ("size", 0)):
            changed = copy.deepcopy(assets)
            changed[0][field] = value
            with self.assertRaises(ValueError):
                release.validate_assets(changed, expected)

    def test_published_retry_never_mutates_or_downloads_build(self):
        published = {"draft": False, "prerelease": False, "assets": self.assets(), "html_url": "release URL"}
        with patch.object(release, "find_release", return_value=published), \
             patch.object(release, "verify_tag") as tag, patch.object(release, "gh") as gh, \
             patch.object(release, "api") as api:
            release.publish(self.m, self.repo)
            tag.assert_called_once()
            gh.assert_not_called()
            api.assert_not_called()

    def test_conflicting_draft_stops_before_any_mutation(self):
        draft = {"draft": True, "target_commitish": "0" * 40}
        with patch.object(release, "find_release", return_value=draft), \
             patch.object(release, "verify_tag"), patch.object(release, "gh") as gh:
            with self.assertRaisesRegex(ValueError, "different source"):
                release.publish(self.m, self.repo)
            gh.assert_not_called()

    def test_finds_draft_when_tag_endpoint_returns_404(self):
        draft = {"tag_name": "v" + self.m["version"], "draft": True}
        with patch.object(release, "api", side_effect=[None, [draft]]):
            self.assertEqual(release.find_release("repos/owner/repo", draft["tag_name"]), draft)

    def test_wrong_existing_tag_is_rejected(self):
        ref = {"object": {"type": "commit", "sha": "0" * 40}}
        with patch.object(release, "api", return_value=ref):
            with self.assertRaisesRegex(ValueError, "different source"):
                release.verify_tag("repos/owner/repo", "v0.3.33", self.m["sourceCommit"])


if __name__ == "__main__":
    unittest.main()
