import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

spec = importlib.util.spec_from_file_location("plugin_package", Path(__file__).parents[1] / "scripts/package.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class PackagingTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source = self.root / "publish"
        self.output = self.root / "plugin.spkg"
        self.source.mkdir()
        self.metadata = {"PluginID": "stable-id", "Version": "0.3.2", "ExecuteFileName": "plugin.dll", "IconPath": "icon.png"}
        self.write_metadata()
        for name in ("plugin.dll", "icon.png", "LICENSE", "Languages/en.json", "Languages/en.xaml",
                     "Languages/zh-cn.json", "Languages/zh-cn.xaml", "Languages/ja.json"):
            path = self.source / name
            path.parent.mkdir(exist_ok=True)
            path.write_bytes(b"test-content")

    def write_metadata(self):
        (self.source / "plugin.json").write_text(json.dumps(self.metadata), encoding="utf-8")

    def test_package_keeps_root_layout_and_all_languages(self):
        module.package(self.source, self.output)
        with zipfile.ZipFile(self.output) as archive:
            self.assertIn("Languages/ja.json", archive.namelist())
            self.assertEqual(archive.read("plugin.dll"), b"test-content")
            self.assertEqual(json.loads(archive.read("plugin.json")), self.metadata)

    def test_missing_entry_assembly_rejected(self):
        (self.source / "plugin.dll").unlink()
        with self.assertRaisesRegex(ValueError, "plugin.dll"):
            module.package(self.source, self.output)

    def test_missing_language_rejected(self):
        (self.source / "Languages/en.xaml").unlink()
        with self.assertRaisesRegex(ValueError, "Languages/en.xaml"):
            module.package(self.source, self.output)

    def test_entry_cannot_escape_package_root(self):
        self.metadata["ExecuteFileName"] = "../plugin.dll"
        self.write_metadata()
        with self.assertRaisesRegex(ValueError, "root filename"):
            module.package(self.source, self.output)

    def test_missing_entry_filename_rejected(self):
        del self.metadata["ExecuteFileName"]
        self.write_metadata()
        with self.assertRaisesRegex(ValueError, "ExecuteFileName"):
            module.package(self.source, self.output)

    def test_non_object_metadata_rejected(self):
        (self.source / "plugin.json").write_text("[]", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "JSON object"):
            module.package(self.source, self.output)

    def test_blank_identity_rejected(self):
        self.metadata["PluginID"] = "  "
        self.write_metadata()
        with self.assertRaisesRegex(ValueError, "PluginID"):
            module.package(self.source, self.output)

    def test_host_sdk_and_debug_symbols_not_shipped(self):
        for name in ("STranslate.Plugin.dll", "plugin.pdb"):
            (self.source / name).write_bytes(b"not-for-shipping")
        module.package(self.source, self.output)
        with zipfile.ZipFile(self.output) as archive:
            self.assertNotIn("STranslate.Plugin.dll", archive.namelist())
            self.assertNotIn("plugin.pdb", archive.namelist())

    def test_package_is_reproducible_and_checksummed(self):
        first = module.package(self.source, self.output)
        second = module.package(self.source, self.root / "other.spkg")
        self.assertEqual(first, second)
        self.assertTrue(self.output.with_suffix(".spkg.sha256").read_text().startswith(first))
