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
| `Thumbnails__WrittenTo` | サムネイルの置き場所。空なら作らない |
| `Encodings__OutputRoots` | エンコードの成果物を書くルート(`encodes=/srv/encodes`) |

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

**移行で録画を運ぶなら、`CARINA_RECORDINGS_DIR` を移行元と同じファイルシステムに置く。**
ハードリンクで運ぶので、ファイルシステムをまたぐと 1 本も運べない。

番組表を集める間隔、サムネイルやエンコードの回し方、信号品質の保持期間といった調整つまみは
`Collection:` `Thumbnails:` `Encodings:` `QualitySignal:` `Transcoding:` の各名前空間にある。
**どれも既定のままで動く。**

## 移行

現行の録画システムから、録画・ルール・チャンネル定義を引き継ぐ。
実行は CLI で、結果は移行記録の画面に残る。

```bash
docker compose exec app dotnet run --project src/Carina.Db -- \
  --carry --from <移行元の録画ディレクトリ> --into <新しい録画ルート>
```

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
失敗したら台帳を空にし、新しいルートを作り直して、もう一度実行する。
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
