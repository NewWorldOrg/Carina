# Carina

地上デジタル放送向けの録画システムのバックエンドです。

チューナーを占有して録画ファイルを書く特権プロセス `driver` と、HTTP API を提供する非特権プロセス `app` の2つで動きます。
分けているのは、API を入れ替えても進行中の録画を止めないためです。

フロントエンドは [Vela](https://github.com/NewWorldOrg/Vela) で、稼働中の app が `GET /openapi/v1.json` で返す OpenAPI 文書からクライアントを生成します。
この文書は app が実行時に組み立てるものなので、Carina 側には置きません。

## 必要なもの

- Docker

開発にチューナーカードと B-CAS カードは不要です。
合成チューナーが固定内容のトランスポートストリームを生成します。

## セットアップ

```bash
task up      # driver / app / PostgreSQL
task build
task test
task lint    # dotnet format --verify-no-changes
```

Task を使わない場合:

```bash
docker compose up -d
docker compose exec app dotnet build
docker compose exec app dotnet test
```

API はコンテナ内のポート 8080 で待ち受け、ホストのポート 8081 に公開します(`API_PORT` で変更可)。

## 設定

環境依存の値は埋め込みません。
デバイス、出力先、ソケットのパス、データベース接続、ポートはすべて設定から読み、不正な値があれば該当項目を示して起動を停止します。
コミットする設定ファイルにはプレースホルダのみを置きます。

| 変数 | 用途 |
| --- | --- |
| `CARINA_DRIVER_CONFIG` | driver の設定ファイルのパス |
| `CARINA_DRIVER_SOCKET` | driver と app をつなぐ Unix ドメインソケットのパス |
| `ConnectionStrings__Carina` | API が使う PostgreSQL の接続文字列 |
| `CARINA_DB_CONNECTION` | マイグレーション適用時の接続文字列 |
| `CARINA_ROLE` | イメージが起動する役割 |
| `CARINA_KNOWN_PROXIES` | `X-Forwarded-*` を信頼する前段のアドレス |
| `CARINA_KNOWN_NETWORKS` | 同じくネットワーク(アドレス/プレフィクス) |
| `CARINA_PUBLIC_ORIGIN` | ブラウザがこのインストールに到達するアドレス(`https://host`) |

`CARINA_PUBLIC_ORIGIN` は ID プロバイダへ登録する redirect URI の出所です。
設定画面が案内する値と authorize / token へ送る値はどちらもここから組み立てるため、案内どおりに登録すれば認証は成立します。
未設定でも起動しますが、redirect URI はリクエストの届いたアドレスからの推定になり、設定画面はその値が推定であることを添えて返します。
画面を描くサーバが内部アドレスで API を呼ぶ構成ではこの推定はブラウザの辿らないアドレスになるため、公開しているアドレスを設定してください。

## イメージの役割

`Dockerfile` が生成するイメージは1つで、`docker/entrypoint.sh` が役割を選択します。

| 役割 | 起動するもの |
| --- | --- |
| `driver` | 特権プロセス |
| `app` | HTTP プロセス |
| `migrate` | マイグレーションを適用して終了 |
| `web` | フロントエンド。配布用イメージのビルドが成果物を差し込む |
| `all` | 両プロセスを1コンテナで起動(開発用) |

`/api/*` を app へ、それ以外を web へ振り分けるのはイメージの外側の役割です。
これは設計上の契約です。
別オリジンになるとブラウザはセッション Cookie を送らず、状態を変更するリクエストは `Origin` 検証で拒否され、iPadOS ではサードパーティ Cookie が遮断されます。

## driver の操作

```bash
task probe:driver     # ヘルスチェック
task logs:driver
task restart:driver   # コード変更の反映
```

録画中の再起動は、その録画が終わるまで戻りません。
`POST /api/driver/restart` は 409 を返して待たせません。

実行環境が守るべき点が2つあります。

- `stop_grace_period` は driver が申告する秒数より長くすること。短いと後処理の途中で SIGKILL されます。秒数は driver を `--shutdown-budget` 付きで起動すると表示され、通常の起動時にも同じ値を出力します
- 再起動ポリシーに `on-failure` を使わないこと。要求による停止は終了コード 0 のため、意図的に停止したときに再起動しません

## テスト

`dotnet test` で単体テスト・API のフィーチャテスト・アーキテクチャテストが動きます。

アーキテクチャテストはコンパイル結果ではなくプロジェクトファイルを読むため、宣言しただけで未使用の参照も検出します。
driver が共有契約を越えて参照していないこと、ドメインと放送規格パーサが依存を持たないこと、マイグレーション用プロジェクトを誰も参照していないことを確認します。
