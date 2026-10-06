# Carina

日本の地上デジタル放送向けの録画システムのバックエンド。
チューナーを掴んで録画し、番組表・予約・ライブ視聴・エンコードを HTTP API で出す。
画面は [Vela](https://github.com/NewWorldOrg/Vela) が受け持つ。

## 構成

動くのは 3 つのプロセス。

| | 役割 |
| --- | --- |
| `driver` | 特権。チューナーを占有し、スクランブルを解いて TS を録画ファイルに書く |
| `app` | 非特権。HTTP API を出す。番組表・予約・ライブ・エンコード・ライブラリ |
| PostgreSQL | データベース |

`driver` と `app` は Unix ドメインソケットで話す(driver は TCP ポートを開かない)。
**別プロセスなので、`app` を入れ替えても進行中の録画は止まらない。**
録画ファイルを書くのは `driver` だけで、`app` には読み取り専用で渡す。

API 文書は、開発環境(`ASPNETCORE_ENVIRONMENT=Development`)で動いている `app` が `GET /openapi/v1.json` に出す。
実行時に組み立てるものなので、このリポジトリには置いていない。

```
src/        アプリケーション本体。プロジェクトごとの役割は CLAUDE.md にある
tests/      テスト。src の各プロジェクトに対応する
docker/     entrypoint、driver の開発用設定、/dev/dri の検出
patches/    イメージの中でソースからビルドする ffmpeg へのパッチ
```

## 必要なもの

Docker のみ。
チューナーカードも B-CAS カードも要らない。
装置の代わりに合成のチューナーが動くので、選局からセッションまで一通り歩ける。

## セットアップ

```bash
task up       # driver / app / PostgreSQL
task migrate  # スキーマの適用
task run:app  # API の起動
```

Task を使わない場合:

```bash
docker compose up -d
docker compose exec app dotnet run --project src/Carina.Db -- --migrate
docker compose exec app dotnet run --project src/Carina.Api
```

API はコンテナの 8080 番で待ち受け、ホストの 8081 番に公開する(`API_PORT` で変える)。
`app` は起動時にスキーマを当てないので、`task migrate` を先に走らせる。
開発用の `app` コンテナは API を自分では起動しない。
ビルド・試験・lint のコマンドは `CLAUDE.md` にある。

`docker compose ps` の health は、`driver` では `--probe` の答え、`app` では API が `/api/health` に答えているかを表す。
`driver` は、ソケットに答えないとき・停止に向けて録画の終わりを待っているとき・使えるチューナーが 1 本も無いときに unhealthy になる。
`app` は `task run:app` で API を起動するまで unhealthy のままになる。
health は表示するだけで、unhealthy になっても何も再起動しない。

コンテナのログは 3 つのサービスとも json-file で、1 ファイル 20 MB・5 世代までに切り詰める(`compose.yml` の `x-logging`)。
この設定はコンテナを作り直したときに効き、動いているコンテナには及ばない。

## 設定

### driver

`CARINA_DRIVER_CONFIG` が指す JSON ファイル 1 つだけを読む。
既定のパスは無く、読めない項目や知らないキーがあれば、どれが悪いかを言って起動を止める。

| キー | 用途 |
| --- | --- |
| `socketPath` | `app` とつなぐ Unix ドメインソケット。`/run/` の下。既定は `/run/carina/driver.sock` |
| `socketGroupId` | ソケットの所有グループの id。既定は 10001。`app` を動かすグループと同じ値にする |
| `outputRoots` | 録画の書き出し先。`name` と絶対パスの `path` を 1 つ以上 |
| `tuner.backend` | `dvb`(実機のチューナー)か `fake`(合成のチューナー)。既定値は無い |
| `devices` | チューナー。1 つ以上、うち 1 つ以上が `enabled` |
| `shutdownGraceHours` | 録画中に止められたとき、録画が終わるまで待つ上限の時間。1 から 168 で、既定は 6 |

`devices` の各要素は `id`、`kind`(`terrestrial` か `satellite`)、`enabled`(既定は `true`)を持つ。
`tuner.backend` が `dvb` なら、`/dev/` の下の装置のパス `devicePath` も要る。
衛星のチューナーで LNB に給電するなら `lnbPower` を `true` にする。
セッションの上限時間や demux のバッファなど、残りのキーは既定のままで動く。
`liveSessionMinutes` は「最後に求められてからどれだけ持つか」であって視聴の長さではない。
観ている間は `app` がその都度先へ延ばすので、視聴が途中で切れることはない。

`tuner.backend` が `dvb` のとき、`driver` はホストの pcscd を通してカードリーダーの B-CAS カードを使い、スクランブルを解いて録画する。
`driver` のコンテナには、ホストの `/run/pcscd` をソケットのファイルではなくディレクトリごと渡す(pcscd は起動し直すたびにソケットを作り直すため)。
カードが答えないときはスクランブルされたまま録り、その量を品質の数字に残す。

### app

| 変数 | 用途 |
| --- | --- |
| `CARINA_ROLE` | イメージが起動する役割 |
| `ConnectionStrings__Carina` | PostgreSQL の接続文字列。既定値は無く、未設定なら起動しない |
| `CARINA_DRIVER_SOCKET` | `driver` とつなぐソケットのパス。既定は `/run/carina/driver.sock`。driver の `socketPath` と同じ値にする |
| `CARINA_DATA_PROTECTION_KEYS` | OIDC の client secret を封じる鍵の置き場(絶対パス)。既定値は無く、未設定なら起動しない。コンテナを作り直しても残る場所を渡す |
| `CARINA_DB_CONNECTION` | スキーマ適用時の接続文字列 |
| `CARINA_PUBLIC_ORIGIN` | ブラウザがこのインストールに到達するアドレス(`https://host`) |
| `CARINA_KNOWN_PROXIES` | `X-Forwarded-*` を信頼する前段のプロキシのアドレス。区切りはカンマか空白 |
| `CARINA_KNOWN_NETWORKS` | 同じく、前段のプロキシが置かれたネットワーク(`アドレス/プレフィクス`) |
| `Integrity__OutputRoots` | `driver` の出力ルートが `app` からどこに見えるか(`primary=/srv/recordings`) |
| `Recording__UndecidedEndAhead` | 終わりを名乗らない番組を録るとき、実効終了時刻を今からどれだけ先に置くか。既定は 20 分で、半分を過ぎるたびに置き直す |
| `Recording__StartingAhead` | 予約の開始のどれだけ前から録画の準備(チューナーの確保・選局・開始)を始めるか。既定は予約を確かめる間隔(`Recording__BetweenTicks`)+ 5 秒で、`Recording__BetweenTicks` より長くなければ起動しない。開始より前に断られたときは記録せず、次の確認で試し直す |
| `RecordingRetry__MostAttempts` | 選局で受信できなかった・データが来なかった録画を、番組の放送中に始め直す回数。既定は 10、0 で始め直さない、上限は 20。期待と違う放送だったもの・容量の事前確認で断られたもの・要確認に落ちたチャンネルは始め直さない |
| `RecordingRetry__BetweenAttempts` | 始め直す間隔。既定は 1 分、上限は 1 日。チャンネルが間を空けている間は、それより前に始め直さない |
| `RecordingProgress__AtMostEvery` | 録画中に書いた長さやドロップの数が動いたとき、開いている画面へ知らせる最短の間隔。既定は 30 秒、上限は 1 時間。録画の開始・停止・中断・結果はこの間隔を待たずに知らせる |
| `Thumbnails__WrittenTo` | サムネイルの置き場所。空なら作らない |
| `Captions__WrittenTo` | 録画から取り出した字幕の置き場所。空なら取り出さない |
| `Encodings__OutputRoots` | エンコード済みファイルを書くルート(`encodes=/srv/encodes`) |
| `Encodings__Prefer` | 録画をあとからエンコードするときの変換器(`Software` / `Vaapi`)。既定は `Software` |
| `Transcoding__Prefer` | ライブと録画再生をその場で変換するときの変換器(`Software` / `Vaapi`)。既定は `Software` |
| `Machine__RenderNode` | VAAPI で使う GPU の描画ノード(絶対パス)。既定は `/dev/dri/renderD128`。GPU が 2 枚あればノードも 2 つ並ぶので、どちらを使うかをここで選ぶ。エンコード・ライブ・録画再生のその場の変換・GPU の能力の確かめが、すべてこのノードを使う |
| `ProgrammeFeed__ConcurrentReaders` | 一括番組表を同時に何本まで配るか。既定は 4 で、超えた要求はその場で断る |
| `ProgrammeFeed__StatementTimeout` | 一括番組表の 1 文に与える時間。既定は 30 秒、上限は `24.20:31:23.647`。超えたら何も送らず、どこから読み直すかを添えて断る |
| `Auth__SessionAbsoluteLifetime` | ログインしてからそのセッションが切れるまでの長さ。既定は 30 日、上限は 365 日 |
| `Auth__SessionIdleTimeout` | 最後に使われてからそのセッションが切れるまでの長さ。既定は 7 日。`Auth__SessionAbsoluteLifetime` より長くはできず、`Auth__SessionBetweenLastUsedWrites` より長くなければならない |
| `Auth__SessionBetweenLastUsedWrites` | セッションが使われた時刻を記録し直す最短の間隔。既定は 5 分 |
| `Auth__LoginFailuresBeforeRefusing` | ログインを断り始めるまでに数える、間違ったパスワードの回数。既定は 5、上限は 100 |
| `Auth__LoginWindow` | 間違ったパスワードを数えている間の長さ。既定は 5 分、上限は 1 日 |

`CARINA_PUBLIC_ORIGIN` は ID プロバイダへ登録する redirect URI の出所。
未設定でも起動するが、リクエストの届いたアドレスからの推定になる。

前段のプロキシを `CARINA_KNOWN_PROXIES` か `CARINA_KNOWN_NETWORKS` で指定しないと、`X-Forwarded-*` は読まれない。
そのままではプロキシ越しの要求が http として扱われ、セッション Cookie に Secure が付かず、ID プロバイダには受け付けられない http の redirect URI が渡る。

画面から入れた OIDC の client secret は、データベースには `CARINA_DATA_PROTECTION_KEYS` の鍵で封じた形でだけ入る。
データベースのバックアップだけでは secret を開けないので、鍵の置き場はデータベースとは別に残す。
鍵の置き場を失うと secret を開けなくなり、ID プロバイダでのサインインは止まって、設定画面が secret の入れ直しを求める(ローカルアカウントは使える)。

root で起動したイメージは `app` を uid・gid 10001 に降ろして動かす。
番号を変えるときは Docker の `user:` か k8s の `runAsUser`・`runAsGroup`・`fsGroup` で渡し、`socketGroupId` をそのグループにそろえる。
`CARINA_DATA_PROTECTION_KEYS` にはその番号で書けるディレクトリを渡す。書けなければ `app` は理由を言って起動しない。

置き場所とハードウェアは compose が受け取る。

| 変数 | 用途 |
| --- | --- |
| `CARINA_RECORDINGS_DIR` | 録画を書くホスト側のディレクトリ。未設定なら Docker のボリューム |
| `CARINA_ENCODES_DIR` | エンコード済みファイルを書くホスト側のディレクトリ。未設定なら Docker のボリューム |
| `CARINA_KEYS_DIR` | client secret を封じる鍵を置くホスト側のディレクトリ。未設定なら Docker のボリューム |
| `CARINA_DRI` | 映像処理装置のディレクトリ(`/dev/dri`)。未設定なら何も渡さない |
| `CARINA_RENDER_NODE` | `app` の `Machine__RenderNode` に渡す描画ノード。既定は `/dev/dri/renderD128` |
| `CARINA_DRI_VIDEO_GID` / `CARINA_DRI_RENDER_GID` | `card0` / `CARINA_RENDER_NODE` のノードの所有グループの番号 |
| `CARINA_ENCODINGS_PREFER` | `app` の `Encodings__Prefer` に渡す値 |
| `CARINA_TRANSCODING_PREFER` | `app` の `Transcoding__Prefer` に渡す値 |

`task up` は起動のたびに `docker/dri-env.sh` でホストを見て、装置が在れば渡す。
描画ノードは `CARINA_RENDER_NODE`(シェルの環境変数、無ければ `.env`)のものを見て、その所有グループを `app` に足す。
`driver` は GPU を使わないので、装置は渡さない。
渡すことと使うことは別で、ハードウェアで変換させるなら `.env` に `CARINA_TRANSCODING_PREFER=Vaapi` や `CARINA_ENCODINGS_PREFER=Vaapi` を書く(`.env.example` に雛形がある)。
装置は 1 つなので、あとからのエンコードは、観ている人がいる間は次の仕事を始めずに待つ。

**移行で録画を運ぶなら、`CARINA_RECORDINGS_DIR` を移行元と同じマウントの下に置く。**
ハードリンクで運ぶので、マウントをまたぐと 1 本も運べない。

番組表を集める間隔、サムネイルやエンコードの回し方、信号品質の保持期間といった調整つまみは、`Collection:` `Logos:` `Thumbnails:` `Captions:` `Encodings:` `Integrity:` `QualitySignal:` `Recording:` `Transcoding:` の各名前空間にある。
**どれも既定のままで動く。**

品質の閾値は設定ではなく、画面から変える(`PATCH /api/quality/thresholds/{key}`)。
ドロップや解けなかったスクランブルの割合、CNR、ビット誤り率、データの供給が途絶えたと見なすまでの時間(既定は 300 秒)などがある。
CNR とビット誤り率の閾値は、手で決めない限り実測から決まる。
番組表の取得だけは供給が途絶えたと見なすまでの時間を使わず、`Collection:` の巡回の間隔・チューナーが埋まっているときの待ち直し・1 回の取得の最長時間を足した時間で判定する。

自動エンコードの 2 つだけは画面からも変えられる。

| 変数 | 用途 |
| --- | --- |
| `Encodings__Automatically` | 録画が終わったら誰も頼まなくてもジョブを作るか。既定は `true` |
| `Encodings__MostCores` | 1 本のエンコードが使ってよいコア数。既定は 2、1 以上、機械のコア数で頭打ち |

画面から決めると(`GET` / `PUT /api/encoding/settings`)その値がデータベースに残り、以後はそちらが効く。
環境変数の値は、画面で一度も決めていない間の初期値になり、画面で決めた値を消して環境変数に戻す方法は無い。
自動エンコードの対象(完了した録画と途中で終わった録画。失敗した録画は含まない)と、観ている人に装置を譲ることは、設定では変えられない。

**自動のジョブは、退役していない保存先がちょうど 1 つのときだけ作られる。**
その保存先の既定のプロファイルでエンコードする。
保存先が 1 つも無いとき、2 つ以上あるとき、既定のプロファイルが退役しているときは、**何も作らず、その理由をログに残す。**
録画ごと・予約ごとにプロファイルを選ぶ方法は無い。

本編と CM の切れ目は、本番の変換を始める前に探し、エンコード済みファイルにチャプターの印として打つ。
映像処理装置は使わず `nice -n 19` で走るので、録画中の機械でも邪魔にならない。
探す時間の上限は 5 分。
**切れ目探しでエンコードが失敗することはない。**
探せなかったときはログに残して、エンコードはそのまま進む。

| 変数 | 用途 |
| --- | --- |
| `Encodings__Chapters__Marked` | エンコードの前に切れ目を探すかどうか。既定は `true`。`false` なら何も探さず、ffmpeg に渡す引数は探さなかったときと同じになる |
| `Encodings__Chapters__Watermark` | 切れ目を探すときに局の透かしを覚えて使うかどうか。既定は `true`。`false` なら透かしのために映像を見ず、覚えも使いもしない |
| `Encodings__Chapters__Noise` | 無音とみなす音量。デシベルの整数で既定は `-50`、`-100` から `-1` まで |
| `Encodings__Chapters__ShortestSilence` | これより短い無音は切れ目の候補にしない。既定は `00:00:00.150` |
| `Encodings__Chapters__Scene` | 画がこれ以上変われば候補を裏づけたとみなす。0 より大きく 1 以下で、既定は `0.30` |
| `Encodings__Chapters__Grid` | CM の並びが乗るグリッド。既定は `00:00:15` |
| `Encodings__Chapters__GridTolerance` | グリッドからこれだけまでのずれは組とみなす。既定は `00:00:01`、`Encodings__Chapters__Grid` の半分より短いこと |
| `Encodings__Chapters__MostBreakShare` | 全体のうち CM と判定してよい割合の上限。超えたら読み取りを丸ごと捨てる。0 より大きく 1 以下で、既定は `0.5` |
| `Encodings__Chapters__MostChapters` | 打ってよい印の数の上限。超えたら読み取りを丸ごと捨てる。1 以上の整数で、既定は `40` |

**組にならない単独の切れ目からは何も作らない。** CM の無い番組では 1 つも打たれない。

## 移行

現行の録画システムから、録画・ルール・チャンネル定義を引き継ぐ。
実行は CLI で、結果は移行記録の画面に残る。

```bash
docker compose run --rm --no-deps \
  -v <移行元と新しいルートの両方を含むディレクトリ>:/srv/carry \
  -e Integrity__OutputRoots=primary=/srv/carry/<新しい録画ルート> \
  app dotnet run --project src/Carina.Db -- \
  --carry --from /srv/carry/<移行元の録画ディレクトリ> --into /srv/carry/<新しい録画ルート>
```

`docker compose exec app` では走らない。
compose の `app` は `/srv/recordings` を読み取り専用でしか持たず、移行元のディレクトリを一切持たない。
下見も本番も、書ける録画ルートと移行元を渡した 1 回きりのコンテナで走らせる。
ハードリンクは 2 つのマウントをまたげないので、移行元と新しいルートを別々の `-v` で渡すと、同じディスクの上でも 1 本も運べない。
両方を含む 1 つのディレクトリを 1 つの `-v` で渡す。
そのコンテナの `Integrity__OutputRoots` だけを、載せた先の新しいルートへ向け直す。
名前は宣言のまま、パスがそのコンテナ限りで変わるだけである。

`--for-real` を付けなければ下見で、**移行元を一切変えないので何度でも走らせてよい**。
移行元のデータベースは `CARINA_MIGRATION_SOURCE_CONNECTION` が持つ接続で、読み取り専用トランザクションの中から読む。
`--into` は `Integrity__OutputRoots` が宣言しているディレクトリを指す。
運んだ録画の出力ルートの名前は、その宣言が同じディレクトリに与えている名前になる。
宣言に無いディレクトリを指すと、下見でも本番でも断って何もしない。

本番の前に、この順で済ませておく。

1. **チャンネル再スキャン**。定義は移行せず、再スキャンが定義の出所になる
2. **EPG が 1 巡していること**。予約の照合先が無いと運べない
3. **新システムでまだ録画を 1 本もしていないこと**。やり直しが成立する条件
4. エンコードの保存先とプロファイルが 1 つずつ在ること

本番は新しいルートが空でなければ断る。
エンコードの保存先が 1 つに定まらないときも、移行元から新しいルートへハードリンクが張れないときも断る。
下見はどれでも断らずに走り切り、**本番なら止まっていたことを記録に残す**。
CLI は `Before carrying, ...` の行で、記録は `migration_standing` の行で、それぞれ 1 件ずつ言う。
失敗したら Carina のデータベースを空にし、その実行が新しいルートへ運び込んだリンクだけを外して、もう一度実行する。
新しいルート自体は消さない。
driver がライブ録画を書き込むルートそのもので、移行が運んだもの以外もそこに入る。
運び直しは一瞬で終わるので、巻き戻しの仕組みは持たない。

予約は運ばない。
ルールを運べば、新システムが通常の経路で作り直す。
ルールは全件が無効で入るので、人が有効にするまで予約は生まれない。
チャンネル定義は運ばず、再スキャン結果への対応を提案するだけで、この実行は何も確定させない。

## イメージの役割

`Dockerfile` が生成するイメージは 1 つで、`docker/entrypoint.sh` が `CARINA_ROLE`(または第 1 引数)で役割を選ぶ。

| 役割 | 起動するもの |
| --- | --- |
| `driver` | 特権プロセス |
| `app` | HTTP プロセス |
| `migrate` | スキーマを適用して終了 |
| `all` | 両プロセスを 1 コンテナで起動(開発用) |

画面はこのイメージに入っていない。別のイメージで動かす。
前段にリバースプロキシを置き、`/api/*` を `app` へ、それ以外を画面へ送る。
振り分けはイメージの外側の役割で、画面も `/api/*` を中継しない。
**両者は同一オリジンに置く。**
別オリジンだとブラウザはセッション Cookie を送らず、iPadOS ではサードパーティ Cookie が遮断される。

- WebSocket(`/api/live/ws`)と SSE(`/api/events`)は切らず、溜めずに流す
- `Range` は素通しにする(録画の再生とシーク)

`ffmpeg` はディストリビューションのパッケージではなく、イメージの中でソースから作る。
字幕を絵にするデコーダを持つパッケージが無いため。VAAPI には `intel-media-va-driver` も要る。
同梱物のライセンスと、`ffmpeg`・x264 の対応するソースはイメージの `/usr/share/doc/carina/` にあり、一覧は `THIRD-PARTY-NOTICES.md` にある。

## イメージのタグ

`master` に入るたびに、CI が同じ 1 つのイメージを `ghcr.io/newworldorg/carina` へ 2 つのタグで出す。
対応する CPU は amd64 だけ。

| タグ | `<commit>` が指すもの | 使う役割 |
| --- | --- | --- |
| `driver-sha-<commit>` | driver 側を最後に変えたコミット | `driver` |
| `app-sha-<commit>` | app 側を最後に変えたコミット | `app`、`migrate` |

**更新で driver を入れ替える必要があるのは、`driver-sha-*` が前と変わったときだけ。**
app だけの変更では `app-sha-*` だけが動くので、`driver` は前のタグのまま動かし続ければよく、録画は止まらない。

- driver 側は `Carina.Driver` とそれが参照するプロジェクト、app 側は `Carina.Api`・`Carina.Db` とそれらが参照するプロジェクト
- `Carina.Contracts`、`Dockerfile`、`Directory.Build.props`、`Directory.Packages.props`、`docker/entrypoint.sh` の変更は両方のタグを動かす
- 試験、文書、CI の定義、開発用の compose の変更はどちらのタグも動かさない
- 一度出たタグは上書きされない

どちらの側に何が入るかは `.github/image-tags.sh inputs driver`(または `app`)が答える。

## driver の操作

```bash
task probe:driver     # ヘルスチェック
task logs:driver
task restart:driver   # コード変更の反映
```

録画中の再起動は、その録画が終わるまで戻らない。
待つのは `shutdownGraceHours` までで、使い切れば録画を打ち切る。

実行環境が守る点が 2 つある。

- `stop_grace_period` は driver が申告する秒数より長くすること。短いと後処理の途中で SIGKILL される
- 再起動ポリシーに `on-failure` を使わないこと。要求による停止は終了コード 0 で、それ以外は 70

driver は呼び出し元を認証しない。
境界は Unix ドメインソケットのパーミッションと所有グループだけで、TCP ポートは開かない。

## ライセンス

AGPL-3.0-only。著作権者は NewWorldOrg。詳細は `LICENSE` を参照。
