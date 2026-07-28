# LiveObject システム仕様

LiveObject周りの仕様の要約や方向性を記述する。機能実装するうえで参考になるように。

## 基本

### Editor対応

- Editor(非Play中)でもRemoteControlによる、パラメータ操作等が機能するように配慮する

### プロパティパス

- 内部表現（DotBracket）と外部表現（Slash）の2形式がある
- 外部インターフェース（REST API）では常に Slash 形式を使う
- サーバー内部では DotBracket 形式を使う
- 境界での変換は `PropertyPath` が担う

### 制約

- ScriptableObjectはエディタでPlay後も値が残るため、復元で明示的に戻す必要がある
- コンテナ未登録オブジェクトをレジストリに自動登録してはならない
- デフォルト値のキーはtargetオブジェクト参照（LiveObjectインスタンスではない）

## オブジェクトとプロパティ

### レジストリ登録

- オブジェクトの登録は **Containerによる明示登録** と **シングルトン自己登録** の2つのみ
- 登録されたオブジェクトはIDを持ち、外部から直接参照できる
- それ以外（走査やAPI応答で一時的に必要なもの）はレジストリに登録しない
- コンテナ未登録のオブジェクトは、親からのプロパティパスでアクセスする
- 2つのレジストリを使い分ける:
  - `LiveObjectRegistry` — Container / singleton 経由で登録された LiveObject の管理。REST API のリクエスト解決に使う
  - `LiveObjectFileRegistry` — シーンファイル保存/読み込み中の `@id` (文字列) と UnityEngine.Object の双方向マップ。ランタイム専用キャッシュで永続化はしない。登録済み LiveObject を持つ root の場合は、その LiveObject.id をそのまま file-scope id として再利用する

### プロパティ属性

- `[LiveField]` — 永続化される
- `[LiveProperty]` — 永続化されない。永続化したい場合は次のいずれかを使う:
  - **Shadow Field パターン**: `[LiveField, Hide, FormerlyNamedAs("propName")]` を持つ private field とペアにし、Property は値の get/set ラッパ + 副作用 (`Apply*`) として実装。永続化は Field 経由
  - **`[InlineReference]`**: readonly Property + UnityEngine.Object 派生型 (Component/ScriptableObject) の場合のみ。pending entry としてシーンファイルに書き出される
- read-onlyプロパティは永続化出力から除外する（書き込めないため不要、`[InlineReference]` は除く）

### セレクタオブジェクト

- 子オブジェクトへのアクセスはセレクタID＋インデックスを含むパスで行う
- レスポンスの `id` / `path` はセレクタ基準になる

### LivePropertyRef（プロパティ参照型）

- 他の LiveObject のプロパティへの **エイリアス参照** を表すフィールド型
- 集約ページ (例: `FusionPage`) でレイアウト・属性をコード上にまとめつつ、値・dirty・revert は実体側に一元化する
- 参照側のフィールドに `[Section]` / `[Slider]` / `[Help]` 等の UI メタ属性を付け、`LivePropertyRef.To<T>(memberName)` で対象を指定する
- 参照先が未生成のときは null を返す（RemoteApp 側は値なし扱い）。default の二重管理は発生しない
- 編集可否は参照先のプロパティに従う（参照側が `static readonly` でも実体が書き込み可能なら編集できる）

## ライフサイクルと永続化

### ライフサイクル

- **Play開始**: 初期化（登録+デフォルトキャプチャ）→ 保存データ読み込み
- **Play停止**: Delta保存 → デフォルト値に復元
- 復元はデフォルトJSONの再適用で行う（プロパティ単位のRevertではない）

### 永続化

- Delta形式：初期状態からの差分のみ保存する
- デフォルト値は初期化時に1回だけキャプチャする
- デフォルトキャプチャは永続化と同じ条件（read-only除外）で行う
- 読み込み中にデフォルトを再キャプチャしてはならない（適用後の値がデフォルトになるため）
- **inline 子オブジェクトの defaults 登録**: Container 初期化時は `_objects` に加え、`ResolveLiveObjects` で BFS 走査した inline UnityEngine.Object 参照（コンポーネント、ScriptableObject 等）についても `EnsureDefaultsCaptured` を呼ぶ。これを行わないと、pending エントリの delta 計算で defaults が見つからずフォールバック（差分ゼロ＝metadata only 扱い）になり、実際の変更が保存されない
- デフォルト値のレジストリ (`LiveObjectDefaultRegistry`) は **target 参照** をキーとして保持するため、同じ target を指す別インスタンスの `CreateUnregistered` LiveObject からも同一 defaults を参照できる

### ResolveLiveObjects

- Container / 呼び出し側が渡したルートから BFS で依存 LiveObject を探索するユーティリティ
- 結果リストには `@id` 付きのルート LiveObject に加えて、**inline 子オブジェクト**（`hasId=false` な `CreateUnregistered` 生成物）も含める
  - 呼び出し側（Container.Initialize やテスト）が結果を走査して `SetDefault` / `EnsureDefaultsCaptured` を呼べるようにするため
  - `SceneToJson` のトップレベル出力ループは `hasId` チェックで非 root エントリをスキップするので、リストに含めても出力が二重化することはない
- BFS は target インスタンスベースで重複排除する（同じ target を指す複数参照を一度だけ訪問）

## シーンファイル

### フォーマット

- ルートは `{ format, formatVersion, metadata, objects: [...] }`
- 各エントリは `@type` と、`@source` または `@id` のいずれかを持つ
- メタキーの役割:
  - `@source` — 既存オブジェクトの上書き対象を指す source-key (ルートのみ / プロパティパス付きの 2 形式)
  - `@id` — 新規作成エントリの id 宣言
  - `@ref` — 他エントリへの参照 (プロパティ値中、source-key 空間を共有)
  - `@prefab` — Prefab Asset GUID。新規エントリで Prefab 由来のときに付与
- エントリ種別は 3 つ:
  - **既存ルート上書き** — 登録済み LiveObject にプロパティを上書き適用
  - **Pending 子参照** — 未登録の UnityEngine.Object 参照を親ルート + パスで辿って上書き
  - **新規インスタンス** — `@prefab` から Prefab 化、無ければ Activator / 型探索で生成
- 出力は `SerializeMode` に従う:
  - Snapshot は全プロパティを出力
  - Delta は defaults との差分のみ。差分ゼロのエントリは省かれ、変更なしなら `objects: []` になる
- UnityEngine.Object 参照は走査中に file-scope の `@ref` に置換し、本体は別エントリとして展開する。Texture2D など inline 値型は対象外

### SceneFromJson

- 読み込みは複数パス: Prefab 生成 → ルート Registry 登録 → FileRegistry 構築 → プロパティ適用
- Factory 登録型はシーン上の既存オブジェクトから解決する (空インスタンスを作らない)
- プロパティ適用時は defaults を再キャプチャしない (Container.Initialize 時のものを使う)
- pending 子参照は適用直前に target 参照キーで defaults を捕捉して、保存時の delta 計算と整合させる

### `@instanceID` メタデータ

- target が UnityEngine.Object の LiveObject に副次ルックアップキーとして付与する
- 用途: Registry 未登録のオブジェクト (selector 経由でのみアクセスされる AvatarController 等) を変更フィードで更新通知できるようにする
- primary key は従来通り `@id` / selector 合成キー。RemoteApp は受信 id で直引きが失敗したときだけ instanceID で翻訳する
- 永続化 JSON には含めない (セッション依存のため)。再接続後は RemoteApp 側の再フェッチで整合する

### `@truncated` メタデータ と ネスト深度

- REST の `GET /live/objects` / `GET /live/object/{id}` は既定で **深さ1** で返す。すなわち対象オブジェクト自身のプロパティ値は直列化するが、値がネストした inline（未登録・`hasId=false`）の複合オブジェクトの場合は、その中身を展開せず **打ち切りスタブ** `{ "@type": <typeName>, "@truncated": true }` に置換する（`@name` / `@instanceID` は従来条件で付与）。
  - 目的: 全件×全ネストの展開を避け、レスポンスをスケーラブルに保つ。クライアントは「ここに子がいる」ことと型を知り、必要時に `GET /live/object/{id}/{path}` で遅延取得する。
- **配列は深度を消費しない**。配列プロパティの要素は配列と同じ深さで直列化されるため、要素数と各要素の型はスタブでも残る（UI が個数・順序を描画できる）。プリミティブ / string / enum / Vector 等の値型リーフは深さに関係なく常に値を出す。
- **登録済み子** (`hasId=true`) は従来どおり `@ref` で表現され、深度の影響を受けない（もともと深さ1相当）。`@truncated` が付くのは inline 子のみ。
- `?nested`（または `?nested=true` / `?nested=1`）を付けると従来どおり **無限展開**で返る。`?nested=false` / `?nested=0` は既定（深さ1）。
- **`@truncated` は読み取り専用マーカー**。デシリアライズ (`FromJson` / PUT) 側ではプロパティ名でないため無視され、書き戻しても子を消さない。
- **`GET /live/object/{id}/{path}`（property GET）は `nested` に非対応で常にフル展開**。パス指定でレスポンスが既に1ブランチに限定されており、深さ1にすると正当な「ブランチ全量取得」が要素ごとの追加リクエストに分解されるため。PUT 応答・永続化 (scene/project/preset) も従来どおりフル展開（深度は `forPersistence` 時に無限へ強制される）。

## 通信

### REST API

- レスポンスの `id` / `path` はプロパティ所有者の視点で返る（リクエスト時と異なる場合がある）
- `changed` はデフォルト値からの差分有無を示す。データ更新の成否ではない

### InstanceID フォールバック

- REST API の `exposed/object/{id}` は `LiveObjectRegistry` 経由で id を解決する
- Registry で見つからない場合の最終フォールバックとして、数値 id を `UnityEngine.Object.FindObjectFromInstanceID` にかけて逆引きし、`LiveObject.CreateUnregistered` で一時的にラップして返す（レジストリ登録はしない）
- リフレクションは `LiveObjectUtility.InstanceIDToObject` にまとめる
- REST API の単一 LiveObject ToJson は従来どおりの形式（`@ref + @id + @name`）を維持する。file-scope の `@source` 付きエントリは SceneToJson 経由のときのみ有効

### 変更通知

- プロパティ変更は値を送らず、`LiveChangeLog` に**変更されたオブジェクトの id だけ**を記録する
- クライアントは `GET /live/changes?since={revision}` をポーリングし、自分が保持している id だけを再取得する
- `since` 省略時は現在の revision のみを返す（接続直後の同期用）
- 擬似 id `@types` / `@ui` はそれぞれ型テーブルとサイドメニューの再取得を意味する
