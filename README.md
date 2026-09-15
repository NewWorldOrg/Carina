# Carina

日本の地上デジタル放送向けの録画システムのバックエンド。
チューナーを掴んで録画し、番組表・予約・ライブ視聴・エンコードを HTTP API で出す。
画面は [Vela](https://github.com/NewWorldOrg/Vela) が受け持つ。

## 構成

動くのは 3 つのプロセス。

| | 役割 |
| --- | --- |
| `driver` | 特権。チューナーを占有し、TS を録画ファイルに書く |
| `app` | 非特権。HTTP API を出す。番組表・予約・ライブ・エンコード・ライブラリ |
| PostgreSQL | 台帳 |

`driver` と `app` は Unix ドメインソケットで話す(driver は TCP ポートを開かない)。
**別プロセスなので、`app` を入れ替えても進行中の録画は止まらない。**
録画ファイルを書くのは `driver` だけで、`app` には読み取り専用で渡す。

API 文書は稼働中の `app` が `GET /openapi/v1.json` に出す。
実行時に組み立てるものなので、このリポジトリには置いていない。

### ディレクトリ

```
src/        アプリケーション本体
tests/      テスト。src の各プロジェクトに対応する
docker/     entrypoint、driver の開発用設定、/dev/dri の検出
patches/    イメージの中でソースからビルドする ffmpeg へのパッチ
```

`src/` は 2 つのプロセスを 1 つのリポジトリに収めている。

| プロジェクト | 中身 |
| --- | --- |
| `Carina.Driver` | 特権側。選局、TS の扱い、セッション、録画ファイル |
| `Carina.Contracts` | 両プロセスが共有する唯一の成果物。プロセス間の契約 |
| `Carina.Domain` | エンティティ、値オブジェクト、リポジトリのインタフェース |
| `Carina.Broadcast` | 放送規格の解釈。何にも依存しないライブラリ |
| `Carina.Infrastructure` | 永続化、driver への client、外部との境界 |
| `Carina.Db` | スキーマ適用の入口 |
| `Carina.Api` | HTTP の面と、そこが出す OpenAPI 文書 |

参照は内向きの一方向で、`tests/Carina.Architecture.Tests` がそれを固定している。
とくに `Carina.Driver` は `Carina.Contracts` 以外を参照できない。
app の層に手を伸ばせるようにすると、2 つのプロセスを別々に入れ替えられなくなるため。

技術スタックは .NET 10 / ASP.NET Core / EF Core / PostgreSQL。
driver は Linux の DVB API を P/Invoke で叩く。

## 必要なもの

Docker のみ。
チューナーカードも B-CAS カードも要らない。
装置の代わりに合成のチューナーが動くので、選局からセッションまで一通り歩ける。

## セットアップ

```bash
task up      # driver / app / PostgreSQL
task migrate # スキーマの適用
task build
task test
task lint
```

Task を使わない場合:

```bash
docker compose up -d
docker compose exec app dotnet run --project src/Carina.Db -- --migrate
docker compose exec app dotnet build
```

API はコンテナの 8080 番で待ち受け、ホストの 8081 番に公開する(`API_PORT` で変える)。
スキーマは `app` が起動時に当てないので、`task migrate` を先に走らせる。

`docker compose ps` の health は、`driver` では `--probe` の答え、`app` では API が `/api/health` に答えているかを表す。
`driver` は、ソケットに答えない・排水中・使えるチューナーが 1 本も無いときに unhealthy になる。
`app` のコンテナは API を自分では起動しないので、`task run:app` で起動するまで unhealthy のままになる。
health は表示するだけで、unhealthy になっても何も再起動しない。

## 設定

### driver

`CARINA_DRIVER_CONFIG` が指す JSON ファイル 1 つだけを読む。
既定のパスは無く、読めない項目があればどれが悪いかを言って起動を止める。

| キー | 用途 |
| --- | --- |
| `socketPath` | `app` とつなぐ Unix ドメインソケット。`/run/` の下 |
| `socketGroupId` | ソケットの所有グループの id |
| `outputRoots` | 録画の書き出し先。`name` と絶対パスの `path` を 1 つ以上 |
| `devices` | チューナー。1 つ以上、うち 1 つ以上が `enabled` |

`devices` の各要素は `id`、`kind`(`terrestrial` か `satellite`)、`enabled`、衛星なら `lnb` を持つ。
セッションの上限時間や demux のバッファなど、残りのキーは既定のままで動く。
`liveSessionMinutes` は「最後に求められてからどれだけ持つか」であって視聴の長さではない。
観ている間は `app` がその都度先へ延ばすので、視聴が途中で切れることはない。

### app

| 変数 | 用途 |
| --- | --- |
| `CARINA_ROLE` | イメージが起動する役割 |
| `ConnectionStrings__Carina` | PostgreSQL の接続文字列。既定値は無く、未設定なら起動しない |
| `CARINA_DRIVER_SOCKET` | `driver` とつなぐソケットのパス。既定値は無く、未設定なら起動しない |
| `CARINA_DB_CONNECTION` | スキーマ適用時の接続文字列 |
| `CARINA_PUBLIC_ORIGIN` | ブラウザがこのインストールに到達するアドレス(`https://host`) |
| `Integrity__OutputRoots` | `driver` の出力ルートが `app` からどこに見えるか(`primary=/srv/recordings`) |
| `Recording__UndecidedEndAhead` | 終わりを名乗らない番組を録るとき、実効終了時刻を今からどれだけ先に置くか。既定は 20 分で、半分を過ぎるたびに置き直す。予約がチューナーの席を確保するローリングホライズン(30 分)とは別の値 |
| `RecordingRetry__MostAttempts` | 選局で受信できなかった・データが来なかった録画を、番組の放送中に始め直す回数。既定は 10、0 で始め直さない、上限は 20。期待と違う放送だったもの・容量の事前確認で断られたもの・要確認に落ちたチャンネルは始め直さない |
| `RecordingRetry__BetweenAttempts` | 始め直す間隔。既定は 1 分、上限は 1 日。チャンネルが間を空けている間は、それより前に始め直さない |
| `RecordingProgress__AtMostEvery` | 録画中に書いた長さやドロップの数が動いたとき、開いている画面へ知らせる最短の間隔。既定は 30 秒、上限は 1 時間。録画の開始・停止・中断・結果はこの間隔を待たずに知らせる |
| `Thumbnails__WrittenTo` | サムネイルの置き場所。空なら作らない |
| `Encodings__OutputRoots` | エンコードの成果物を書くルート(`encodes=/srv/encodes`) |
| `ProgrammeFeed__ConcurrentReaders` | 一括番組表を同時に何本まで配るか。既定は 4 で、超えた要求はその場で断る |
| `ProgrammeFeed__StatementTimeout` | 一括番組表の 1 文に与える時間。既定は 30 秒、上限は `24.20:31:23.647`。超えたら何も送らず、どこから読み直すかを添えて断る |
| `Auth__SessionAbsoluteLifetime` | ログインしてからその席が終わるまでの長さ。既定は 30 日、上限は 365 日 |
| `Auth__SessionIdleTimeout` | 最後に使われてからその席が終わるまでの長さ。既定は 7 日。`Auth__SessionAbsoluteLifetime` より長くはできず、席が使われたと書き留める間隔の 5 分より短くもできない |
| `Auth__LoginFailuresBeforeRefusing` | 断りに入るまでに数える、間違ったパスワードの回数。既定は 5、上限は 100 |
| `Auth__LoginWindow` | 間違ったパスワードを数えている間の長さ。既定は 5 分、上限は 1 日 |

`CARINA_PUBLIC_ORIGIN` は ID プロバイダへ登録する redirect URI の出所。
未設定でも起動するが、リクエストの届いたアドレスからの推定になる。

置き場所とハードウェアは compose が受け取る。

| 変数 | 用途 |
| --- | --- |
| `CARINA_RECORDINGS_DIR` | 録画を書くホスト側のディレクトリ。未設定なら Docker のボリューム |
| `CARINA_ENCODES_DIR` | エンコードの成果物を書くホスト側のディレクトリ。未設定なら Docker のボリューム |
| `CARINA_DRI` | 映像処理装置のディレクトリ(`/dev/dri`)。未設定なら何も渡さない |
| `CARINA_DRI_VIDEO_GID` / `CARINA_DRI_RENDER_GID` | `card0` / `renderD128` の所有グループの番号 |
| `CARINA_ENCODINGS_PREFER` | 録画をあとからエンコードするときの変換器(`Software` / `Vaapi`)。既定は `Software` |

`task up` は起動のたびに `docker/dri-env.sh` でホストを見て、装置が在れば渡す。
渡すことと使うことは別で、ハードウェアで変換させるなら `.env` に書く(`.env.example` に雛形がある)。
送りながら観るほうが `CARINA_TRANSCODING_PREFER=Vaapi`、あとからのエンコードが `CARINA_ENCODINGS_PREFER=Vaapi` で、
装置は 1 つなので、後者は観ている人がいる間、次の仕事を始めずに待つ。

**移行で録画を運ぶなら、`CARINA_RECORDINGS_DIR` を移行元と同じマウントの下に置く。**
ハードリンクで運ぶので、マウントをまたぐと 1 本も運べない。

番組表を集める間隔、サムネイルやエンコードの回し方、信号品質の保持期間といった調整つまみは
`Collection:` `Thumbnails:` `Encodings:` `QualitySignal:` `Transcoding:` の各名前空間にある。
**どれも既定のままで動く。**

供給が途絶えたと見なすまでの時間だけは設定ではなく画面から動かす(`PATCH /api/quality/thresholds/supplySilence`、既定は 300 秒)。
途絶の見張りは信号品質の集約と同じ間隔(`QualitySignal:BetweenRollups`、既定は 5 分)で回り、録画の進み・録画の計測・信号品質サンプル・番組表の訪問を別々の異常として台帳に残す。
台帳に載るのは次の周回なので、途絶が閾値に達してから記録されるまでには最大で 1 周回ぶんの遅れがある。

候補チャンネルごとの実測スコア(lock 率・CNR の最低値・ビット誤り率の最高値・サンプル件数)も同じ周回で候補へ書き戻す。
遡る長さは `QualitySignal:EvaluateCandidatesOver`(既定は 7 日、最長 366 日)で、選び直した多重は選び直した時点から数え直す。
書き戻すのはスコアと評価日時だけで、どの候補を選局に使うかは変えない。

自動エンコードの 2 つだけは画面からも変えられる。

| 変数 | 用途 |
| --- | --- |
| `Encodings__Automatically` | 録画が終わったら誰も頼まなくてもジョブを作るか。既定は `true` |
| `Encodings__MostCores` | 1 本のエンコードが使ってよいコア数。既定は 2、1 以上、機械のコア数で頭打ち |

画面から決めると(`GET` / `PUT /api/encoding/settings`)その値が台帳の 1 行に残り、以後はそちらが効く。
配置設定は台帳に行が無いあいだの初期値で、行を消す口は持たない。
**対象**(完了と尻切れの録画すべて・失敗は対象外)と**観ている人に譲ること**は設定ではなく、`GET` は前者を事実として答えるだけ。

本編と CM の切れ目を探す設定は `Encodings:Chapters:` にある。
エンコードのジョブは本番の変換を始める前に、まず音声だけを頭から終わりまで聴いて無音の並びを拾い、そのうち長いものの周りだけ映像を 6 秒ずつ覗いて、暗転と画の変わり目を確かめる。
映像処理装置は使わず、`nice -n 19` で走るので、録画中の機械でも邪魔にならない。
**この版は切れ目を判定するところまでで、成果物への焼き込みも台帳への記録もまだ入っていない。**
探せなかったときはその旨をログに残してエンコードはそのまま進む。
切れ目探しでエンコードが失敗することはない。
探す時間の上限は 5 分。
音声を聴いている間にそれを超えたら「読めなかった」として先へ進む。
映像を覗いている途中で超えた場合と、覗く回数が上限に当たった場合は、そこまでに見た分で判定し、そう作られたことを記録に残す。
覗くのは長い無音から順で、回数の上限は `MostChapters` の 4 倍。

聴いたあと、局の透かし(画面の隅に載るロゴ)も映像全体で見る。
単独で復号できる絵だけを 1 秒に 1 枚、縮めて灰色にし、四隅に居続ける輪郭をその局の透かしとして覚える。
**録画は、その録画から覚えた透かしでは判定しない。**
使うのは同じサービスの別の録画から先に覚えた透かしだけで、そのサービスの最初の録画は透かし無しで判定する。
覚えた透かしは覚えた元の録画と一緒に `encode_watermark` に残り、サービスごとに新しい 2 本までを持つ。
透かしは CM の候補を消すことにだけ使い、新しく候補を作ることはない。
候補の区間の中で透かしが半分を超える絵に映っていれば、そこは本編とみなして消す。
透かしがほぼすべての絵に映っていたときは、本編と CM を見分けていないので使わない。
透かしを見られなかったときや覚えた透かしを読めなかったときは、透かし無しで判定してエンコードはそのまま進む。

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

**単独の切れ目からは何も作らない。**
グリッドの倍数だけ離れた 2 つの切れ目が揃ったときだけ CM の区間とみなすので、CM の無い番組では 1 つも打たれない。

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
移行元の台帳は `CARINA_MIGRATION_SOURCE_CONNECTION` が持つ接続で、読み取り専用トランザクションの中から読む。
`--into` は `Integrity__OutputRoots` が宣言しているディレクトリを指す。
運んだ録画が名乗る出力ルートの名前は、その宣言が同じディレクトリに与えている名前になる。
宣言に無いディレクトリを指すと、下見でも本番でも断って何もしない。
録画はハードリンクで運ぶので、置き場所は設定の `CARINA_RECORDINGS_DIR` に従う。

本番の前に、この順で済ませておく。

1. **チャンネル再スキャン**。定義は移行せず、再スキャンが定義の出所になる
2. **EPG が 1 巡していること**。予約の照合先が無いと運べない
3. **新システムでまだ録画を 1 本もしていないこと**。やり直しが成立する条件
4. エンコードの保存先とプロファイルが 1 つずつ在ること

本番は新しいルートが空でなければ断る。
エンコードの保存先が 1 つに定まらないときも、移行元から新しいルートへハードリンクが張れないときも断る。
下見はどれでも断らずに走り切り、**本番なら止まっていたことを記録に残す**。
CLI は `Before carrying, ...` の行で、記録は `migration_standing` の行で、それぞれ 1 件ずつ言う。
失敗したら台帳を空にし、その実行が新しいルートへ運び込んだリンクだけを外して、もう一度実行する。
新しいルート自体は消さない。
driver がライブ録画を書き込むルートそのもので、移行が運んだもの以外もそこに入る。
運び直しは一瞬で終わるので、巻き戻しの仕組みは持たない。

予約は運ばない。
ルールを運べば、新システムが通常の経路で作り直す。
ルールは全件が無効で入るので、人が有効にするまで予約は生まれない。
チャンネル定義は運ばず、再スキャン結果への対応を提案するだけで、この実行は何も確定させない。

## イメージの役割

`Dockerfile` が生成するイメージは 1 つで、`docker/entrypoint.sh` が `CARINA_ROLE`
(または第 1 引数)で役割を選ぶ。

| 役割 | 起動するもの |
| --- | --- |
| `driver` | 特権プロセス |
| `app` | HTTP プロセス |
| `migrate` | スキーマを適用して終了 |
| `web` | フロントエンド。配布用イメージのビルドが成果物を差し込む |
| `all` | 両プロセスを 1 コンテナで起動(開発用) |

`/api/*` を `app` へ、それ以外を `web` へ振り分けるのはイメージの外側の役割。
**両者は同一オリジンに置く。**
別オリジンだとブラウザはセッション Cookie を送らず、iPadOS ではサードパーティ Cookie が遮断される。

`ffmpeg` と `libaribb25` は配布物のパッケージではなくイメージの中でソースから作る。
字幕を絵にするデコーダを持つパッケージが無いため。VAAPI には `intel-media-va-driver` も要る。

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
