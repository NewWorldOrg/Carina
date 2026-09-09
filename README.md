# Carina

日本の地上デジタル放送向けの録画システムのバックエンド。

チューナーを占有して録画ファイルを書く特権プロセス `driver` と、その周りで HTTP API を出す非特権プロセス `app` の2つで動く。
別プロセスなので、app を入れ替えても進行中の録画は止まらない。

フロントエンドは [Vela](https://github.com/NewWorldOrg/Vela) で、稼働中の app が `GET /openapi/v1.json` に出す OpenAPI 文書からクライアントを生成する。

## 必要なもの

Docker のみ。
チューナーカードも B-CAS カードも要らない。
装置の代わりに合成のチューナーが動き、選局からセッションまで一通り歩ける。
ただしそれが返すのは決まった規則で作った TS パケットの列で、映像も PSI も入っていない。

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

API はコンテナの 8080 番で待ち受け、ホストの 8081 番に公開する(`API_PORT` で変える)。

スキーマの適用は `task migrate`(`Carina.Db --migrate`)で、app は起動時に当てない。
`--migrate` は PostgreSQL のアドバイザリロックを取るので、2つ並べて走らせないこと。

## driver の設定

`CARINA_DRIVER_CONFIG` が指す JSON ファイル1つだけを読む。
既定のパスは無く、読めない項目があればどれが悪いかを言って起動を止める。

| キー | 用途 |
| --- | --- |
| `socketPath` | app とつなぐ Unix ドメインソケット。`/run/` の下 |
| `socketGroupId` | ソケットの所有グループの id。1 以上 |
| `outputRoots` | 録画の書き出し先。`name` と絶対パスの `path` を1つ以上 |
| `shutdownGraceHours` | 停止を頼まれてから録画を書き終えるまで待つ時間。1〜168 |
| `liveSessionMinutes` | 視聴セッションの上限。1〜1440(既定 240) |
| `walkSessionMinutes` | 番組表の巡回セッションの上限。1〜240(既定 30) |
| `tuner.backend` | `dvb` か `fake` |
| `tuner.signalQualitySeconds` | frontend の統計を読む間隔。1〜3600(既定 10) |
| `tuner.demuxBufferBytes` | demux のリングバッファ。1MiB〜64MiB(既定 16MiB) |
| `devices` | チューナー。1つ以上、うち1つ以上が `enabled` |

`devices` の各要素は `id`、`kind`(`terrestrial` か `satellite`)、`enabled`、衛星なら `lnb` を持つ。

## app の設定

環境に依る値は埋め込まない。
不正な値があれば該当項目を示して起動を止める。

| 変数 | 用途 |
| --- | --- |
| `ConnectionStrings__Carina` | API が使う PostgreSQL の接続文字列。既定値は無く、未設定なら起動しない |
| `CARINA_DRIVER_SOCKET` | driver とつなぐソケットのパス。既定値は無く、未設定なら起動しない |
| `CARINA_DB_CONNECTION` | マイグレーション適用時の接続文字列 |
| `CARINA_MIGRATION_SOURCE_CONNECTION` | 置き換える録画環境の MySQL への接続文字列。`--carry` のときだけ読む。読み出しは読み取り専用トランザクションの中 |
| `CARINA_ROLE` | イメージが起動する役割 |
| `CARINA_PUBLIC_ORIGIN` | ブラウザがこのインストールに到達するアドレス(`https://host`)。ID プロバイダへ登録する redirect URI の出所。未設定ならリクエストの届いたアドレスからの推定になる |
| `CARINA_KNOWN_PROXIES` | `X-Forwarded-*` を信頼する前段のアドレス |
| `CARINA_KNOWN_NETWORKS` | 同じくネットワーク(アドレス/プレフィクス) |
| `CARINA_ANONYMOUS_NETWORKS` | 資格情報を運べない再生機器が置かれるネットワーク。既定は空 |
| `CARINA_DRI` | app のコンテナへ渡す映像処理装置のディレクトリ(`/dev/dri`)。未設定なら何も渡さない |
| `CARINA_DRI_VIDEO_GID` | `card0` の所有グループとしてコンテナへ渡す番号 |
| `CARINA_DRI_RENDER_GID` | `renderD128` の所有グループとしてコンテナへ渡す番号 |
| `CARINA_RECORDINGS_DIR` | 録画を書くホスト側のディレクトリ。未設定なら Docker のボリュームが持つ。移行がハードリンクで運ぶには、移行元と同じファイルシステムに置く |

### 映像処理装置

ハードウェアで映像を変換するには `/dev/dri` の `card0` と `renderD128` がコンテナの中に要る。
`task up` は起動のたびに `docker/dri-env.sh` でホストを見て、在れば渡し、無ければ何も渡さない。
`task dri` はいま検出できる値を表示する。

装置を渡すことと使うことは別の設定になっている。
ハードウェアで変換させるホストでは `.env` に `CARINA_TRANSCODING_PREFER=Vaapi` と書く(`.env.example` に雛形がある)。

### 映像の変換

ライブ視聴も未エンコードの録画の再生も、視聴1つにつき ffmpeg を1本起こす。
上限に達していれば次の視聴は起動せずに断られる。

| 設定 | 既定 | 用途 |
| --- | --- | --- |
| `Transcoding:AtOnce` | `4` | ライブと再生を合わせて同時に走らせる ffmpeg の本数。1 以上の整数 |
| `Transcoding:Prefer` | `Software` | 映像エンコーダ。`Software` か `Vaapi`。大文字小文字を含めてこの綴り |

`Vaapi` を選んでも描画ノードが無い、または開けない機体ではソフトウェアへ落ちる。
どちらで作ったかは再生では `Carina-Playback-Encoder` ヘッダが言う。

### 字幕

放送の字幕(ARIB STD-B24)は、映像を変換している ffmpeg そのものがサーバで絵にして WebSocket の字幕チャネルで配る。
字幕ストリームを持たないサービスでは、同じ受信のまま字幕なしで変換をやり直す。
補正のための設定は持たない。

### 番組表の収集

環境変数から与えるときは `Collection__BetweenSweeps` のように書く。
時間は `[d.]hh:mm:ss` で読む。

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

### 台帳と録画ファイルの突き合わせ

定期的に走る点検が食い違いを `integrity_check` と `integrity_finding` に残す。
点検はファイルを1つも消さず、書き換えもしない。
出力ルートは driver が名前で宣言するので、app からはどこにマウントされているかを教える。
app 側の読み取り専用マウントで足りる。

| 設定 | 既定 | 用途 |
| --- | --- | --- |
| `Integrity:OutputRoots` | 空 | 出力ルートの名前とマウント先(`primary=/srv/recordings;bulk=/mnt/bulk`)。空なら点検は走らない |
| `Integrity:BeforeFirstSweep` | `00:05:00` | 起動してから最初の点検までの間隔 |
| `Integrity:BetweenSweeps` | `06:00:00` | 点検と点検の間隔 |

### サムネイル

録画が終わると1枚だけ作る。
読み込み元は `Integrity:OutputRoots` の宣言をそのまま使い、絵は `Thumbnails:WrittenTo` の下に書く。

| 設定 | 既定 | 用途 |
| --- | --- | --- |
| `Thumbnails:WrittenTo` | 空 | 絵の置き場所(絶対パス)。空なら作らない |
| `Thumbnails:Programme` | `ffmpeg` | 起動する実行ファイル |
| `Thumbnails:BeforeFirstPass` | `00:01:00` | 起動してから最初の一巡までの間隔 |
| `Thumbnails:BetweenPasses` | `00:05:00` | 一巡と一巡の間隔 |
| `Thumbnails:AtMostAPass` | `8` | 一巡で作る枚数の上限 |
| `Thumbnails:NoLaterThan` | `00:02:00` | 抽出位置の上限 |
| `Thumbnails:OneOverAShareOf` | `3` | 尺の何分の1を抽出位置にするか |
| `Thumbnails:LongestRender` | `00:00:30` | 1枚に許す時間 |
| `Thumbnails:Width` | `960` | 絵の幅(偶数) |

### エンコード

成果物は app が書き込みで持つ別のルートに置き、録画が書かれた出力ルートには置かない。
コンテナで動かすならこのルートは app だけに読み書きで渡す(`compose.yml` の `encodes` ボリューム)。
`app` ロールは非特権ユーザーで動くので、そのユーザーが書けるディレクトリであることが条件。

| 設定 | 既定 | 用途 |
| --- | --- | --- |
| `Encodings:OutputRoots` | 空 | app が成果物を書くルートの名前とマウント先(`encodes=/srv/encodes`)。空なら何もエンコードされない |
| `Encodings:WorkedIn` | 空 | 作業ファイルの置き場所(絶対パス)。空なら成果物の隣。いずれかのルートと別のマウントなら起動しない |
| `Encodings:Prefer` | `Software` | 最初に頼むエンコーダ(`Software` / `Vaapi`)。無ければ実行時に劣化して結果に記録 |
| `Encodings:MostCores` | `2` | 1本のジョブが使うコア数の上限 |
| `Encodings:MostAttempts` | `3` | 再起動で巻き戻したジョブに与える試行回数 |
| `Encodings:BeforeFirstLook` | `00:00:15` | 起動してから最初に待機列を見るまでの間隔 |
| `Encodings:BetweenLooks` | `00:00:30` | 待機列を見る間隔 |
| `Encodings:StalledAfter` | `00:10:00` | 前進が無いまま待つ上限。超えると止めて失敗にする |

### 信号品質の採取

driver がセッションを保持しているあいだだけ貯める。
品質を測るためにチューナーを掴むことはない。

| 設定 | 既定 | 用途 |
| --- | --- | --- |
| `QualitySignal:BeforeFirstSample` | `00:00:30` | 起動してから最初に読むまでの間隔 |
| `QualitySignal:BetweenSamples` | `00:00:10` | 読みと読みの間隔 |
| `QualitySignal:BeforeFirstRollup` | `00:02:00` | 起動してから最初に集約するまでの間隔 |
| `QualitySignal:BetweenRollups` | `00:05:00` | 集約と集約の間隔 |
| `QualitySignal:KeepSamplesFor` | `7.00:00:00` | 生値を持つ期間 |
| `QualitySignal:KeepMinuteWindowsFor` | `90.00:00:00` | 分の窓を持つ期間。`forever` なら消さない |
| `QualitySignal:KeepHourWindowsFor` | `forever` | 時間の窓を持つ期間。`forever` なら消さない |

## イメージの役割

`Dockerfile` が生成するイメージは1つで、`docker/entrypoint.sh` が `CARINA_ROLE`(または第1引数)で役割を選ぶ。

| 役割 | 起動するもの |
| --- | --- |
| `driver` | 特権プロセス |
| `app` | HTTP プロセス |
| `migrate` | マイグレーションを適用して終了 |
| `web` | フロントエンド。配布用イメージのビルドが成果物を差し込む |
| `all` | 両プロセスを1コンテナで起動(開発用) |

`ffmpeg` と `libaribb25` は配布物のパッケージではなくイメージの中でソースから作る。
字幕を絵にするデコーダ(libaribcaption)を持つ配布物のパッケージが無いためで、フォント(Noto Sans CJK)も同じ理由で入れる。
VAAPI には `intel-media-va-driver` も要る。

`/api/*` を app へ、それ以外を web へ振り分けるのはイメージの外側の役割。
両者は同一オリジンに置くこと。
別オリジンになるとブラウザはセッション Cookie を送らず、iPadOS ではサードパーティ Cookie が遮断される。

## driver の操作

```bash
task probe:driver     # ヘルスチェック
task logs:driver
task restart:driver   # コード変更の反映
```

録画中の再起動は、その録画が終わるまで戻らない。
待つのは `shutdownGraceHours` までで、使い切れば録画を打ち切る。

実行環境が守る点が2つある。

- `stop_grace_period` は driver が申告する秒数より長くすること。短いと後処理の途中で SIGKILL される。秒数は `--shutdown-budget` を付けて起動すると表示される
- 再起動ポリシーに `on-failure` を使わないこと。要求による停止は終了コード 0 で、それ以外は 70。`on-failure` では意図的に停止したときに再起動しない

driver は呼び出し元を認証しない。
境界は Unix ドメインソケットのパーミッションと所有グループだけで、TCP ポートは開かない。
