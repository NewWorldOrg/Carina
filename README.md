# Carina

日本のテレビ放送を録画して視聴するシステムのバックエンドです。\
番組表の収集・予約と自動予約のルール・録画・ライブと録画の配信・エンコードを HTTP API で提供します。\
チューナーの制御と B-CAS カードによるスクランブル解除は [Puppis](https://github.com/NewWorldOrg/Puppis) の driver が受け持ち、画面は [Vela](https://github.com/NewWorldOrg/Vela) が受け持ちます。

本番で動かすときは、3 つをまとめて構成した [Pyxis](https://github.com/NewWorldOrg/Pyxis) を使ってください。\
ここにはイメージの使い方と設定を書きます。

## 構成

| プロセス | 役割 |
| --- | --- |
| driver | 特権で動きます。チューナーを占有し、録画ファイルを書きます。Puppis のイメージです |
| app | 特権なしで動きます。HTTP API を提供します。このリポジトリのイメージです |
| PostgreSQL | データベースです |

driver と app は Unix ドメインソケットで通信します。\
別のプロセスなので、app を入れ替えても進行中の録画は止まりません。\
2 つの間のプロトコルは nuget.org のパッケージ `Puppis.Contracts` で、app は `Directory.Packages.props` でバージョンを固定しています。

API ドキュメントは、`ASPNETCORE_ENVIRONMENT=Development` で起動した app が `GET /openapi/v1.json` で返します。

## 動かす

イメージは `ghcr.io/newworldorg/carina` にあり、`v2.0.0` のようなバージョンのタグと `latest` が使えます。\
`master` にマージされるたびに `sha-<commit>` のタグでも公開します。\
対応する CPU は amd64 です。

### イメージの役割

`docker/entrypoint.sh` が `CARINA_ROLE` か第 1 引数で役割を選びます。

| 役割 | 起動するもの |
| --- | --- |
| `app` | HTTP サーバ。指定が無いときの既定です |
| `migrate` | データベースのスキーマを適用して終了します |

リバースプロキシを置き、同じオリジンで `/api/*` を app に、それ以外を Vela に振り分けます。\
WebSocket(`/api/live/ws`)と SSE(`/api/events`)はバッファせずに流し、`Range` ヘッダはそのまま通してください。\
root で起動すると、app は uid と gid を 10001 に切り替えて動きます。

## 設定

環境変数で設定します。主なものは次のとおりです。

| 変数 | 内容 |
| --- | --- |
| `ConnectionStrings__Carina` | PostgreSQL の接続文字列。必須です |
| `CARINA_DRIVER_SOCKET` | driver のソケットのパス。既定は `/run/carina/driver.sock` |
| `CARINA_DATA_PROTECTION_KEYS` | OIDC の client secret を暗号化する鍵を置くディレクトリ。必須です。データベースとは別にバックアップしてください |
| `CARINA_PUBLIC_ORIGIN` | ブラウザから見たサーバのアドレス(`https://host`)。OIDC の redirect URI の元になります。指定しないと通知の文面に画面へのリンクが付きません |
| `CARINA_KNOWN_NETWORKS` | `X-Forwarded-*` を信頼するリバースプロキシのネットワーク(`10.42.0.0/16` など)。指定しないと https として扱われず、セッション Cookie に Secure が付きません |
| `Integrity__OutputRoots` | driver の録画ディレクトリが app から見えるパス(`primary=/srv/recordings`) |
| `Encodings__OutputRoots` | エンコード済みファイルの保存先(`encodes=/srv/encodes`) |
| `Thumbnails__WrittenTo` / `Captions__WrittenTo` | サムネイルと字幕の保存先。空なら作りません |
| `Encodings__Prefer` / `Transcoding__Prefer` | エンコードと、ライブ・再生の変換に使うエンコーダー。`Software`・`Vaapi`・`Nvenc` のどれか。既定は `Software`。2 つに別のカードは指定できません |
| `Machine__RenderNode` | VAAPI で使う GPU のデバイス。既定は `/dev/dri/renderD128`。NVENC では使いません |
| `Encodings__Automatically` | 録画が終わったら自動でエンコードするか。既定は `true` |
| `Encodings__MostCores` | 1 本のエンコードが使うコア数の上限。既定は 2 |

番組表の収集間隔、録画の再試行、セッションの有効期間などは `Collection:` `Recording:` `RecordingRetry:` `Auth:` などの名前空間にあり、既定のままで動きます。\
品質の閾値とエンコードの自動実行は画面からも変えられます。

### GPU

`Vaapi` は Intel の GPU を `/dev/dri` のデバイスで渡します。\
`Nvenc` は NVIDIA のカードを NVIDIA Container Toolkit で渡します(`docker run --gpus all`、compose では `gpu-nvidia.yml` の重ね)。\
ホストの NVIDIA ドライバは 530.41.03 以上が必要です。\
イメージの ffmpeg は NVENC 対応で組んでありますが、NVIDIA のライブラリはイメージに入れず、コンテナランタイムがホストから差し込みます。\
差し込む範囲はイメージの `NVIDIA_DRIVER_CAPABILITIES=video,compute,utility` で決まっていて、`video` が無いと `libnvidia-encode` が入らず NVENC は使えません。\
カードが無い、ランタイムが無い、ドライバが古いときは起動時の確認で分かり、ソフトウェアのエンコードに切り替えて結果に記録します。

## 移行

既存の録画システムから録画とルールを引き継ぐ CLI があります。\
移行元と移行先を 1 つのマウントで渡し、`--for-real` を付けなければ何も変えずに確認だけします。

```bash
docker compose run --rm --no-deps -v /srv:/srv/carry \
  -e Integrity__OutputRoots=primary=/srv/carry/tv/recordings \
  app dotnet run --project src/Carina.Db -- --carry --from /srv/carry/epgstation/recorded --into /srv/carry/tv/recordings
```

移行元のデータベースは `CARINA_MIGRATION_SOURCE_CONNECTION` で指定します。\
結果は画面の移行記録に残ります。

## 開発

Docker があれば動きます。チューナーも B-CAS カードも不要で、合成チューナーで一通り試せます。

```bash
task up       # driver / app / PostgreSQL
task migrate  # スキーマの適用
task run:app  # API の起動
```

API はホストの 8081 番ポートで待ち受けます。\
ビルド、テスト、lint のコマンドは `CLAUDE.md` にあります。\
driver のコードは Puppis にあり、開発用の driver は `compose.yml` に書いた Puppis のイメージで動きます。

## ライセンス

AGPL-3.0-only です。著作権者は NewWorldOrg で、全文は `LICENSE` にあります。\
イメージに同梱したソフトウェアのライセンスは `THIRD-PARTY-NOTICES.md` と、イメージ内の `/usr/share/doc/carina/` にあります。
