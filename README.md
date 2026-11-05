# Carina

Carina は、日本のテレビ放送を録画して観るシステムの裏側。\
チューナーで録画し、番組表・予約・ライブ・再生・エンコードを HTTP API で提供する。\
画面は [Vela](https://github.com/NewWorldOrg/Vela) が受け持つ。

API でできること。

- 番組表を集め、予約し、キーワード・局・曜日・ジャンルの条件で自動で予約するルールを持つ
- チューナーで録画し、B-CAS カードでスクランブルを解く。カードは家の中の別の機械にも貸せる
- ライブと録画をブラウザや外部プレイヤーに配信し、字幕とデータ放送を出す
- 録画を H.264・H.265・AV1 にエンコードし、CM・OP・ED を検出してチャプターにする
- 録画ごとのドロップと受信の状態を記録し、壊れた録画を探せる
- 既存の録画システムから録画とルールを引き継ぐ

## 構成

動くプロセスは 3 つある。

| | 役割 |
| --- | --- |
| `driver` | 特権で動く。チューナーを占有し、スクランブルを解いて TS を録画ファイルに書く。[Puppis](https://github.com/NewWorldOrg/Puppis) のイメージ |
| `app` | 特権なしで動く。番組表・予約・ライブ・エンコード・ライブラリの HTTP API を提供する。このリポジトリのイメージ |
| PostgreSQL | データベース |

`driver` と `app` は Unix ドメインソケットで通信し、`driver` は `app` のための TCP ポートを開かない。\
別のプロセスなので、`app` を入れ替えても進行中の録画は止まらない。\
録画ファイルを書くのは `driver` だけで、`app` は読み取り専用で参照する。\
2 つが交わすメッセージの形と口の名前は Puppis が nuget.org の package `Puppis.Contracts` として出し、`app` はその版を `Directory.Packages.props` で固定して参照する。

API 文書は、開発環境(`ASPNETCORE_ENVIRONMENT=Development`)の `app` が `GET /openapi/v1.json` で返す。\
実行時に組み立てる文書なので、このリポジトリには置いていない。

## 動かす

本番で動かすなら、画面の Vela と一緒に立てる [Pyxis](https://github.com/NewWorldOrg/Pyxis) を使う。\
compose と Helm チャートがあり、前段のプロキシも入っている。\
ここには、イメージそのものの決まりを書く。

### イメージの役割

`Dockerfile` が作るイメージは `app` のもので、`driver` は Puppis のイメージで動かす。\
`docker/entrypoint.sh` が `CARINA_ROLE` か第 1 引数で役割を選ぶ。\
どちらも無ければ `app` になる。

| 役割 | 起動するもの |
| --- | --- |
| `app` | HTTP プロセス |
| `migrate` | スキーマを適用して終了 |

画面はこのイメージに入っておらず、別のイメージで動かす。\
前段にリバースプロキシを置き、`/api/*` を `app` に、それ以外を画面に振り分ける。\
振り分けはイメージの外で行い、画面の側も `/api/*` を中継しない。\
両者は同じオリジンに置く。\
オリジンが別だとブラウザがセッション Cookie を送らず、iPadOS ではサードパーティ Cookie として遮断される。

- WebSocket(`/api/live/ws`)と SSE(`/api/events`)は、切らずにバッファせず流す
- `Range` ヘッダはそのまま通す(録画の再生とシークに使う)

外部プレイヤー用の URL(`/api/videos/{id}/with-ticket/…` と `/api/live/{nid}-{sid}/with-ticket/…`)はパスに再生用のトークンを含み、トークンは発行から 30 秒で失効するが、一度使えば同じ URL でその録画・チャンネルを 2 時間開ける。\
前段のプロキシでアクセスログを取るなら、パスを残さない設定にする。

`ffmpeg` は、ディストリビューションのパッケージではなく、イメージの中でソースからビルドする。\
字幕を画像として描くデコーダを持つパッケージが無いためである。\
VAAPI 用に `intel-media-va-driver` も入れている。

字幕の絵は Denpa Font で描く。\
半角 512・全角 1024 の等幅で、ARIB の追加記号を収めた字幕用の書体である。\
イメージをビルドするとき Releases から `denpa-font.ttf` を取り、`SHA256SUMS` と `Dockerfile` の `DENPA_FONT_SHA256` の両方で確かめる。\
版は `Dockerfile` の `DENPA_FONT_VERSION` で固定する。\
Denpa Font に無い字は Noto Sans CJK JP(`fonts-noto-cjk`)で描く。

### イメージのタグ

`master` にマージされるたびに、CI がイメージを `ghcr.io/newworldorg/carina` に公開する。\
対応する CPU は amd64 だけである。

| タグ | 指すもの |
| --- | --- |
| `sha-<commit>` | そのコミットのイメージ。マージのたびに出る |
| `vX.Y.Z` | その版のコミットの `sha-<commit>` と同じイメージ |
| `latest` | prerelease(`v0.2.0-rc.1` のように `-` を含むもの)を除いた最も新しい版 |

一度公開したタグは上書きしない(`latest` を除く)。\
`vX.Y.Z` のタグを push すると、CI はイメージを作り直さずにそのコミットの `sha-<commit>` へ版のタグを足す。\
タグは `Directory.Build.props` の版と同じでなければならず、違うと CI が止まる。\
prerelease のタグを push したときは、その版のタグだけが足される。

### driver の止め方と設定

`driver` の設定のキー・B-CAS カードの貸し借り・止め方は、[Puppis の README](https://github.com/NewWorldOrg/Puppis#readme) にある。\
`app` 側で合わせるのは 2 つで、`CARINA_DRIVER_SOCKET` を `driver` の `socketPath` に、`Integrity__OutputRoots` を `driver` の `outputRoots` に合わせる。\
`driver` は呼び出し元を認証せず、アクセスを制限するのは Unix ドメインソケットのパーミッションと所有グループだけなので、`socketGroupId` は `app` を動かすグループにする。\
`driver` は録画が終わるまで止まらないので、実行環境の `stop_grace_period` を `driver` が申告する秒数より長くし、再起動ポリシーに `on-failure` を使わない。

## 設定

### app

| 変数 | 用途 |
| --- | --- |
| `CARINA_ROLE` | イメージが起動する役割(「イメージの役割」の節を参照) |
| `ConnectionStrings__Carina` | PostgreSQL の接続文字列。既定値は無く、未設定なら起動しない |
| `CARINA_DRIVER_SOCKET` | `driver` とつなぐソケットのパス。既定は `/run/carina/driver.sock`。driver の `socketPath` と同じ値にする |
| `CARINA_DATA_PROTECTION_KEYS` | OIDC の client secret を暗号化する鍵の置き場所(絶対パス)。既定値は無く、未設定なら起動しない。コンテナを作り直しても消えない場所を指定する |
| `CARINA_DB_CONNECTION` | スキーマを適用するときの接続文字列 |
| `CARINA_PUBLIC_ORIGIN` | ブラウザからこのサーバに届くアドレス(`https://host`) |
| `CARINA_KNOWN_PROXIES` | `X-Forwarded-*` を信頼する前段のプロキシのアドレス。カンマか空白で区切る |
| `CARINA_KNOWN_NETWORKS` | 前段のプロキシがあるネットワーク(`アドレス/プレフィクス`)。カンマか空白で区切る |
| `Integrity__OutputRoots` | `driver` の録画の保存先が、`app` からどのパスに見えるか(`primary=/srv/recordings`) |
| `Recording__UndecidedEndAhead` | 終了時刻が決まっていない番組を録画するとき、終了の予定を今からどれだけ先に置くか。既定は 20 分で、その半分が過ぎるたびに先へ延ばす |
| `Recording__StartingAhead` | 予約の開始のどれだけ前から、録画の準備(チューナーの確保・選局・開始)を始めるか。既定は `Recording__BetweenTicks`(予約を確かめる間隔)+ 5 秒で、`Recording__BetweenTicks` より長くないと起動しない。開始前に断られたときは記録せず、次の確認で試し直す |
| `RecordingRetry__MostAttempts` | 選局で受信できなかった録画や、データが来なかった録画を、放送中に始め直す回数。既定は 10、上限は 20 で、0 なら始め直さない。期待と違う放送だった録画、容量の事前確認で断られた録画、要確認になったチャンネルの録画は始め直さない |
| `RecordingRetry__BetweenAttempts` | 始め直す間隔。既定は 1 分、上限は 1 日。チャンネル側で間を空けているときは、その時間が過ぎるまで始め直さない |
| `RecordingProgress__AtMostEvery` | 録画中に書いた長さやドロップの数が変わったとき、開いている画面に知らせる最短の間隔。既定は 30 秒、上限は 1 時間。録画の開始・停止・中断・結果は、この間隔を待たずに知らせる |
| `Thumbnails__WrittenTo` | サムネイルの保存先。空なら作らない |
| `Captions__WrittenTo` | 録画から取り出した字幕とデータ放送の記録の保存先。空ならどちらも取り出さない |
| `Learning__ImportFrom` | 録画を縮めたデータ(形 `anime-material-v1`、1 本につきディレクトリ 1 つ)を置いたディレクトリ(絶対パス)。空なら取り込まない。空いている時間に 1 本ずつ、CM・OP・ED の学習に使うデータとして取り込む。中のファイルは書き換えも削除もしない |
| `Encodings__OutputRoots` | エンコード済みファイルの保存先(`encodes=/srv/encodes`) |
| `Encodings__Prefer` | 録画をあとからエンコードするときの変換器(`Software` / `Vaapi`)。既定は `Software` |
| `Transcoding__Prefer` | ライブと録画再生をその場で変換するときの変換器(`Software` / `Vaapi`)。既定は `Software` |
| `Machine__RenderNode` | VAAPI で使う GPU の描画ノードの絶対パス。既定は `/dev/dri/renderD128`。エンコード・その場の変換・GPU の機能の確認はすべてこのノードを使うので、GPU が 2 枚あるときはここで選ぶ |
| `ProgrammeFeed__ConcurrentReaders` | 一括番組表を同時に何本まで返すか。既定は 4 で、超えた要求はすぐに断る |
| `ProgrammeFeed__StatementTimeout` | 一括番組表の SQL 1 文にかけてよい時間。既定は 30 秒、上限は `24.20:31:23.647`。超えたら何も返さず、どこから読み直せばよいかを添えて断る |
| `Auth__SessionAbsoluteLifetime` | ログインしてからセッションが切れるまでの長さ。既定は 30 日、上限は 365 日 |
| `Auth__SessionIdleTimeout` | 最後に使われてからセッションが切れるまでの長さ。既定は 7 日。`Auth__SessionAbsoluteLifetime` 以下で、`Auth__SessionBetweenLastUsedWrites` より長くする |
| `Auth__SessionBetweenLastUsedWrites` | セッションを使った時刻を記録し直す最短の間隔。既定は 5 分 |
| `Auth__LoginFailuresBeforeRefusing` | ログインを断り始める、間違ったパスワードの回数。既定は 5、上限は 100 |
| `Auth__LoginWindow` | 間違ったパスワードの回数を数える期間。既定は 5 分、上限は 1 日 |

`CARINA_PUBLIC_ORIGIN` は、ID プロバイダに登録する redirect URI の元になる。\
未設定でも起動するが、そのときは要求が届いたアドレスから推定する。\
データ放送の通信の中継は、このホスト名とこの機械のアドレスには接続しない。\
未設定のときに断るのはこの機械のアドレスだけになる。

前段のプロキシを `CARINA_KNOWN_PROXIES` か `CARINA_KNOWN_NETWORKS` で指定しないと、`X-Forwarded-*` は読まれない。\
その場合、プロキシ経由の要求は http として扱われる。\
するとセッション Cookie に Secure が付かず、ID プロバイダには http の redirect URI が渡されて断られる。

画面で入力した OIDC の client secret は、`CARINA_DATA_PROTECTION_KEYS` の鍵で暗号化してからデータベースに保存する。\
データベースのバックアップだけでは secret を復号できないので、鍵はデータベースとは別に保管する。\
鍵を失うと secret を復号できなくなり、ID プロバイダでのサインインが止まる。\
そのときは設定画面で secret を入力し直す(ローカルアカウントではそのままログインできる)。

root で起動したイメージは、`app` を uid・gid 10001 に切り替えて動かす。\
番号を変えるときは、Docker の `user:` か、k8s の `runAsUser`・`runAsGroup`・`fsGroup` で指定する。\
そのときは `socketGroupId` も同じグループにそろえる。\
`CARINA_DATA_PROTECTION_KEYS` には、その番号で書き込めるディレクトリを指定する。\
書き込めなければ、`app` は理由を表示して起動しない。

開発用の compose では、保存先とハードウェアを次の変数で指定する。

| 変数 | 用途 |
| --- | --- |
| `CARINA_RECORDINGS_DIR` | 録画を保存するホスト側のディレクトリ。未設定なら Docker のボリューム |
| `CARINA_ENCODES_DIR` | エンコード済みファイルを保存するホスト側のディレクトリ。未設定なら Docker のボリューム |
| `CARINA_KEYS_DIR` | client secret を暗号化する鍵を置くホスト側のディレクトリ。未設定なら Docker のボリューム |
| `CARINA_LEARNING_IMPORT_DIR` | `app` の `Learning__ImportFrom` に読み取り専用で渡す、縮めたデータを置いたホスト側のディレクトリ。未設定なら空の Docker のボリューム |
| `CARINA_DRI` | GPU の装置のディレクトリ(`/dev/dri`)。未設定なら何も渡さない |
| `CARINA_RENDER_NODE` | `app` の `Machine__RenderNode` に渡す描画ノード。既定は `/dev/dri/renderD128` |
| `CARINA_DRI_VIDEO_GID` / `CARINA_DRI_RENDER_GID` | `card0` / `CARINA_RENDER_NODE` のノードの所有グループの番号 |
| `CARINA_ENCODINGS_PREFER` | `app` の `Encodings__Prefer` に渡す値 |
| `CARINA_TRANSCODING_PREFER` | `app` の `Transcoding__Prefer` に渡す値 |

`task up` は、起動のたびに `docker/dri-env.sh` でホストを調べ、GPU があれば `app` に渡す。\
描画ノードは `CARINA_RENDER_NODE` で決まり、シェルの環境変数に無ければ `.env` から読む。\
そのノードの所有グループも `app` に追加する。\
`driver` は GPU を使わないので、GPU を渡さない。\
GPU を渡しただけでは、変換はソフトウェアのままである。\
GPU で変換させるには、`.env` に `CARINA_TRANSCODING_PREFER=Vaapi` や `CARINA_ENCODINGS_PREFER=Vaapi` を書く(雛形は `.env.example`)。\
GPU は 1 つなので、あとからのエンコードは、視聴中の人がいる間は次のジョブを始めずに待つ。

移行で録画を移すなら、`CARINA_RECORDINGS_DIR` を移行元と同じマウントの下に置く。\
録画はハードリンクで移すので、マウントをまたぐと 1 本も移せない。

番組表を集める間隔、サムネイルやエンコードの進め方、信号品質の保存期間などを調整する設定もある。\
これらは `Collection:` `Logos:` `Thumbnails:` `Captions:` `Encodings:` `Integrity:` `QualitySignal:` `Recording:` `Transcoding:` の各名前空間にある。\
どれも既定のままで動く。

品質の閾値は、設定ではなく画面から変える(`PATCH /api/quality/thresholds/{key}`)。\
閾値はドロップや解けなかったスクランブルの割合・CNR・ビット誤り率など。\
CNR とビット誤り率の閾値は、手で決めない限り実測から決まる。\
データの供給が途絶えたと見なすまでの時間も閾値の 1 つで、既定は 300 秒である。\
ただし番組表の取得は、この時間ではなく、次の 3 つを足した時間で途絶えたと判定する。

- `Collection:` の巡回の間隔
- チューナーが埋まっているときに待ち直す時間
- 1 回の取得にかける最長の時間

自動エンコードの設定のうち、次の 2 つは画面からも変えられる。

| 変数 | 用途 |
| --- | --- |
| `Encodings__Automatically` | 録画が終わったら、頼まれなくてもエンコードのジョブを作るか。既定は `true` |
| `Encodings__MostCores` | 1 本のエンコードが使ってよいコア数。既定は 2。1 以上で、機械のコア数が上限 |

画面で決めた値(`GET` / `PUT /api/encoding/settings`)はデータベースに残り、以後はそちらが使われる。\
環境変数の値は、画面で一度も決めていない間だけ使われる初期値である。\
画面で決めた値を消して、環境変数の値に戻す方法は無い。\
自動エンコードの対象は、完了した録画と途中で終わった録画で、失敗した録画は含まない。\
この対象と、視聴中の人がいる間はエンコードが待つことは、設定では変えられない。

自動のジョブは、画面で「退役」にしていない保存先がちょうど 1 つのときだけ作られる。\
そのときは、その保存先の既定のプロファイルでエンコードする。\
保存先が無いときや 2 つ以上あるとき、既定のプロファイルが退役しているときは、ジョブを作らずに理由をログに残す。\
録画ごと・予約ごとにプロファイルを選ぶ方法は無い。

エンコード済みファイルには、CM・OP・ED の検出がその録画で判定した区間をチャプターとして付ける。\
検出は録画ごとに自動で動き、設定で止めることはできない。\
まだ判定の無い録画は待たずにチャプター無しでエンコードし、判定ができたら空いている時間にチャプターを書き込む。\
判定が変わったときも同じように書き直す。

## 移行

現行の録画システムから、録画・ルール・チャンネル定義を引き継ぐ。\
実行は CLI で行い、結果は画面の移行記録に残る。

```bash
docker compose run --rm --no-deps \
  -v <移行元と新しいルートの両方を含むディレクトリ>:/srv/carry \
  -e Integrity__OutputRoots=primary=/srv/carry/<新しい録画ルート> \
  app dotnet run --project src/Carina.Db -- \
  --carry --from /srv/carry/<移行元の録画ディレクトリ> --into /srv/carry/<新しい録画ルート>
```

`docker compose exec app` では実行できない。\
compose の `app` は `/srv/recordings` を読み取り専用でしか持たず、移行元のディレクトリも持たないためである。\
下見も本番も、書き込める録画ルートと移行元を渡した使い捨てのコンテナで実行する。\
ハードリンクは 2 つのマウントをまたげない。\
移行元と新しいルートを別々の `-v` で渡すと、同じディスクの上でも 1 本も移せない。\
両方を含む 1 つのディレクトリを、1 つの `-v` で渡す。\
そのコンテナでは、`Integrity__OutputRoots` だけを新しいルートに向け直す。\
出力ルートの名前は変わらず、そのコンテナの中でだけパスが変わる。

`--for-real` を付けなければ、何も変えずに確かめるだけの下見になる。\
下見は移行元を一切変えないので、何度実行してもよい。\
移行元のデータベースは、`CARINA_MIGRATION_SOURCE_CONNECTION` の接続を使い、読み取り専用のトランザクションで読む。\
`--into` には、`Integrity__OutputRoots` に書いたディレクトリを指定する。\
移した録画の出力ルートの名前は、`Integrity__OutputRoots` でそのディレクトリに付けた名前になる。\
`Integrity__OutputRoots` に無いディレクトリを指定すると、下見でも本番でも何もせずに終わる。

本番の前に、次の順で済ませておく。

1. チャンネルを再スキャンする。チャンネル定義は移さず、再スキャンの結果を使う
2. 番組表をひととおり集め終える。予約を照らし合わせる先が無いと移行できない
3. 新しいシステムでは、まだ 1 本も録画しない。やり直しができるための条件
4. エンコードの保存先とプロファイルを 1 つずつ作っておく

本番は、新しいルートが空でなければ実行しない。\
エンコードの保存先が 1 つに決まらないときと、移行元から新しいルートへハードリンクを張れないときも実行しない。\
下見はこれらの場合も最後まで実行し、本番なら止まっていたことを記録に残す。\
止まる理由は 1 件ずつ出る。\
CLI では `Before carrying, ...` で始まる行、記録では `migration_standing` の行。\
失敗したら Carina のデータベースを空にし、その実行が新しいルートに張ったリンクだけを外して、もう一度実行する。\
新しいルート自体は消さない。\
そこは `driver` が録画を書き込むルートそのもので、移行で移したもの以外のファイルも入るためである。\
やり直しはすぐに終わるので、巻き戻しの仕組みは用意していない。

予約そのものは移さない。\
移したルールから、新しいシステムが通常どおり予約を作り直す。\
ただしルールはすべて無効の状態で入るので、人が有効にするまで予約は作られない。\
チャンネル定義も移さない。\
この実行は再スキャンの結果との対応を提案するだけで、何も確定させない。

## 開発

必要なのは Docker だけで、チューナーカードも B-CAS カードも要らない。\
実機の代わりに合成のチューナーが動くので、選局からセッションまでひととおり試せる。

### セットアップ

```
src/        アプリケーション本体。プロジェクトごとの役割は CLAUDE.md にある
tests/      テスト。src の各プロジェクトに対応する
docker/     entrypoint、/dev/dri の検出、イメージに同梱するライセンスの検査
patches/    イメージの中でソースからビルドする ffmpeg へのパッチ
```

```bash
task up       # driver / app / PostgreSQL
task migrate  # スキーマの適用
task run:app  # API の起動
```

Task を使わないなら次のとおり。

```bash
docker compose up -d
docker compose exec app dotnet run --project src/Carina.Db -- --migrate
docker compose exec app dotnet run --project src/Carina.Api
```

開発用の `driver` は Puppis のイメージで動き、`compose.yml` に版を書いてある。\
Puppis の `v0.1.0` が出るまでは `sha-<commit>` のタグを書き、出たらそのタグに替える。\
設定は `compose.yml` の `configs` にある合成のチューナーのもので、`CARINA_DRIVER_CONFIG` で `driver` に渡す。\
API はコンテナの 8080 番で待ち受け、ホストの 8081 番に公開する(`API_PORT` で変えられる)。\
`app` は起動時にスキーマを適用しないので、先に `task migrate` を実行する。\
開発用の `app` コンテナは、API を自動では起動しない。\
ビルド・試験・lint のコマンドは `CLAUDE.md` にある。

`docker compose ps` の health は、`driver` では `--probe` の結果を表す。\
`app` の health は、`/api/health` が応答するかどうかを表す。\
`driver` は次のときに unhealthy になる。

- ソケットが応答しない
- 停止に向けて、録画が終わるのを待っている
- 使えるチューナーが 1 本も無い

`app` は、`task run:app` で API を起動するまで unhealthy のまま。\
health は状態を表示するだけで、unhealthy になっても何も再起動しない。

コンテナのログは 3 つのサービスとも json-file 形式で、1 ファイル 20 MB・5 世代までに抑える。\
設定は `compose.yml` の `x-logging` にある。\
この設定はコンテナを作り直したときに効き、動いているコンテナには効かない。


### driver の操作

```bash
task probe:driver     # ヘルスチェック
task logs:driver
task restart:driver
```

`driver` のコードはこのリポジトリに無い。\
変えるときは Puppis で変え、`compose.yml` のイメージの版を替えて `docker compose up -d driver` で入れ替える。

### 取り決めの版

`app` と `driver` の取り決めは `Puppis.Contracts` の package で、版は `Directory.Packages.props` に 1 か所だけ書く。\
取り決めの変更は足すだけで、口やイベントを消さず名前を変えないので、古い `driver` と新しい `app` の組も新しい `driver` と古い `app` の組も正常な組である。\
新しい口を使うときは、`driver` が挨拶で返すできることの一覧にその名前があるかを見てから頼む。\
Puppis が新しい版を出したら `Directory.Packages.props` の版を上げ、使う口を足す変更と同じ PR で入れる。

## ライセンス

ライセンスは AGPL-3.0-only で、著作権者は NewWorldOrg である。\
詳細は `LICENSE` にある。

イメージに同梱した他のソフトウェアとそのライセンスは、`THIRD-PARTY-NOTICES.md` に載せている。\
イメージの中では、`LICENSE`・`THIRD-PARTY-NOTICES.md`・同梱物のライセンス全文と、GPL の部品(`ffmpeg`・x264)の対応するソースが `/usr/share/doc/carina/` にある。\
.NET のランタイムのライセンスは `/usr/share/dotnet/` にある。
