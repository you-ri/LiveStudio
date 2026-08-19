// Copyright (c) You-Ri, 2026

using Lilium.RemoteControl.Server;
using Lilium.RemoteControl.RestApi;
using Lilium.RemoteControl.UI;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// LiveStudio アプリ用に Remote Control ランタイム一式を保持する単一 MonoBehaviour。
    /// サーバ・LiveObjectHandle コンテナ・シーン保存/読込・UI サイドバー
    /// (<see cref="UIRemoteControlBehaviour"/> 経由) を束ね、共通の
    /// API ハンドラ (Reset / Quit) を上乗せ登録し、
    /// アバター読み込みの進捗通知 (<see cref="VrmLoadNotifier"/>) を立てる。
    /// LiveStudio 固有の REST ルートは 1 本も無い: カメラもマニピュレーターも
    /// 汎用面 (RemoteControl) が担う。
    /// </summary>
    public class LiveStudioRemoteControl : UIRemoteControlBehaviour
    {
        private ResetApiHandler _resetHandler;
        private QuitApiHandler _quitHandler;
        private VrmLoadNotifier _vrmLoadNotifier;

        protected override void OnRegisterHandlers(RemoteControlServerCore server)
        {
            // Cameras have no route at all: a camera is a live object (LiveCamera), going live is
            // its `Switch` live function, and the preview picture is its `preview` image member,
            // served by the generic property GET. The manipulator's routes moved into RemoteControl
            // itself (they only ever touched live objects and TransformValue), so this app
            // registers nothing for it either.

            // Avatar loading has no route of its own: a file is registered with the asset manager
            // and enabled, or a model path property is written, both on the generic /live/object
            // surface. Only the progress of that load needs pushing, so this is a notifier, not a route.
            _vrmLoadNotifier = new VrmLoadNotifier(server);

            // Asset preview pictures — a snapshot's screenshot included, since a snapshot is a project asset
            // like any other — are served by RemoteControl's own /live/asset/{key}/@image; this app only supplies
            // the bytes, through the hook AssetThumbnailProvider registers.

            // Input actions have no route of their own: the input map is a live object
            // (AvatarInput, "InputActions"), each action is an element of its `actions` array, and
            // rebinding one is that element's `Rebind` live function.

            // Expressions have no route of their own: the active avatar's expression list is a live
            // function (getavailableexpressions) and each weight is an ordinary live property
            // (expressions[<name>].weight), both served by the generic /live/object surface.

            _resetHandler = new ResetApiHandler(server);
            server.RegisterRoute(_resetHandler);

            _quitHandler = new QuitApiHandler(server);
            server.RegisterRoute(_quitHandler);
        }

        protected override void OnUnregisterHandlers(RemoteControlServerCore server)
        {
            // UnregisterRoute calls handler.Cleanup() internally.
            server.UnregisterRoute(_resetHandler);
            server.UnregisterRoute(_quitHandler);

            _vrmLoadNotifier?.Dispose();

            _vrmLoadNotifier = null;
            _resetHandler = null;
            _quitHandler = null;
        }
    }
}
