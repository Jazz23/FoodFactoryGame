"""Copy runtime art and convert Unity FBX models to glTF binary using Blender CLI."""
from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
from pathlib import Path


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--blender", default="blender", help="Blender executable, or full path")
    parser.add_argument("--copy-only", action="store_true", help="Copy textures/icons/audio, skip FBX conversion")
    args = parser.parse_args()
    project = args.project.resolve()
    unity_assets = project / "Assets"
    out = Path(__file__).resolve().parents[1] / "assets"

    # Keep source-relative paths so the manifest can be traced back to Unity authoring.
    copy_roots = [unity_assets / "Art", unity_assets / "Audio" / "Resources" / "Audio"]
    copied = 0
    for root in copy_roots:
        if not root.exists():
            continue
        for source in root.rglob("*"):
            if not source.is_file() or source.suffix.lower() not in {".png", ".jpg", ".jpeg", ".tga", ".wav", ".ogg", ".mp3", ".flac"}:
                continue
            rel = source.relative_to(unity_assets)
            # Images are all useful art/UI assets; exclude screenshots and non-runtime tutorial imagery.
            if "Temp" in rel.parts or "TutorialInfo" in rel.parts:
                continue
            target = out / rel
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, target)
            copied += 1

    if not args.copy_only:
        models = unity_assets / "Art"
        blender_script = Path(__file__).resolve().with_name("_blender_fbx_to_glb.py")
        converted = 0
        for source in sorted(models.rglob("*.fbx")):
            rel = source.relative_to(models)
            target = out / "models" / rel.with_suffix(".glb")
            # The sandbox's established player.glb is intentionally never replaced.
            target.parent.mkdir(parents=True, exist_ok=True)
            command = [args.blender, "--background", "--factory-startup", "--python", str(blender_script), "--", str(source), str(target)]
            subprocess.run(command, check=True)
            converted += 1
        print(f"Converted {converted} FBX models; copied {copied} runtime image/audio files to {out}")
    else:
        print(f"Copied {copied} runtime image/audio files to {out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
