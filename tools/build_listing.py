"""GitHub のリリースから VCC (VPM) の一覧 vpm.json を作る。

各リリースに「<id>-<version>.zip」と「package.json」が載っている前提。
package.json の中身に、zip の URL と SHA-256 を足したものを、その版の情報にする。

usage: python3 tools/build_listing.py --repo owner/name --out site/vpm.json
"""
import argparse
import hashlib
import json
import os
import urllib.request

LISTING = {
    "name": "Buicha.jp Packages",
    "id": "jp.buicha.packages",
    "url": "https://packages.buicha.jp/vpm.json",
    "author": "Buicha.jp",
}


def get(url, accept="application/vnd.github+json"):
    headers = {"Accept": accept, "User-Agent": "buicha-packages-listing"}
    token = os.environ.get("GH_TOKEN")
    if token and url.startswith("https://api.github.com/"):
        headers["Authorization"] = "Bearer " + token
    with urllib.request.urlopen(urllib.request.Request(url, headers=headers), timeout=60) as r:
        return r.read()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--repo", required=True)
    ap.add_argument("--out", required=True)
    args = ap.parse_args()

    packages = {}
    page = 1
    while True:
        releases = json.loads(get("https://api.github.com/repos/%s/releases?per_page=100&page=%d" % (args.repo, page)))
        if not releases:
            break
        for rel in releases:
            if rel.get("draft"):
                continue
            assets = {a["name"]: a for a in rel.get("assets", [])}
            manifest_asset = assets.get("package.json")
            if manifest_asset is None:
                continue
            manifest = json.loads(get(manifest_asset["url"], accept="application/octet-stream"))
            pid, version = manifest["name"], manifest["version"]
            zip_asset = assets.get("%s-%s.zip" % (pid, version))
            if zip_asset is None:
                print("skip %s: no zip" % rel["tag_name"])
                continue
            data = get(zip_asset["url"], accept="application/octet-stream")
            manifest["url"] = zip_asset["browser_download_url"]
            manifest["zipSHA256"] = hashlib.sha256(data).hexdigest()
            packages.setdefault(pid, {"versions": {}})["versions"][version] = manifest
            print("added %s %s" % (pid, version))
        page += 1

    listing = dict(LISTING)
    listing["packages"] = packages
    os.makedirs(os.path.dirname(args.out) or ".", exist_ok=True)
    with open(args.out, "w", encoding="utf-8", newline="\n") as f:
        json.dump(listing, f, ensure_ascii=False, indent=2)
        f.write("\n")


if __name__ == "__main__":
    main()
