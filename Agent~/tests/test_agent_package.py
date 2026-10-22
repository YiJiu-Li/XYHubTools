import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import sys

# Tests must not add bytecode caches to the distributed skill.
sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
SKILL = ROOT / "skills/xy-unity-agent"
spec = importlib.util.spec_from_file_location("inspect_unity_project", SKILL / "scripts/inspect_project.py")
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)


class InventoryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.write("ProjectSettings/ProjectVersion.txt", "m_EditorVersion: 2022.3.53f1\n")
        self.write("Packages/manifest.json", json.dumps({"dependencies": {
            "com.rokid.xr.unity": "2.3.12", "com.yzj.xyhubtools": "https://private-user:secret@host/repo"}}))
        self.module = "Assets/Modules/example"
        self.write(self.module + "/ModuleConfigAsset.asset", "  Ident: example\n  Namespace: Example\n  ClassName: Entry\n")
        self.write(self.module + "/Main/Scripts/Example.asmdef", json.dumps({"name": "Example", "references": []}))
        self.write(self.module + "/Main/Res/Pfbs/example.prefab", "test fixture")

    def write(self, relative, text):
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")

    def test_project_inventory_and_private_sources(self):
        data = m.inspect(self.root)
        self.assertTrue(data["readOnly"])
        self.assertEqual(data["moduleDirectories"], ["example"])
        self.assertNotIn("secret", json.dumps(data))

    def test_module_identity_and_drift(self):
        self.write("Assets/Framework/Template/Foo.cs", "framework")
        self.write(self.module + "/Main/Scripts/Template/Foo.cs", "module fork")
        before = {p.relative_to(self.root).as_posix(): p.read_bytes() for p in self.root.rglob("*") if p.is_file()}
        data = m.inspect(self.root, "example")["module"]
        self.assertEqual(data["configFields"]["Ident"], "example")
        self.assertEqual(data["asmdefs"][0]["name"], "Example")
        self.assertEqual(data["templateComparison"]["sameRelativePathDifferentContent"], ["Foo.cs"])
        after = {p.relative_to(self.root).as_posix(): p.read_bytes() for p in self.root.rglob("*") if p.is_file()}
        self.assertEqual(before, after)

    def test_duplicate_guids_are_reported_not_rewritten(self):
        guid = "a" * 32
        self.write(self.module + "/one.meta", "guid: " + guid + "\n")
        self.write(self.module + "/two.meta", "guid: " + guid + "\n")
        self.assertEqual(len(m.inspect(self.root, "example")["module"]["duplicateGuidsWithinModule"][guid]), 2)

    def test_module_path_escape_is_rejected(self):
        for name in ("..", "../outside", "..\\outside", "/absolute", "C:\\outside"):
            with self.assertRaises(ValueError): m.inspect(self.root, name)

    def test_non_project_is_rejected(self):
        with self.assertRaises(ValueError): m.inspect(self.root / "Assets")


class PackageTests(unittest.TestCase):
    def test_manifest_distribution_is_complete(self):
        manifest = json.loads((ROOT / "agent.json").read_text(encoding="utf-8"))
        self.assertEqual(manifest["id"], "xy-unity-agent")
        self.assertEqual(len({w["id"] for w in manifest["workflows"]}), 6)
        self.assertEqual(set(manifest["additionalSkills"]), {"xy-unity-shader", "xy-agent-desktop-dev"})
        profiles = [manifest] + [json.loads((ROOT / "profiles" / (name + ".json")).read_text(encoding="utf-8"))
                                 for name in manifest["additionalSkills"]]
        for profile in profiles:
            skill = ROOT / "skills" / profile["id"]
            self.assertEqual(len(set(profile["files"])), len(profile["files"]))
            for relative in profile["files"]:
                self.assertTrue((skill / relative).is_file(), relative)
                self.assertNotIn("..", Path(relative).parts)
            actual = {p.relative_to(skill).as_posix() for p in skill.rglob("*") if p.is_file()}
            self.assertEqual(actual, set(profile["files"]))
            for markdown in skill.rglob("*.md"):
                import re
                for relative in re.findall(r"\]\(([^)]+)\)", markdown.read_text(encoding="utf-8")):
                    if "://" not in relative:
                        self.assertTrue((markdown.parent / relative).is_file(), relative)


if __name__ == "__main__":
    unittest.main()
