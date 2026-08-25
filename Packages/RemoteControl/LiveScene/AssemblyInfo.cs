// Copyright (c) You-Ri, 2026

using System.Runtime.CompilerServices;

// 通信モジュールから、シーン読み書きの internal メンバーへのアクセスを許可。
// シーンに置くホスト部品とシーン入出力の受け口が通信側にあるため必要。
[assembly: InternalsVisibleTo("Lilium.RemoteControl.Server")]
[assembly: InternalsVisibleTo("Lilium.RemoteControl.Editor.Tests")]
