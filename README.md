# Carina

日本の地上デジタル放送向けの録画システムのバックエンド。

チューナーを占有して録画ファイルを書く特権プロセス `driver` と、その周りで HTTP API を出す
非特権プロセス `app` の2つで動く。別プロセスなので、app を入れ替えても進行中の録画は止まらない。

フロントエンドは [Vela](https://github.com/NewWorldOrg/Vela) で、稼働中の app が
`GET /openapi/v1.json` に出す OpenAPI 文書からクライアントを生成する。この文書は app が
実行時に組み立てるものなので、このリポジトリには置いていない。

## 必要なもの

Docker のみ。開発にチューナーカードも B-CAS カードも要らない。装置の代わりに合成のチューナーが
動き、選局からセッションまで一通り歩ける。ただしそれが返すのは決まった規則で作った TS パケットの
列で、映像も PSI も入っていない。

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
`task up` の初回は `Dockerfile` の `develop` と `driver-develop` のビルドが走る。

スキーマの適用は `task migrate`(`Carina.Db --migrate`)で、app は起動時に当てない。
`--migrate` は PostgreSQL のアドバイザリロックを取る。2つ並べても壊れないが、遅いほうは
見えないロックを待つだけなので、並べて走らせないこと。

## driver の設定

driver は `CARINA_DRIVER_CONFIG` が指す JSON ファイル1つだけを読む。既定のパスは無い。
読めない項目があれば、どれが悪いかを言って起動を止める。

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

`devices` の各要素は `id`、`kind`(`terrestrial` か `satellite`)、`enabled`、衛星なら
`lnbPower` を持つ。`backend` が `dvb` のときは `devicePath` に frontend のノード
(`/dev/dvb/adapterN/frontendM`)を書く。同じ adapter の demux と dvr はそこから導く。

開発用の `compose.yml` はチューナーを1つも渡さず、`docker/driver.development.json` の
`fake` バックエンドを使う。実機では、装置と機体向けの設定ファイルをコミット対象外の
`compose.override.yml` で渡す。スクランブル解除は `dvb` バックエンドのときだけ試み、
カードリーダはホストの pcscd のソケットを driver へ渡して届かせる。

## app の設定

環境に依る値は埋め込まない。デバイス、出力先、ソケットのパス、データベース接続、ポートは
すべて設定から読み、不正な値があれば該当項目を示して起動を止める。コミットする設定ファイルには
プレースホルダのみを置く。

| 変数 | 用途 |
| --- | --- |
| `ConnectionStrings__Carina` | API が使う PostgreSQL の接続文字列。既定値は無く、未設定なら起動しない |
| `CARINA_DRIVER_SOCKET` | driver とつなぐソケットのパス。既定値は無く、未設定なら起動しない |
| `CARINA_DB_CONNECTION` | マイグレーション適用時の接続文字列 |
| `CARINA_MIGRATION_SOURCE_CONNECTION` | 置き換える録画環境の MySQL への接続文字列。`--carry` のときだけ読む |
| `CARINA_ROLE` | イメージが起動する役割 |
| `CARINA_PUBLIC_ORIGIN` | ブラウザがこのインストールに到達するアドレス(`https://host`) |
| `CARINA_KNOWN_PROXIES` | `X-Forwarded-*` を信頼する前段のアドレス |
| `CARINA_KNOWN_NETWORKS` | 同じくネットワーク(アドレス/プレフィクス) |
| `CARINA_ANONYMOUS_NETWORKS` | 資格情報を運べない再生機器が置かれるネットワーク(アドレス/プレフィクス)。既定は空 |
| `CARINA_DRI` | app のコンテナへ渡す映像処理装置のディレクトリ(`/dev/dri`)。未設定なら何も渡さない |
| `CARINA_DRI_VIDEO_GID` | `card0` の所有グループとしてコンテナへ渡す番号 |
| `CARINA_DRI_RENDER_GID` | `renderD128` の所有グループとしてコンテナへ渡す番号 |

`CARINA_PUBLIC_ORIGIN` は ID プロバイダへ登録する redirect URI の出所で、設定画面が案内する値も
authorize / token へ送る値もここから組み立てる。未設定でも起動するが、redirect URI はリクエストの
届いたアドレスからの推定になり、設定画面はその値が推定であることを添えて返す。画面を描くサーバが
内部アドレスで API を呼ぶ構成では推定がブラウザの辿らないアドレスになるので、公開しているアドレスを書く。

`CARINA_MIGRATION_SOURCE_CONNECTION` は `Carina.Db --carry` が置き換え元を読むためだけの設定で、
ホスト・データベース・アカウントのどれにも既定値が無い。未設定なら移行は走らずその旨を言って終わる。
読み出しは `START TRANSACTION READ ONLY` の中で行うので、渡した資格情報が書ける立場でも移行元へは
書き込まない。

`CARINA_ANONYMOUS_NETWORKS` はセッションを持てない再生機器のために置いた口で、既定は空。
アドレスがセッションの代わりになる箇所はこのプロセスに無いため、ここに書いても資格情報を持たない
リクエストは他と同じく拒まれる。書かれていれば起動時にその内容を読み上げる。

### 映像処理装置

ハードウェアで映像を変換するには `/dev/dri` の `card0` と `renderD128` がコンテナの中に要る。
`task up` は起動のたびに `docker/dri-env.sh` でホストを見て、在れば渡し、無ければ何も渡さない。
`task dri` はいま検出できる値を表示する。何も出なければそのホストに装置は無い。

グループは名前ではなく番号で渡す。`renderD128` の所有グループはディストリビューションによって
番号が違い、コンテナの `/etc/group` に無いこともある。何も検出できないときに渡るのは `0` と
`65534` で、どちらも権限を足さない。

装置を渡すことと使うことは別の設定になっている。compose は `Transcoding__Prefer` を
`CARINA_TRANSCODING_PREFER` から読み、既定は `Software`。ハードウェアで変換させるホストでは
`.env` に `CARINA_TRANSCODING_PREFER=Vaapi` と書く(`.env.example` に雛形がある)。

### 映像の変換本数

ライブ視聴も未エンコードの録画の再生も、視聴1つにつき ffmpeg を1本起こす。同時に走らせる本数の
上限は `Transcoding:AtOnce` で、ライブと再生を分けずに1つの予算として数える。上限に達していれば
次の視聴は起動せずに断られる。ライブの断りは何本が走っていて上限が何本かを言い、再生の断りは言わない。

エンコーダは `Transcoding:Prefer` で選ぶ。`Software` は x264 でビットレート上限を切り、`Vaapi` は
`/dev/dri` の描画ノードで H.264 を作って上限の代わりに固定の量子化パラメータを使う。`Vaapi` を
選んでも描画ノードが無い、または開けない機体ではソフトウェアへ落ちる。どちらで作ったかは、再生では
`Carina-Playback-Encoder` ヘッダが言う。デコードとインタレース解除は常にソフトウェア。

| 設定 | 既定 | 用途 |
| --- | --- | --- |
| `Transcoding:AtOnce` | `4` | ライブと再生を合わせて同時に走らせる ffmpeg の本数。1 以上の整数 |
| `Transcoding:Prefer` | `Software` | 映像エンコーダ。`Software` か `Vaapi`。大文字小文字を含めてこの綴り |

どちらも他の値を書くと起動を止める。

### 字幕

放送の字幕(ARIB STD-B24)は、ブラウザに渡す前にサーバで絵にする。描くのは映像を変換している
ffmpeg そのもので、libaribcaption が放送どおりに描いた絵をパレット PNG にして WebSocket の
字幕チャネルで配る。キャンバスの大きさはチャネルのヘッダで先に届き、受け側は絵をキャンバスから
表示領域へ引き伸ばして重ねる。途中から観る人には、いま出ている字幕を1枚だけ取り置いてヘッダの
直後に送る。字幕が消えたときは空の字幕フレームが届く。

字幕ストリームを持たないサービスでは、字幕を頼まれた ffmpeg が映像ごと組み立てを断る。その場合は
同じ受信のまま字幕なしで変換をやり直し、その局の受信が続く間は以後の変換も字幕を頼まない。
補正のための設定は持たない。

### 番組表の収集

集める間隔と待ち時間は `Collection` にある。環境変数から与えるときは `Collection__BetweenSweeps`
のように書く。書かなかった項目は下の既定のまま。時間は `[d.]hh:mm:ss` で読み、読めない値・負の
待ち時間・長さを持たない間隔は該当項目を示して起動を止める。

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

`RidesAlong` を `false` にすると、録画や視聴で開いている受信からの吸い上げを行わなくなる。

### 台帳と録画ファイルの突き合わせ

定期的に走る点検が、録画の台帳と出力ルート配下の実ファイルを突き合わせ、食い違いを分類つきで
`integrity_check` と `integrity_finding` に残す。点検はファイルを1つも消さず、書き換えもしない。

出力ルートは driver が名前で宣言するもので、app からはどこにマウントされているかを
`Integrity:OutputRoots` で教える。`名前=/絶対パス` を `;` で並べる。1つも書かなければ点検は走らず、
その旨を起動時に一度だけ書き残す。app 側の読み取り専用マウントで足りる。

| 設定 | 既定 | 用途 |
| --- | --- | --- |
| `Integrity:OutputRoots` | 空 | 出力ルートの名前とマウント先(`primary=/srv/recordings;bulk=/mnt/bulk`) |
| `Integrity:BeforeFirstSweep` | `00:05:00` | 起動してから最初の点検までの間隔 |
| `Integrity:BetweenSweeps` | `06:00:00` | 点検と点検の間隔 |

出力ルート配下は下まで歩く。台帳が名前で指せるのはルート直下だけなので、サブディレクトリにある
ファイルは定義上すべて孤児で、ルートからの相対パスつきで報告する。

書き込み中の録画は突き合わせの対象外。読めなかった出力ルートは、そこにある録画をまとめて「無い」と
呼ばずに、丸ごと判定から外す。中身の無いファイルは、台帳が `failed` と言っている録画では食い違いに
ならない。台帳が `complete` と言っている録画が空だったときは、それとは別の分類で残す。

### サムネイル

録画が終わると、その録画のサムネイルを1枚だけ作る。`failed` で終わった録画には作らない
(絵があること自体が「録れている」という主張になるため)。`truncated` には作るが、台帳が尻切れだと
言っている事実は残る。抽出位置は `min(NoLaterThan, 尺 / OneOverAShareOf)` で、既定は先頭から
120 秒と尺の 1/3 の小さいほう。生成に失敗しても録画の結末は変わらず、理由は `thumbnail_fault` に
分類つきで残す。

読み込み元は `Integrity:OutputRoots` の宣言をそのまま使う。出力ルートは app に読み取り専用で
渡っていればよく、絵は `Thumbnails:WrittenTo` の下に書く。ここを書かなければサムネイルは作られず、
その旨を起動時に一度だけ書き残す。マウントされていない出力ルートの行はそもそも読まない ——
読めない行が一巡の先頭を占めると以後どの録画にも絵が付かなくなるため。待っている件数は一巡ごとに
数えて書き残す。

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

録画をあとから軽く観られる形に焼き直す。成果物は app が書き込みで持つ別のルートに置き、録画が
書かれた出力ルートには置かない —— 録画の出力ルートは driver だけが書く場所で、app には読み取り
専用で渡っているため。どのルートを持つかは `Encodings:OutputRoots` に `名前=/絶対パス` を `;` で
並べて教える。1つも書かなければ何もエンコードされず、その旨を起動時に一度だけ書き残す。

`GET /api/storage` が返す出力ルートの集合は、driver が宣言するものの後ろに app が持つこれらの
ルートを並べたもの。空き容量と総容量はディスクから、書けるかどうかは実際に rename を試して測る。
driver と同じ名前を app 側にも書くと、その名前は driver のものとして扱い、app 側のルートは集合から
外して警告する。保存先として名指しできるのはこの集合のうち app が持つルートだけで、録画の出力
ルートを名指しした保存先は保存時に拒まれる。

作業ファイルは成果物と同じルートの下に書き、完成時に rename する。`Encodings:WorkedIn` に作業
ディレクトリを書くと全ルート共通でそこに書くが、いずれかのルートと別のマウントにあればプロセスは
起動しない(別マウントをまたぐ rename は copy に化け、途中で止まっても成功に見えるため)。

| 設定 | 既定 | 用途 |
| --- | --- | --- |
| `Encodings:OutputRoots` | 空 | app が成果物を書くルートの名前とマウント先(`encodes=/srv/encodes`) |
| `Encodings:WorkedIn` | 空 | 作業ファイルの置き場所(絶対パス)。空なら成果物の隣 |
| `Encodings:Prefer` | `Software` | 最初に頼むエンコーダ(`Software` / `Vaapi`)。無ければ実行時に劣化して結果に記録 |
| `Encodings:MostCores` | `2` | 1本のジョブが使うコア数の上限 |
| `Encodings:MostAttempts` | `3` | 再起動で巻き戻したジョブに与える試行回数 |
| `Encodings:BeforeFirstLook` | `00:00:15` | 起動してから最初に待機列を見るまでの間隔 |
| `Encodings:BetweenLooks` | `00:00:30` | 待機列を見る間隔 |
| `Encodings:StalledAfter` | `00:10:00` | 前進が無いまま待つ上限。超えると止めて失敗にする |

コンテナで動かすなら、このルートは app だけに読み書きで渡す(`compose.yml` の `encodes`
ボリューム)。`app` ロールは非特権ユーザーで動くので、そのユーザーが書けるディレクトリであることが条件。

### 信号品質の採取

driver がセッションを保持しているあいだだけ、その frontend が返す lock 状態・CNR・階層別の
post-Viterbi 誤りビット数を貯める。品質を測るためにチューナーを掴むことはない。lock していない
frontend の CNR は値として採らない(無信号でも負の値が返るため)。lock と統計は取得時刻が別なので、
それぞれの時刻を添えて残す。階層別の誤りビット数は階層のまま残し、1つに畳まない。カウンタは選局のたびに巻き戻るので、サンプルはセッションを名乗り、
その境界をまたぐ差分は計算しない。読みを受け取れなかったときは捨てず、「取得できず」として、
なぜ取れなかったかの分類つきで残す。

貯めた生値は分と時間の2つの窓に集約する。集約されていない生値は消さない。保持期間は生値・分の窓・
時間の窓のそれぞれに設定があり、`forever` と書いた窓は消えない。

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

`Dockerfile` が生成するイメージは1つで、`docker/entrypoint.sh` が `CARINA_ROLE`(または第1引数)で
役割を選ぶ。

| 役割 | 起動するもの |
| --- | --- |
| `driver` | 特権プロセス |
| `app` | HTTP プロセス |
| `migrate` | マイグレーションを適用して終了 |
| `web` | フロントエンド。配布用イメージのビルドが成果物を差し込む |
| `all` | 両プロセスを1コンテナで起動(開発用) |

`ffmpeg` と `libaribb25` は配布物のパッケージではなくイメージの中でソースから作る。字幕を絵にする
デコーダ(libaribcaption)を持つ配布物のパッケージが無いためで、フォント(Noto Sans CJK)と
フォントキャッシュも同じ理由で入れる。VAAPI には `intel-media-va-driver` も要る —— libva は `iHD` を
探しに行き、見つからなければ `unknown libva error` で止まる。サムネイルは app 側で ffmpeg を起こして
作るので、置き換えるときはどちらの役割にも同じ手当てが要る。

`/api/*` を app へ、それ以外を web へ振り分けるのはイメージの外側の役割。両者は同一オリジンに
置くこと。別オリジンになるとブラウザはセッション Cookie を送らず、状態を変更するリクエストは
`Origin` 検証で拒まれ、iPadOS ではサードパーティ Cookie が遮断される。

## driver の操作

```bash
task probe:driver     # ヘルスチェック
task logs:driver
task restart:driver   # コード変更の反映
```

録画中の再起動は、その録画が終わるまで戻らない。待つのは `shutdownGraceHours` までで、使い切れば
録画を打ち切る。`POST /api/driver/restart` は 409 を返して待たせない。

実行環境が守る点が2つある。

- `stop_grace_period` は driver が申告する秒数より長くすること。短いと後処理の途中で SIGKILL される。
  秒数は `--shutdown-budget` を付けて起動すると表示され、通常の起動時にも同じ値を出力する
- 再起動ポリシーに `on-failure` を使わないこと。要求による停止は終了コード 0 で、それ以外は 70。
  `on-failure` では意図的に停止したときに再起動しない

driver は呼び出し元を認証しない。境界は Unix ドメインソケットのパーミッションと所有グループだけで、
ソケットは所有グループの外に一切の権限を与えず、TCP ポートは開かない。
