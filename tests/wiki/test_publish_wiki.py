import importlib.util
import os
from pathlib import Path
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("publish_wiki", ROOT / "scripts/publish-wiki.py")
wiki = importlib.util.module_from_spec(spec)
spec.loader.exec_module(wiki)


class WikiTests(unittest.TestCase):
    def test_guide_has_valid_links_and_both_languages(self):
        pages = wiki.load_pages(ROOT / "docs/wiki")
        for name, content in pages.items():
            if not name.startswith("_"):
                self.assertIn("## English", content, name)
                self.assertRegex(content, r"[\u4e00-\u9fff]")
        self.assertIn("/wiki/Getting-Started)", pages["Home.md"])

    def test_link_rewrite_preserves_external_urls_and_anchors(self):
        content = "[one](Home.md#english) [two](https://example.org/a.md) [three](#local)"
        self.assertEqual(
            wiki.render_links(content, {"Home.md": ""}),
            f"[one](https://github.com/{wiki.REPOSITORY}/wiki/Home#english) "
            "[two](https://example.org/a.md) [three](#local)",
        )
        for target in ("Missing.md", "../LICENSE", "/Home.md"):
            with self.assertRaises(ValueError):
                wiki.render_links(f"[bad]({target})", {"Home.md": ""})

    def test_reject_symlink_source_and_destination(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            outside = root / "outside.md"
            outside.write_text("keep", encoding="utf-8")
            source = root / "source"
            source.mkdir()
            (source / "Home.md").symlink_to(outside)
            with self.assertRaises(ValueError):
                wiki.load_pages(source)
            wiki.git(["init", "--initial-branch=main", str(source)])
            with self.assertRaises(ValueError):
                wiki.sync_pages(source, {"Home.md": "overwrite"}, os.environ.copy())
            self.assertEqual(outside.read_text(encoding="utf-8"), "keep")

    def test_roundtrip_preserves_other_pages_and_noop_history(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            remote, checkout, verification = root / "remote.git", root / "wiki", root / "verify"
            env = os.environ.copy()
            wiki.git(["init", "--bare", "--initial-branch=main", str(remote)], env=env)
            wiki.git(["clone", str(remote), str(checkout)], env=env)
            original, _ = wiki.sync_pages(checkout, {"Custom.md": "maintainer page"}, env)
            expected, changed = wiki.sync_pages(checkout, {"Home.md": "guide", "_Sidebar.md": "nav"}, env)
            self.assertTrue(changed)
            self.assertNotEqual(expected, original)
            same, changed = wiki.sync_pages(checkout, {"Home.md": "guide", "_Sidebar.md": "nav"}, env)
            self.assertFalse(changed)
            self.assertEqual(same, expected)
            wiki.git(["clone", str(remote), str(verification)], env=env)
            self.assertEqual((verification / "Custom.md").read_text(), "maintainer page")
            self.assertEqual((verification / "Home.md").read_text(), "guide")
            self.assertEqual(wiki.git(["rev-parse", "HEAD"], cwd=verification, env=env), expected)


if __name__ == "__main__":
    unittest.main()
