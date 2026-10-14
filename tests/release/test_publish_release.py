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

    def test_preview_manifest_requires_separate_notes_and_boolean_channel(self):
        self.m["prerelease"] = True
        with self.assertRaisesRegex(ValueError, "notes path"):
            release.validate_manifest(self.m)
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self.m["notes"] = f'docs/releases/previews/v{self.m["version"]}-snap.md'
            notes = root / self.m["notes"]
            notes.parent.mkdir(parents=True)
            notes.write_text("Preview notes", encoding="utf-8")
            with patch.object(release, "ROOT", root):
                release.validate_manifest(self.m)
                self.m["prerelease"] = "true"
                with self.assertRaisesRegex(ValueError, "prerelease flag"):
                    release.validate_manifest(self.m)

    def add_setup(self):
        self.m["setup"] = {"artifactId": 98765, "artifactName": "FluentControl-Setup-x64",
                           "archiveSha256": "a" * 64, "sha256": "b" * 64, "bytes": 123,
                           "fileName": f'FluentControl-{self.m["version"]}-x64-Setup.exe'}

    def test_dual_installers_require_matching_source_and_complete_assets(self):
        self.add_setup()
        release.validate_manifest(self.m)
        entries = release.installer_manifests(self.m)
        self.assertEqual(len(entries), 2)
        run, artifact = self.source()
        setup_artifact = {**artifact, "id": self.m["setup"]["artifactId"],
                          "name": self.m["setup"]["artifactName"], "digest": "sha256:" + "a" * 64}
        release.validate_source(entries[1], run, setup_artifact, self.repo)
        setup_artifact["workflow_run"]["head_sha"] = "0" * 40
        with self.assertRaisesRegex(ValueError, "source mismatch"):
            release.validate_source(entries[1], run, setup_artifact, self.repo)
        expected, checksum = release.expected_assets(self.m)
        self.assertEqual(len(expected), 3)
        self.assertEqual(len(checksum.splitlines()), 2)
        release.validate_assets(self.assets(), expected, complete=True)
        with self.assertRaisesRegex(ValueError, "Missing"):
            release.validate_assets(self.assets()[:1], expected, complete=True)

    def test_setup_manifest_rejects_other_names_sources_and_versions(self):
        self.add_setup()
        for field, value in (("fileName", "other.exe"), ("artifactName", "untrusted"),
                             ("archiveSha256", "bad"), ("bytes", -1), ("sourceCommit", "a" * 40)):
            bad = copy.deepcopy(self.m)
            bad["setup"][field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                release.validate_manifest(bad)

    def test_bundle_archive_uses_the_same_integrity_gate(self):
        self.add_setup()
        with tempfile.TemporaryDirectory() as directory:
            work = Path(directory)
            archive = work / "setup.zip"
            entry = release.installer_manifests(self.m)[1]
            body = b"non-executable setup fixture"
            entry["bytes"] = len(body)
            entry["sha256"] = hashlib.sha256(body).hexdigest()
            with zipfile.ZipFile(archive, "w") as package:
                package.writestr(entry["fileName"], body)
            entry["archiveSha256"] = release.sha256(archive)
            self.assertEqual(release.unpack_msi(entry, archive, work).read_bytes(), body)
            entry["sha256"] = "0" * 64
            with self.assertRaises(ValueError):
                release.unpack_msi(entry, archive, work)

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

    def test_channel_mismatch_cannot_promote_or_demote_existing_release(self):
        for requested in (False, True):
            self.m["prerelease"] = requested
            published = {"draft": False, "prerelease": not requested,
                         "assets": self.assets(), "html_url": "release URL"}
            with patch.object(release, "find_release", return_value=published), \
                 patch.object(release, "verify_tag"), patch.object(release, "api") as api:
                with self.assertRaisesRegex(ValueError, "channel differs"):
                    release.publish(self.m, self.repo)
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

    @patch.dict(release.os.environ, {}, clear=True)
    def test_creation_uses_returned_id_without_read_after_create_tag_lookup(self):
        # Exercise both release shapes independently of the current manifest.
        for include_setup, preview in ((False, False), (True, False), (True, True)):
            with self.subTest(include_setup=include_setup, preview=preview):
                self.m["prerelease"] = preview
                self.m.pop("setup", None)
                if include_setup:
                    self.add_setup()
                run, artifact = self.source()
                entries = release.installer_manifests(self.m)
                artifacts = [{**artifact, "id": e["artifactId"], "name": e["artifactName"],
                              "digest": "sha256:" + e["archiveSha256"]} for e in entries]
                expected = self.assets()
                tag = "v" + self.m["version"] + ("-snap" if preview else "")
                draft = {"id": 42, "tag_name": tag, "draft": True, "prerelease": preview, "assets": []}
                uploaded = {**draft, "assets": self.assets()}
                published = {**uploaded, "draft": False, "html_url": "release URL"}
                api_responses = [run, *artifacts, draft, uploaded, published]
                def fake_gh(*args, **kwargs):
                    if "output" in kwargs:
                        return None
                    asset = expected.pop(0)
                    self.assertTrue(args[1].endswith("/releases/42/assets?name=" + asset["name"]))
                    self.assertEqual(Path(args[args.index("--input") + 1]).name, asset["name"])
                    return json.dumps(asset)
                with patch.object(release, "find_release", return_value=None) as find, \
                     patch.object(release, "verify_tag"), \
                     patch.object(release, "api", side_effect=api_responses) as api, \
                     patch.object(release, "gh", side_effect=fake_gh), \
                     patch.object(release, "unpack_msi", side_effect=lambda e, a, d: d / e["fileName"]):
                    release.publish(self.m, self.repo)
                    find.assert_called_once()
                    self.assertEqual(expected, [])
                    reads = [c.args[0] for c in api.call_args_list if "/actions/artifacts/" in c.args[0]]
                    self.assertEqual(reads, [f"repos/{self.repo}/actions/artifacts/{e['artifactId']}" for e in entries])
                    writes = [c for c in api.call_args_list if c.kwargs.get("method") in ("POST", "PATCH")]
                    self.assertEqual([c.kwargs["method"] for c in writes], ["POST", "PATCH"])
                    self.assertTrue(writes[1].args[0].endswith("/releases/42"))
                    self.assertEqual(writes[0].kwargs["payload"]["tag_name"], tag)
                    self.assertEqual(writes[0].kwargs["payload"]["prerelease"], preview)
                    self.assertEqual(writes[1].kwargs["payload"]["prerelease"], preview)
                    self.assertEqual(writes[1].kwargs["payload"]["make_latest"], "false" if preview else "true")


class ReleaseNotesTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        notes = self.root / "docs/releases"
        notes.mkdir(parents=True)
        self.body = "# FluentControl v0.3.50\n\n## 中文\n\n说明\n\n## English\n\nNotes\n"
        (notes / "v0.3.50.md").write_text(self.body, encoding="utf-8")
        root_patch = patch.object(release, "ROOT", self.root)
        root_patch.start()
        self.addCleanup(root_patch.stop)
        self.original = {
            "id": 42, "tag_name": "v0.3.50", "target_commitish": "a" * 40,
            "name": "FluentControl v0.3.50", "draft": False, "prerelease": False,
            "published_at": "2026-10-09T13:13:29Z", "body": "Old notes", "html_url": "release URL",
            "assets": [{"id": 7, "name": "installer.msi", "size": 100,
                        "digest": "sha256:" + "b" * 64, "state": "uploaded"}],
        }
        self.state = copy.deepcopy(self.original)
        self.tag = {"type": "commit", "sha": "a" * 40}

    def api(self, endpoint, optional=False, method="GET", payload=None):
        if "/git/ref/tags/" in endpoint:
            return {"object": copy.deepcopy(self.tag)}
        if method == "PATCH":
            self.assertEqual(endpoint, "repos/owner/repo/releases/42")
            self.assertEqual(payload, {"body": self.body})
            self.state.update(payload)
        else:
            self.assertEqual(method, "GET")
        return copy.deepcopy(self.state)

    def test_only_body_changes_and_retry_does_not_write(self):
        with patch.object(release, "api", side_effect=self.api) as api, \
             patch.object(release, "gh") as gh:
            release.sync_notes("owner/repo")
            self.assertEqual(self.state, {**self.original, "body": self.body})
            self.assertEqual(sum(c.kwargs.get("method") == "PATCH" for c in api.call_args_list), 1)
            api.reset_mock()
            release.sync_notes("owner/repo")
            self.assertFalse(any(c.kwargs.get("method") == "PATCH" for c in api.call_args_list))
            gh.assert_not_called()

    def test_unpublished_notes_never_create_a_release(self):
        with patch.object(release, "api", return_value=None) as api:
            release.sync_notes("owner/repo")
            api.assert_called_once_with("repos/owner/repo/releases/tags/v0.3.50", optional=True)

    def test_draft_prerelease_or_wrong_tag_cannot_be_updated(self):
        for field, value in (("draft", True), ("prerelease", True), ("tag_name", "v0.3.33")):
            with self.subTest(field=field), patch.dict(self.state, {field: value}), \
                 patch.object(release, "api", side_effect=self.api) as api:
                with self.assertRaisesRegex(ValueError, "matching published"):
                    release.sync_notes("owner/repo")
                self.assertFalse(any(c.kwargs.get("method") == "PATCH" for c in api.call_args_list))

    def test_concurrent_body_asset_or_title_edit_stops_before_write(self):
        for field, value in (("body", "Maintainer edit"), ("assets", []), ("name", "New title")):
            with self.subTest(field=field):
                changed = {**self.original, field: value}
                responses = [self.original, {"object": self.tag}, changed]
                with patch.object(release, "api", side_effect=responses) as api:
                    with self.assertRaisesRegex(ValueError, "changed during sync"):
                        release.sync_notes("owner/repo")
                    self.assertFalse(any(c.kwargs.get("method") == "PATCH" for c in api.call_args_list))

    def test_moved_tag_stops_before_write(self):
        moved = {"object": {**self.tag, "sha": "c" * 40}}
        responses = [self.original, {"object": self.tag}, self.original, moved]
        with patch.object(release, "api", side_effect=responses) as api:
            with self.assertRaisesRegex(ValueError, "tag changed"):
                release.sync_notes("owner/repo")
            self.assertFalse(any(c.kwargs.get("method") == "PATCH" for c in api.call_args_list))

    def test_post_write_verification_detects_unsaved_notes_and_asset_changes(self):
        for changed in (self.original, {**self.original, "body": self.body, "assets": []}):
            responses = [self.original, {"object": self.tag}, self.original,
                         {"object": self.tag}, {}, changed]
            with self.subTest(changed=changed), patch.object(release, "api", side_effect=responses):
                with self.assertRaises(ValueError):
                    release.sync_notes("owner/repo")

    def test_filename_heading_mismatch_stops_before_api_call(self):
        path = self.root / "docs/releases/v0.3.50.md"
        path.write_text("# FluentControl v0.3.33\n", encoding="utf-8")
        with patch.object(release, "api") as api:
            with self.assertRaisesRegex(ValueError, "heading differs"):
                release.sync_notes("owner/repo")
            api.assert_not_called()


if __name__ == "__main__":
    unittest.main()
