# jp.lilium.remotecontrol

Unity向けREST APIベースのリモートコントロールパッケージ

## 概要

`jp.lilium.remotecontrol` は、外部アプリケーション（VirgoMotionRemoteなど）からUnityアプリケーションをリモート制御するためのREST API基盤を提供します。リアルタイム通信にはREST APIとServer-Sent Events（SSE）を使用します。
`[ExposedClass]` および `[ExposedProperty]` `[ExpossedField]` 属性でマークされたクラスやコンポーネントのプロパティは、リモートアプリケーションから制御できます。
非再生中(Editorモード)でも動作し操作可能です。

## 機能

- REST APIによるUnityアプリケーションのリモート制御
- Server-Sent Events（SSE）によるリアルタイム通知
- 属性ベースのプロパティ公開システム
- 設定の永続化のためのエクスポート/インポート機能
- Unity 2022.3 以降をサポート

## クイックスタート

### 1. ExposerAssetの作成

公開オブジェクトを管理するExposerAssetを作成します：
- Projectウィンドウで右クリック
- **Create > Virgo Motion > Exposer Asset** を選択

### 2. シーンのセットアップ

1. シーン内のGameObjectに `RemoteControlProvider` コンポーネントを追加
2. プロバイダーにExposerAssetを割り当て

### 3. クラスを公開対象としてマーク

```csharp
using Lilium.RemoteControl;

[ExposedClass]
public class MySettings : MonoBehaviour
{
    [ExposedProperty]
    public float volume { get; set; } = 1.0f;

    [ExposedProperty]
    [Slider(0, 100)]
    public int brightness { get; set; } = 50;

    [ExposedFunction]
    public void Reset()
    {
        volume = 1.0f;
        brightness = 50;
    }
}
```

## 属性一覧

| 属性 | 対象 | 説明 |
|------|------|------|
| `[ExposedClass]` | クラス/構造体 | リモート制御の公開対象として型をマーク |
| `[ExposedProperty]` | プロパティ/フィールド | リモートからの取得/設定用にプロパティを公開 |
| `[ExposedFunction]` | メソッド | リモートからのメソッド呼び出しを許可 |
| `[ExposedEnum]` | 列挙型 | 列挙型の型定義を公開 |
| `[ExposedHelp("text")]` | 任意 | ヘルプ/説明テキストを追加 |
| `[Slider(min, max)]` | プロパティ | 最小値/最大値を指定したスライダーUIを設定 |
| `[ExposedDefault]` | 静的プロパティ | 構造体のカスタムデフォルト値を定義 |
| `ExposedPropertyRef` | フィールド型 | 他クラスの ExposedProperty を **エイリアス参照**するための readonly struct。集約ページで他コンポーネントのプロパティをまとめて表示する場合に使う。値・dirty・revert は参照先に委譲される (詳細は `Documentation~/ExposedObjectSpec.md`) |

## REST APIエンドポイント

### システムエンドポイント

| メソッド | エンドポイント | 説明 |
|----------|---------------|------|
| GET | `/api/status` | アプリケーションステータスを取得（名前、バージョン、FPS） |
| GET | `/api/stream` | SSEイベントストリームに接続 |
| GET/POST | `/api/heartbeat` | 接続維持用ハートビート |

### 公開オブジェクトエンドポイント

| メソッド | エンドポイント | 説明 |
|----------|---------------|------|
| GET | `/exposed/objects` | 全公開オブジェクトの一覧を取得 |
| GET | `/exposed/objects?type={typeName}` | 型名でオブジェクトをフィルタリング |
| GET | `/exposed/object/{id}` | IDでオブジェクトを取得 |
| GET | `/exposed/object/{id}/{path}` | プロパティ値を取得 |
| PUT | `/exposed/object/{id}/{path}` | プロパティ値を設定 |
| POST | `/exposed/object/{id}/{path}` | 配列要素を追加 |
| DELETE | `/exposed/object/{id}/{path}` | 配列要素を削除 |
| POST | `/exposed/object/{id}/{path}/reset` | プロパティをデフォルト値にリセット |

### 型定義エンドポイント

| メソッド | エンドポイント | 説明 |
|----------|---------------|------|
| GET | `/exposed/types` | 全公開型定義の一覧を取得 |
| GET | `/exposed/types?type={typeName}` | 特定の型定義を取得 |
| GET | `/exposed/enums` | 全公開列挙型定義の一覧を取得 |

### 関数エンドポイント

| メソッド | エンドポイント | 説明 |
|----------|---------------|------|
| POST | `/exposed/function/{id}/{functionName}` | 公開関数を呼び出し |

### データ管理エンドポイント

| メソッド | エンドポイント | 説明 |
|----------|---------------|------|
| POST | `/exposed/export` | 設定をファイルにエクスポート |
| POST | `/exposed/import` | ファイルから設定をインポート |

## 翻訳システム（Localization）

`LocalizationSystem` は、キーベースの翻訳テキスト解決を提供する静的クラスです。RemoteAppに送信するラベルやヘルプテキストを多言語対応するために使用します。

### 仕組み

1. **翻訳データの形式**: 言語ごとにJSONファイルで管理（`{ "key": "翻訳テキスト" }` 形式）
2. **翻訳の解決**: `LocalizationSystem.Translate(key)` でキーに対応する翻訳を返す。見つからない場合はキーをそのまま返す（フォールバック）
3. **言語の決定**: PlayerPrefs → システム言語の順で自動決定。REST API (`/api/language`) から変更可能

### 翻訳データの登録方法

外部パッケージから翻訳データを登録するには、`LoadTranslations` を使用します。

```csharp
// Resources フォルダに JSON ファイルを配置
// 例: Resources/MyPackageLocales/ja.json
//     Resources/MyPackageLocales/en.json

[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
private static void Initialize()
{
    LoadLocale("en");
    LoadLocale("ja");
}

private static void LoadLocale(string language)
{
    var textAsset = Resources.Load<TextAsset>($"MyPackageLocales/{language}");
    if (textAsset != null)
    {
        LocalizationSystem.LoadTranslations(language, textAsset.text);
        Resources.UnloadAsset(textAsset);
    }
}
```

### 翻訳JSONファイルの例

```json
{
    "Light": "ライト",
    "Camera": "カメラ",
    "Background": "背景",
    "Screen": "画面",
    "Specify a 360-degree image.": "360度画像を指定してください。"
}
```

### 翻訳が適用される箇所

| 対象 | 説明 |
|------|------|
| メニューラベル | WebUIのサイドメニュー項目の `label` |
| ヘルプテキスト | `[ExposedHelp("...")]` で指定したテキスト |

### ディレクトリ構成例

```
Runtime/
  Localization/
    Resources/
      MyPackageLocales/
        en.json       # 英語（キーと同じなら空 {} でも可）
        ja.json       # 日本語
```

### REST APIエンドポイント

| メソッド | エンドポイント | 説明 |
|----------|---------------|------|
| GET | `/api/language` | 現在の言語と利用可能な言語一覧を取得 |
| PUT | `/api/language` | 言語を変更（`{"language":"ja"}`） |

## 内部仕様

ExposedObjectの登録・永続化・シリアライズに関する仕様は [Documentation~/ExposedObjectSpec.md](Documentation~/ExposedObjectSpec.md) を参照。このシステムに変更を加える際は必ず仕様書を確認すること。

## Source Generator (Lilium.RemoteControl.SourceGenerator)

`Plugins/Lilium.RemoteControl.SourceGenerator.dll` は Roslyn IIncrementalGenerator で、`[ExposedClass]` を持つ型のメンバー宣言順を C# ソースから抽出して runtime に提供する。これにより `[ExposedProperty]` `[ExposedField]` `[ExposedFunction]` を `order` 未設定で混在させたとき、RemoteApp の DynamicObjectPane に **ソース宣言順** で並ぶ。

ソースは `SourceGenerator~/Lilium.RemoteControl.SourceGenerator/` 配下、ビルド成果物 (DLL) はこのパッケージに **commit する**運用。フォルダ末尾の `~` は Unity に無視させるためのマーカー (Unity が C# ソースをアセンブリとしてコンパイルしないようにするため)。

### Generator を変更したとき

```powershell
# Windows (PowerShell 5.1 / 7+)
./SourceGenerator~/build.ps1
```

スクリプトが `dotnet build -c Release` した上で DLL を `Plugins/` にコピーする。source change と DLL の両方を git commit すること (DLL を含めずに push すると、他環境で旧 generator が動く)。

`dotnet` SDK (.NET 6+) が必要。consumer 側は SDK 不要 (ビルド済み DLL を使うだけ)。

### Roslyn バージョン制約

Source Generator は **Roslyn 4.0** を参照している (Unity 2022.3 の古いパッチを含めて広く動かすため)。Unity 側の compiler version が generator のものより低いと `CS9057` 警告が出て generator が無効化されるため、`src/Lilium.RemoteControl.SourceGenerator/Lilium.RemoteControl.SourceGenerator.csproj` の `Microsoft.CodeAnalysis.CSharp` バージョンは **常に Unity の最低サポート compiler バージョン以下** に保つこと。

Roslyn 4.3 で導入された API (例: `ForAttributeWithMetadataName`) は使わない。代わりに 4.0 互換の `CreateSyntaxProvider` パターンを使う。

generator が無効化された環境では `ExposedClassDeclarationOrderTable.Register` が呼ばれず、Reflection の MetadataToken フォールバックに落ちる (= kind ごとにブロック化されたメンバー順)。

## ライセンス

本パッケージは [MIT License](LICENSE.md) の下で配布されます。
Copyright (c) 2026 You-Ri

### サードパーティ依存

| 依存パッケージ | ライセンス | 用途 |
|---|---|---|
| [Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json) (`com.unity.nuget.newtonsoft-json`) | MIT | JSON シリアライズ (ランタイム依存) |
| [Roslyn (Microsoft.CodeAnalysis)](https://github.com/dotnet/roslyn) | MIT | Source Generator のビルド時のみ参照 (DLL は再配布されない) |

詳細は [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) を参照してください。
