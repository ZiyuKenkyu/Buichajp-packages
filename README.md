# Buicha.jp Packages

VRChat Creator Companion (VCC) で配布している Buicha.jp のパッケージです。
Buicha.jp packages for the VRChat Creator Companion (VCC).

## 追加のしかた / How to add

VCC の Settings → Packages → Add Repository に次の URL を入れます。
In VCC, open Settings → Packages → Add Repository and enter:

```
https://packages.buicha.jp/vpm.json
```

または、このリンクを開きます / Or open this link: `vcc://vpm/addRepo?url=https://packages.buicha.jp/vpm.json`

## パッケージ / Packages

| ID | 名前 / Name | 内容 / What it is |
|---|---|---|
| `jp.buicha.live.player` | Buicha Live Player | ぶいちゃライブの配信や YouTube などの動画を映す、ワールド用のスクリーンと操作パネル (4 チャンネル / ステージ、日本語 / English) |

使い方と利用条件は、パッケージの中の `README.txt` (日本語) / `README_EN.txt` (English) を見てください。
See `README.txt` (Japanese) / `README_EN.txt` (English) inside the package for usage and terms of use.

## 版の出し方 (管理者向け)

1. 開発用の Unity プロジェクトで `Tools/sync_vpm_repo.py` を実行し、`Packages/jp.buicha.live.player/` をこのリポジトリにコピーする。
2. `package.json` の `version` を上げてコミットし、push する。
3. GitHub の Actions → 「Release and publish listing」→ Run workflow。
   zip と package.json を載せたリリース (タグ = 版) を作り、全リリースから `vpm.json` を作り直して GitHub Pages に出す。
