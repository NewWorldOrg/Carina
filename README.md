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
| `CARINA_ANONYMOUS_NETWORKS` | 資格情報を運べない再生機器が置かれるネットワーク(アドレス/プレフィクス)。既定は空 |

`CARINA_PUBLIC_ORIGIN` は ID プロバイダへ登録する redirect URI の出所です。
設定画面が案内する値と authorize / token へ送る値はどちらもここから組み立てるため、案内どおりに登録すれば認証は成立します。
未設定でも起動しますが、redirect URI はリクエストの届いたアドレスからの推定になり、設定画面はその値が推定であることを添えて返します。
画面を描くサーバが内部アドレスで API を呼ぶ構成ではこの推定はブラウザの辿らないアドレスになるため、公開しているアドレスを設定してください。

`CARINA_ANONYMOUS_NETWORKS` はセッションを保持できない再生機器のために置いた口で、既定は空です。
アドレスがセッションの代わりになる箇所はこのプロセスのどこにもないため、ここにネットワークを書いても資格情報を持たないリクエストは他と同じく拒否されます。
書かれていれば起動時にその内容を読み上げるので、忘れられた値が黙って残り続けることはありません。

### 番組表の収集

番組表を集める間隔と待ち時間は `Collection` セクションにあります。
環境変数から与えるときは `Collection__BetweenSweeps` のように書きます。
書かなかった項目は下の既定のままです。時間は `[d.]hh:mm:ss` で読み、読めない値・負の待ち時間・
長さを持たない間隔は該当項目を示して起動を停止します。

| 設定 | 既定 | 用途 |
| --- | --- | --- |
| `Collection:BetweenSweeps` | `00:30:00` | 一巡してから次の一巡までの間隔 |
| `Collection:WantedCoverage` | `8.00:00:00` | どこまで先まで埋まっていてほしいか |
| `Collection:RevisitsBelow` | `3.00:00:00` | これを割った放送を優先して取り直す |
| `Collection:BetweenVisits` | `06:00:00` | 同じ放送へ戻るまでの間隔 |
| `Collection:BeforeRetrying` | `02:00:00` | 取りそこねた放送を試し直すまでの間隔 |
| `Collection:LongestVisit` | `00:03:00` | 一度の訪問に許す長さ |
| `Collection:KeepEndedProgrammes` | `24:00:00` | 終わった番組を現用に残す長さ |
| `Collection:ArchiveRetention` | 無期限 | 書けばその長さで past 側を手放す |
| `Collection:LongestBackOff` | `24:00:00` | 届かない放送を待つ上限 |
| `Collection:BetweenBoosts` | `00:10:00` | 手で急かせる間隔 |
| `Collection:LongestBoost` | `00:30:00` | 急かした状態を続ける上限 |
| `Collection:RidesAlong` | `true` | 開いている受信に相乗りして集めるか |
| `Collection:BetweenRideAlongSaves` | `00:05:00` | 相乗りで集めた分を書き出す間隔 |
| `Collection:BetweenSessionChecks` | `00:00:30` | 相乗り先がまだ生きているかを見る間隔 |
| `Collection:WhenTunersAreFull` | `00:00:30` / `2` / `00:05:00` / `4` | チューナーが埋まっているときの待ち方(`FirstDelay` / `Factor` / `MaximumDelay` / `FailureCeiling`) |

`RidesAlong` を `false` にすると、録画や視聴で開いている受信からの吸い上げを行わなくなります。

### 台帳と録画ファイルの突き合わせ

定期的に走る点検が、録画の台帳と出力ルート配下の実ファイルを突き合わせ、食い違いを分類つきで
`integrity_check` と `integrity_finding` に残します。点検はファイルを1つも消さず、書き換えもしません。

出力ルートは driver が名前で宣言するもので、app からはどこにマウントされているかを
`Integrity:OutputRoots` で教えます。`名前=/絶対パス` を `;` で並べます。1つも書かなければ点検は
走らず、その旨を起動時に一度だけ書き残します。app 側の読み取り専用マウントで足ります。

| 設定 | 既定 | 用途 |
| --- | --- | --- |
| `Integrity:OutputRoots` | 空 | 出力ルートの名前とマウント先(`primary=/srv/recordings;bulk=/mnt/bulk`) |
| `Integrity:BeforeFirstSweep` | `00:05:00` | 起動してから最初の点検までの間隔 |
| `Integrity:BetweenSweeps` | `06:00:00` | 点検と点検の間隔 |

出力ルート配下は下まで歩きます。台帳が名前で指せるのはルート直下だけなので、サブディレクトリに
あるファイルは定義上すべて孤児で、ルートからの相対パスつきで報告します。

書き込み中の録画は突き合わせの対象外です。読めなかった出力ルートは、そこにある録画をまとめて
「無い」と呼ばずに、丸ごと判定から外します。中身の無いファイルは、台帳が `failed` と言っている
録画では食い違いになりません。台帳が `complete` と言っている録画が空だったときは、それとは別の
分類で残します。

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

driver は呼び出し元を認証しません。
これは欠落ではなく設計です。
秘密情報を持たないプロセスに認証を足すと特権側に秘密を置くことになるため、境界は Unix ドメインソケットのパーミッションと所有グループだけで担保します。
ソケットは所有グループの外に一切の権限を与えず、TCP ポートは開きません。
どちらもアーキテクチャテストが機械的に固定します。

## テスト

`dotnet test` で単体テスト・API のフィーチャテスト・アーキテクチャテストが動きます。

アーキテクチャテストはコンパイル結果ではなくプロジェクトファイルを読むため、宣言しただけで未使用の参照も検出します。
driver が共有契約を越えて参照していないこと、ドメインと放送規格パーサが依存を持たないこと、マイグレーション用プロジェクトを誰も参照していないことを確認します。
