"""Read-only Unity/module inventory. No Unity imports or external dependencies."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import sys


def inside(root, path):
    resolved = path.resolve()
    if resolved != root and root not in resolved.parents:
        raise ValueError("Path escapes project: " + str(path))
    return resolved


def read_text(root, path):
    return inside(root, path).read_text(encoding="utf-8-sig", errors="replace")


def digest(root, path):
    return hashlib.sha256(inside(root, path).read_bytes()).hexdigest()


def inspect(project, module=None):
    root = Path(project).resolve()
    if not (root / "Assets").is_dir() or not (root / "ProjectSettings/ProjectVersion.txt").is_file():
        raise ValueError("Expected a Unity project root")
    report = {"project": str(root), "readOnly": True}
    version = read_text(root, root / "ProjectSettings/ProjectVersion.txt")
    match = re.search(r"^m_EditorVersion:\s*(.+)$", version, re.M)
    report["unityVersion"] = match.group(1).strip() if match else None
    package_path = root / "Packages/manifest.json"
    deps = json.loads(read_text(root, package_path)).get("dependencies", {}) if package_path.is_file() else {}
    report["relevantPackages"] = {k: (v if re.fullmatch(r"[\w.+-]+", str(v)) else "custom source (inspect locally)")
                                  for k, v in deps.items() if any(x in k.lower() for x in ("rokid", "armaz", "timeline", "render-pipelines", "xyhub", "unitask"))}
    modules = root / "Assets/Modules"
    if modules.is_dir():
        inside(root, modules)
    report["moduleDirectories"] = sorted(p.name for p in modules.iterdir() if p.is_dir()) if modules.is_dir() else []
    if module is None:
        return report
    if not module or module in (".", "..") or any(c in module for c in ("/", "\\", ":")):
        raise ValueError("--module must be a module directory name")
    target = inside(root, modules / module)
    if not target.is_dir():
        raise ValueError("Module not found: " + module)
    config = target / "ModuleConfigAsset.asset"
    text = read_text(root, config) if config.is_file() else ""
    report["module"] = {"directory": module, "configFields": {
        k: v.strip() for k, v in re.findall(r"^\s*(Ident|Namespace|ClassName|PoiCode):\s*(.*?)$", text, re.M)
    }}
    report["module"]["asmdefs"] = []
    for path in sorted(target.rglob("*.asmdef")):
        data = json.loads(read_text(root, path))
        report["module"]["asmdefs"].append({"path": path.relative_to(root).as_posix(), "name": data.get("name"),
                                             "references": data.get("references", []), "includePlatforms": data.get("includePlatforms", [])})
    report["module"]["prefabs"] = [p.relative_to(root).as_posix() for p in sorted(target.rglob("*.prefab")) if inside(root, p)]
    by_guid = {}
    for path in sorted(target.rglob("*.meta")):
        match = re.search(r"^guid:\s*([a-fA-F0-9]{32})\s*$", read_text(root, path), re.M)
        if match: by_guid.setdefault(match.group(1).lower(), []).append(path.relative_to(root).as_posix())
    report["module"]["duplicateGuidsWithinModule"] = {k: v for k, v in by_guid.items() if len(v) > 1}
    framework = root / "Assets/Framework/Template"
    local = target / "Main/Scripts/Template"
    def sources(base):
        if not base.is_dir(): return {}
        inside(root, base)
        return {p.relative_to(base).as_posix(): digest(root, p) for p in base.rglob("*.cs")}
    common, fork = sources(framework), sources(local)
    report["module"]["templateComparison"] = {
        "frameworkRoot": "Assets/Framework/Template", "moduleRoot": local.relative_to(root).as_posix(),
        "sameRelativePathDifferentContent": sorted(k for k in common.keys() & fork.keys() if common[k] != fork[k]),
        "identical": sorted(k for k in common.keys() & fork.keys() if common[k] == fork[k]),
        "frameworkOnly": sorted(common.keys() - fork.keys()), "moduleOnly": sorted(fork.keys() - common.keys()),
        "note": "Relative-path comparison only; namespace/layout changes can appear as unrelated files. No runtime validation."
    }
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", required=True)
    parser.add_argument("--module")
    args = parser.parse_args()
    try:
        print(json.dumps(inspect(args.project, args.module), ensure_ascii=False, indent=2))
        return 0
    except (OSError, ValueError) as error:
        print(json.dumps({"ok": False, "error": str(error)}, ensure_ascii=False), file=sys.stderr)
        return 1


if __name__ == "__main__":
    if hasattr(sys.stdout, "reconfigure"): sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main())
