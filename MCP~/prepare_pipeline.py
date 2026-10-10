"""Reproduce the compatibility source from a verified official archive + reviewed patches.

Never edits the installed package. The output directory must not already exist.
"""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import shutil
import subprocess
import tarfile
import tempfile


def prepare(archive, output, profile_path):
    archive, output, profile_path = Path(archive), Path(output).resolve(), Path(profile_path).resolve()
    profile = json.loads(profile_path.read_text(encoding="utf-8-sig"))
    if hashlib.sha256(archive.read_bytes()).hexdigest() != profile["upstreamArchiveSha256"]:
        raise ValueError("Official archive SHA-256 differs from the reviewed profile")
    if output.exists():
        raise ValueError("Output must be a new directory; installed sources are never overwritten")
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="xy-pipeline-", dir=output.parent) as temporary:
        staging = Path(temporary)
        with tarfile.open(archive) as tar:
            for member in tar.getmembers():
                parts = PurePosixPath(member.name).parts
                if not parts or parts[0] != "package" or any(p in ("..", ".") or ":" in p or "\\" in p for p in parts):
                    raise ValueError("Unsafe archive path: " + member.name)
                if member.isdir():
                    continue
                if not member.isfile():
                    raise ValueError("Archive links/devices are not supported: " + member.name)
                destination = staging.joinpath(*parts[1:])
                if not destination.resolve().is_relative_to(staging.resolve()):
                    raise ValueError("Archive path escaped staging directory")
                destination.parent.mkdir(parents=True, exist_ok=True)
                destination.write_bytes(tar.extractfile(member).read())
        package = json.loads((staging / "package.json").read_text(encoding="utf-8-sig"))
        if package.get("name") != "com.unity.pipeline" or package.get("version") != profile["upstreamVersion"]:
            raise ValueError("Archive package identity does not match the profile")
        patch_hashes = {}
        for relative in profile["patches"]:
            patch = (profile_path.parent / relative).resolve()
            if not patch.is_relative_to(profile_path.parent):
                raise ValueError("Patch must be inside the profile directory")
            patch_hashes[relative] = hashlib.sha256(patch.read_bytes()).hexdigest()
            subprocess.run(["git", "apply", "--check", str(patch)], cwd=staging, check=True)
            subprocess.run(["git", "apply", str(patch)], cwd=staging, check=True)
        # Patched packages must not carry an upstream signature that no longer matches.
        for signature in (".signature", ".attestation.p7m"):
            (staging / signature).unlink(missing_ok=True)
        receipt = {"upstreamArchiveSha256": profile["upstreamArchiveSha256"], "patches": patch_hashes}
        (staging / "xy-source-provenance.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
        shutil.move(str(staging), str(output))
    return receipt


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--archive", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--profile", default=str(Path(__file__).resolve().parents[1] / "Pipeline~" / "compatibility.json"))
    args = parser.parse_args()
    print(json.dumps(prepare(args.archive, args.output, args.profile), indent=2))
