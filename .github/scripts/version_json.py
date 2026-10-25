#!/usr/bin/env python3
"""The update feed: version.json, which the game reads to know whether a newer build is out (Gwent.Core.UpdateFeed).

version_json.py CHANGELOG.md <assets dir> <repo> <build number> <commit> > version.json

The build number (the count of commits) is the one the builds were stamped with; the notes are the newest section of
CHANGELOG.md, as the release's; each platform's archive has its download link (the latest release's, which stays the
same), its size and its SHA-256, which the game checks after downloading it.
"""
import json
import os
import sys

from release_notes import newest_section, sha256

# The feed's name for each platform, and its archive (the first one found; macOS's is a .tar.gz on a machine without zip)
ARCHIVES = {
    "windows": ["GwentLegacy-Windows.zip"],
    "macos": ["GwentLegacy-macOS.zip", "GwentLegacy-macOS.tar.gz"],
    "linux": ["GwentLegacy-Linux.tar.gz"],
    "android": ["GwentLegacy-Android.apk"],
    "web": ["GwentLegacy-Web.zip"],
}


def main():
    changelog, assets, repo, build, commit = sys.argv[1:6]
    title, body = newest_section(changelog)
    feed = {"build": int(build), "commit": commit[:8], "date": title, "notes": body}
    files = set(os.listdir(assets))
    for platform, names in ARCHIVES.items():
        name = next((n for n in names if n in files), None)
        if name is None:
            continue
        path = os.path.join(assets, name)
        feed[platform] = {
            "name": name,
            "url": f"https://github.com/{repo}/releases/latest/download/{name}",
            "size": os.path.getsize(path),
            "sha256": sha256(path),
        }
    json.dump(feed, sys.stdout, indent=2, ensure_ascii=False)
    print()


if __name__ == "__main__":
    main()
