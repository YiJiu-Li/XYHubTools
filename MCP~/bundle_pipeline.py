"""Build a deterministic UPM archive from the maintained Pipeline source tree.

Usage: python bundle_pipeline.py --source <com.unity.pipeline directory>
Only writes XYHubTools/Pipeline~; it never modifies the source package.
"""
import argparse
import gzip
import hashlib
import io
import json
from pathlib import Path
import tarfile


def build(source, output):
    source, output = Path(source), Path(output)
    package = json.loads((source / "package.json").read_text(encoding="utf-8-sig"))
    if package.get("name") != "com.unity.pipeline":
        raise ValueError("Expected com.unity.pipeline source")
    upstream_version = package["version"]
    package["version"] = "0.8.0-xy.1"
    package["unity"] = "2022.3"
    package["displayName"] = "Unity Pipeline (XY distribution)"
    package.pop("_upm", None)
    package["xyDistribution"] = {"upstreamVersion": upstream_version,
                                  "compatibilityBaseline": "2022.3"}
    output.mkdir(parents=True, exist_ok=True)
    archive = output / "pipeline.tgz"
    with archive.open("wb") as raw, gzip.GzipFile(filename="", mode="wb", fileobj=raw, mtime=0) as gz:
        with tarfile.open(fileobj=gz, mode="w") as tar:
            for path in sorted(source.rglob("*")):
                if not path.is_file() or path.name in (".signature", ".attestation.p7m"):
                    continue
                relative = path.relative_to(source).as_posix()
                content = (json.dumps(package, ensure_ascii=False, indent=2) + "\n").encode("utf-8") if relative == "package.json" else path.read_bytes()
                info = tarfile.TarInfo("package/" + relative)
                info.size = len(content)
                info.mode = 0o644
                tar.addfile(info, io.BytesIO(content))
    metadata = {"name": package["name"], "version": package["version"],
                "upstreamVersion": upstream_version, "unity": package["unity"],
                "sha256": hashlib.sha256(archive.read_bytes()).hexdigest()}
    (output / "bundle.json").write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(metadata))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", required=True)
    parser.add_argument("--output", default=str(Path(__file__).resolve().parents[1] / "Pipeline~"))
    args = parser.parse_args()
    build(args.source, args.output)
