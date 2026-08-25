using System.Runtime.CompilerServices;

// テストアセンブリからinternalメンバーへのアクセスを許可
[assembly: InternalsVisibleTo("Lilium.RemoteControl.Editor.Tests")]
[assembly: InternalsVisibleTo("Lilium.RemoteControl.Tests")]

// シーン読み書きモジュール (LiveSceneSerializer / LiveSceneSaveSystem 等) から
// FileScopedResolver や LiveObjectContainer._objects 等の internal メンバーへのアクセスを許可
[assembly: InternalsVisibleTo("Lilium.RemoteControl.LiveScene")]

// 通信モジュール (HTTP サーバーとハンドラ群) から、シリアライザ等の internal メンバーへの
// アクセスを許可。REST の応答を組み立てるのに必要で、公開 API に昇格させたくないもの。
[assembly: InternalsVisibleTo("Lilium.RemoteControl.Server")]