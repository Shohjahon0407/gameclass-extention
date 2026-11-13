#!/usr/bin/env python3
"""Prepare the HTTPS update feed. The private key always stays outside the project/site."""
import argparse
from datetime import datetime
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
from zoneinfo import ZoneInfo

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("site", type=Path)
parser.add_argument("version")
parser.add_argument("installer", type=Path)
args = parser.parse_args()
key = Path(os.environ.get("MANAJOY_RELEASE_KEY", str(Path.home() / ".local/share/manajoy/release-signing/private.pem")))
public = Path(__file__).resolve().parent.parent / "src/Manajoy.GameClass.Bridge/Setup/release-public.pem"
if not key.is_file():
    parser.error(f"Release signing key is missing: {key}. Restore the original key; do not generate a replacement for an existing release channel.")
filename = f"ManajoyGameClass-Setup-{args.version}.exe"
if args.installer.name != filename or not args.installer.is_file():
    parser.error(f"Expected installer {filename}")
size = args.installer.stat().st_size
if not 0 < size <= 200_000_000:
    parser.error("Installer size is outside the update client's supported range")
with args.installer.open("rb") as file:
    digest = hashlib.file_digest(file, "sha256").hexdigest()
manifest = {
    "version": args.version,
    "released": datetime.now(ZoneInfo("Asia/Tashkent")).date().isoformat(),
    "file": f"download/{filename}",
    "size": size,
    "sha256": digest,
}
args.site.mkdir(parents=True, exist_ok=True)
with tempfile.TemporaryDirectory(prefix="manajoy-release-") as temporary:
    payload = Path(temporary) / "latest.json"
    signature = Path(temporary) / "latest.json.sig"
    payload.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    subprocess.run(["openssl", "dgst", "-sha256", "-sign", str(key), "-out", str(signature), str(payload)], check=True)
    # A different signing key would strand all installed clients: fail the release build immediately.
    subprocess.run(["openssl", "dgst", "-sha256", "-verify", str(public), "-signature", str(signature), str(payload)], check=True)
    download = args.site / "download" / filename
    download.parent.mkdir(parents=True, exist_ok=True)
    if args.installer.resolve() != download.resolve():
        shutil.copy2(args.installer, download)
    for source in (payload, signature):
        target = args.site / source.name
        staging = target.with_suffix(target.suffix + ".new")
        shutil.copyfile(source, staging)
        staging.replace(target)
print(f"Signed update feed ready: {args.site / 'latest.json'}")
