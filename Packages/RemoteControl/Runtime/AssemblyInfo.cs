using System.Runtime.CompilerServices;

// テストアセンブリからinternalメンバーへのアクセスを許可
[assembly: InternalsVisibleTo("Lilium.RemoteControl.Editor.Tests")]
[assembly: InternalsVisibleTo("Lilium.RemoteControl.Tests")]

// シーン読み書きモジュール (LiveSceneSerializer / LiveSceneSaveSystem 等) から
// FileScopedResolver や LiveObjectContainer._objects 等の internal メンバーへのアクセスを許可
[assembly: InternalsVisibleTo("Lilium.RemoteControl.LiveScene")]