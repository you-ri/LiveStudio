// Copyright (c) You-Ri, 2026

using Lilium.RemoteControl.Server;
using Lilium.RemoteControl.RestApi;
using Lilium.RemoteControl.UI;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// LiveStudio アプリ用に Remote Control ランタイム一式を保持する単一 MonoBehaviour。
    /// サーバ・ExposedObjectHandle コンテナ・シーン保存/読込・UI サイドバー
    /// (<see cref="UIRemoteControlBehaviour"/> 経由) を束ね、LiveStudio 固有の
    /// API ハンドラ (Camera / Manipulator / VrmLoad / InputActions / Expressions / Commands /
    /// Reset / Quit) を上乗せ登録する。
    /// </summary>
    public class LiveStudioRemoteControl : UIRemoteControlBehaviour
    {
        private CameraApiHandler _cameraHandler;
        private ManipulatorApiHandler _manipulatorHandler;
        private InputActionsApiHandler _inputActionsHandler;
        private ExpressionsApiHandler _expressionsHandler;
        private CommandsApiHandler _commandsHandler;
        private ResetApiHandler _resetHandler;
        private QuitApiHandler _quitHandler;
        private VrmLoadApiHandler _vrmLoadHandler;
        private AvatarImageHandler _avatarImageHandler;
        private AnimationClipsHandler _animationClipsHandler;
        private AssetsHandler _assetsHandler;

        protected override void OnRegisterHandlers(RemoteControlServerCore server)
        {
            _cameraHandler = new CameraApiHandler(server);
            server.RegisterRoute(_cameraHandler);

            _manipulatorHandler = new ManipulatorApiHandler(server);
            server.RegisterRoute(_manipulatorHandler);

            _vrmLoadHandler = new VrmLoadApiHandler(server);
            server.RegisterRoute(_vrmLoadHandler);

            _avatarImageHandler = new AvatarImageHandler(server);
            server.RegisterRoute(_avatarImageHandler);

            _animationClipsHandler = new AnimationClipsHandler(server);
            server.RegisterRoute(_animationClipsHandler);

            _assetsHandler = new AssetsHandler(server);
            server.RegisterRoute(_assetsHandler);

            // Serves both /api/input-actions and /api/input-actions/bind via its own Routes.
            _inputActionsHandler = new InputActionsApiHandler(server);
            server.RegisterRoute(_inputActionsHandler);

            _expressionsHandler = new ExpressionsApiHandler(server);
            server.RegisterRoute(_expressionsHandler);

            _commandsHandler = new CommandsApiHandler(server);
            server.RegisterRoute(_commandsHandler);

            _resetHandler = new ResetApiHandler(server);
            server.RegisterRoute(_resetHandler);

            _quitHandler = new QuitApiHandler(server);
            server.RegisterRoute(_quitHandler);
        }

        protected override void OnUnregisterHandlers(RemoteControlServerCore server)
        {
            // UnregisterRoute calls handler.Cleanup() internally.
            server.UnregisterRoute(_cameraHandler);
            server.UnregisterRoute(_manipulatorHandler);
            server.UnregisterRoute(_vrmLoadHandler);
            server.UnregisterRoute(_avatarImageHandler);
            server.UnregisterRoute(_animationClipsHandler);
            server.UnregisterRoute(_assetsHandler);
            server.UnregisterRoute(_inputActionsHandler);
            server.UnregisterRoute(_expressionsHandler);
            server.UnregisterRoute(_commandsHandler);
            server.UnregisterRoute(_resetHandler);
            server.UnregisterRoute(_quitHandler);

            _cameraHandler = null;
            _manipulatorHandler = null;
            _vrmLoadHandler = null;
            _avatarImageHandler = null;
            _animationClipsHandler = null;
            _assetsHandler = null;
            _inputActionsHandler = null;
            _expressionsHandler = null;
            _commandsHandler = null;
            _resetHandler = null;
            _quitHandler = null;
        }

        protected override void OnUpdateHandlers()
        {
            _cameraHandler?.Update();
            _inputActionsHandler?.Update();
        }
    }
}
