"""Build a deterministic UPM archive from the maintained Pipeline source tree.

Usage: python bundle_pipeline.py --source <directory produced by prepare_pipeline.py>
Only writes XYHubTools/Pipeline~; it never modifies the source package.
"""
import argparse
import gzip
import hashlib
import io
import json
from pathlib import Path
import re
import tarfile


def build(source, output, profile):
    source, output = Path(source), Path(output)
    profile_path = Path(profile).resolve()
    profile = json.loads(profile_path.read_text(encoding="utf-8-sig"))
    package = json.loads((source / "package.json").read_text(encoding="utf-8-sig"))
    if package.get("name") != "com.unity.pipeline":
        raise ValueError("Expected com.unity.pipeline source")
    upstream_version = package["version"]
    upstream_unity = package.get("unity")
    if upstream_version != profile.get("upstreamVersion"):
        raise ValueError("Source version differs from the reviewed compatibility profile; review upstream changes first")
    distribution_version = profile.get("version", "")
    if not re.fullmatch(r"\d+\.\d+\.\d+-xy\.\d+", distribution_version):
        raise ValueError("Profile version must be an explicit x.y.z-xy.N version")
    if not re.fullmatch(r"\d+\.\d+", profile.get("unity", "")) or not profile.get("validatedUnityVersions") or not profile.get("compatibilityRevision"):
        raise ValueError("Profile requires unity, validatedUnityVersions and compatibilityRevision")
    provenance = json.loads((source / "xy-source-provenance.json").read_text(encoding="utf-8-sig"))
    expected_patches = {p: hashlib.sha256((profile_path.parent / p).read_bytes()).hexdigest() for p in profile["patches"]}
    if provenance.get("upstreamArchiveSha256") != profile["upstreamArchiveSha256"] or provenance.get("patches") != expected_patches:
        raise ValueError("Source provenance differs from the reviewed profile; rerun prepare_pipeline.py")
    source_hash = hashlib.sha256()
    for path in sorted(source.rglob("*")):
        if path.is_file() and path.name not in (".signature", ".attestation.p7m"):
            source_hash.update(path.relative_to(source).as_posix().encode() + b"\0")
            source_hash.update(hashlib.sha256(path.read_bytes()).digest())
    package["version"] = distribution_version
    package["unity"] = profile["unity"]
    package["displayName"] = "Unity Pipeline (XY distribution)"
    package.pop("_upm", None)
    package["xyDistribution"] = {"upstreamVersion": upstream_version,
                                  "compatibilityBaseline": profile["unity"],
                                  "compatibilityRevision": profile["compatibilityRevision"],
                                  "validatedUnityVersions": profile["validatedUnityVersions"],
                                  "sourceTreeSha256": source_hash.hexdigest()}
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
                "upstreamUnity": upstream_unity,
                "upstreamRevision": package.get("repository", {}).get("revision"),
                "compatibilityRevision": profile["compatibilityRevision"],
                "validatedUnityVersions": profile["validatedUnityVersions"],
                "sourceTreeSha256": source_hash.hexdigest(),
                "upstreamArchiveSha256": provenance["upstreamArchiveSha256"],
                "patches": provenance["patches"],
                "sha256": hashlib.sha256(archive.read_bytes()).hexdigest()}
    (output / "bundle.json").write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(metadata))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", required=True)
    parser.add_argument("--profile", default=str(Path(__file__).resolve().parents[1] / "Pipeline~" / "compatibility.json"))
    parser.add_argument("--output", default=str(Path(__file__).resolve().parents[1] / "Pipeline~"))
    args = parser.parse_args()
    build(args.source, args.output, args.profile)
