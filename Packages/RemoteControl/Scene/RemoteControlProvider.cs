// Copyright (c) You-Ri, 2026
using Lilium.RemoteControl;

namespace Lilium.RemoteControl.Scene
{
    /// <summary>
    /// 後方互換用の薄いエイリアス。実体は <see cref="SceneSaveSystem"/> に移動した。
    /// 既存の呼び出し元（RemoteControlBehaviour.sceneSave / LiveStudioPathsInitializer 等）が
    /// 型名 <c>RemoteControlProvider</c> を参照し続けられるよう、サブクラスとして残す。
    /// </summary>
    public class RemoteControlProvider : SceneSaveSystem
    {
        public RemoteControlProvider(ExposedObjectContainer objectContainer, string defaultFileName, bool autoSaveOnQuit = true)
            : base(objectContainer, defaultFileName, autoSaveOnQuit)
        {
        }
    }
}
