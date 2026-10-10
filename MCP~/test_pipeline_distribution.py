import contextlib
import hashlib
import io
import json
from pathlib import Path
import tarfile
import tempfile
import unittest
import bundle_pipeline
import prepare_pipeline


class DistributionTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.archive = self.root / "official.tgz"
        self.write_archive({"package/package.json": json.dumps({"name": "com.unity.pipeline", "version": "9.1.0", "unity": "6000.3"}),
                            "package/.signature": "signed-original"})
        self.profile = self.root / "profile.json"
        self.profile.write_text(json.dumps({"upstreamVersion": "9.1.0", "version": "9.1.0-xy.2", "unity": "2022.3",
            "validatedUnityVersions": ["2022.3.53f1"], "compatibilityRevision": "test-2", "patches": [],
            "upstreamArchiveSha256": hashlib.sha256(self.archive.read_bytes()).hexdigest()}))

    def write_archive(self, files):
        with tarfile.open(self.archive, "w:gz") as archive:
            for name, text in files.items():
                data = text.encode()
                info = tarfile.TarInfo(name)
                info.size = len(data)
                archive.addfile(info, io.BytesIO(data))

    def test_prepare_and_build_are_reproducible_and_profile_driven(self):
        source = self.root / "source"
        prepare_pipeline.prepare(self.archive, source, self.profile)
        self.assertFalse((source / ".signature").exists())
        with contextlib.redirect_stdout(io.StringIO()):
            bundle_pipeline.build(source, self.root / "out1", self.profile)
            bundle_pipeline.build(source, self.root / "out2", self.profile)
        self.assertEqual((self.root / "out1/pipeline.tgz").read_bytes(), (self.root / "out2/pipeline.tgz").read_bytes())
        with tarfile.open(self.root / "out1/pipeline.tgz") as archive:
            package = json.load(archive.extractfile("package/package.json"))
        self.assertEqual(package["version"], "9.1.0-xy.2")
        self.assertEqual(package["unity"], "2022.3")
        self.assertEqual(json.loads((source / "package.json").read_text())["version"], "9.1.0")

    def test_wrong_archive_and_existing_output_are_rejected(self):
        existing = self.root / "existing"
        existing.mkdir()
        with self.assertRaises(ValueError):
            prepare_pipeline.prepare(self.archive, existing, self.profile)
        self.archive.write_bytes(b"wrong")
        with self.assertRaises(ValueError):
            prepare_pipeline.prepare(self.archive, self.root / "other", self.profile)

    def test_new_upstream_version_requires_profile_review(self):
        source = self.root / "source"
        prepare_pipeline.prepare(self.archive, source, self.profile)
        package = source / "package.json"
        data = json.loads(package.read_text())
        data["version"] = "10.0.0"
        package.write_text(json.dumps(data))
        with self.assertRaises(ValueError):
            bundle_pipeline.build(source, self.root / "out", self.profile)

    def test_archive_path_traversal_rejected_before_output(self):
        self.write_archive({"package/../../escape.txt": "escape"})
        profile = json.loads(self.profile.read_text())
        profile["upstreamArchiveSha256"] = hashlib.sha256(self.archive.read_bytes()).hexdigest()
        self.profile.write_text(json.dumps(profile))
        with self.assertRaises(ValueError):
            prepare_pipeline.prepare(self.archive, self.root / "source", self.profile)
        self.assertFalse((self.root / "source").exists())
        self.assertFalse((self.root / "escape.txt").exists())

    def test_stale_provenance_rejected(self):
        source = self.root / "source"
        prepare_pipeline.prepare(self.archive, source, self.profile)
        receipt = source / "xy-source-provenance.json"
        receipt.write_text('{"upstreamArchiveSha256":"wrong", "patches":{}}')
        with self.assertRaises(ValueError):
            bundle_pipeline.build(source, self.root / "out", self.profile)


if __name__ == "__main__":
    unittest.main()
