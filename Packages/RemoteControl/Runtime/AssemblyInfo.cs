using System.Runtime.CompilerServices;

// テストアセンブリからinternalメンバーへのアクセスを許可
[assembly: InternalsVisibleTo("Lilium.RemoteControl.Editor.Tests")]
[assembly: InternalsVisibleTo("Lilium.RemoteControl.Tests")]

// シーン読み書きモジュール (ExposedSceneSerializer / SceneSaveSystem 等) から
// FileScopedResolver や ExposedObjectContainer._objects 等の internal メンバーへのアクセスを許可
[assembly: InternalsVisibleTo("Lilium.RemoteControl.Scene")]