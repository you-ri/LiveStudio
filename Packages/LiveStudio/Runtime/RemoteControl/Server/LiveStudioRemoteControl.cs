// Copyright (c) You-Ri, 2026

using Lilium.RemoteControl.Server;
using Lilium.RemoteControl.RestApi;
using Lilium.RemoteControl.UI;
using Lilium.Virgo.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// LiveStudio アプリ用に Remote Control ランタイム一式を保持する単一 MonoBehaviour。
    /// サーバ・ExposedObject コンテナ・シーン保存/読込・UI サイドバー
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

        protected override void OnRegisterHandlers(RemoteControlServerCore server)
        {
            _cameraHandler = new CameraApiHandler(server);
            server.RegisterRoute("/api/camera", _cameraHandler);

            _manipulatorHandler = new ManipulatorApiHandler(server);
            server.RegisterRoute("/api/manipulator", _manipulatorHandler);

            _vrmLoadHandler = new VrmLoadApiHandler(server);
            server.RegisterRoute("/api/vrm/load", _vrmLoadHandler);

            _inputActionsHandler = new InputActionsApiHandler(server);
            server.RegisterRoute("/api/input-actions", _inputActionsHandler);
            server.RegisterRoute("/api/input-actions/bind", _inputActionsHandler);

            _expressionsHandler = new ExpressionsApiHandler(server);
            server.RegisterRoute("/api/expressions", _expressionsHandler);

            _commandsHandler = new CommandsApiHandler(server);
            server.RegisterRoute("/api/commands", _commandsHandler);

            _resetHandler = new ResetApiHandler(server);
            server.RegisterRoute("/api/commands/reset", _resetHandler);

            _quitHandler = new QuitApiHandler(server);
            server.RegisterRoute("/api/commands/quit", _quitHandler);
        }

        protected override void OnUnregisterHandlers(RemoteControlServerCore server)
        {
            server.UnregisterRoute("/api/camera");
            server.UnregisterRoute("/api/manipulator");
            server.UnregisterRoute("/api/vrm/load");
            server.UnregisterRoute("/api/input-actions");
            server.UnregisterRoute("/api/input-actions/bind");
            server.UnregisterRoute("/api/expressions");
            server.UnregisterRoute("/api/commands");
            server.UnregisterRoute("/api/commands/reset");
            server.UnregisterRoute("/api/commands/quit");

            _cameraHandler?.Cleanup();
            _manipulatorHandler?.Cleanup();
            _vrmLoadHandler?.Cleanup();
            _inputActionsHandler?.Cleanup();
            _expressionsHandler?.Cleanup();
            _commandsHandler?.Cleanup();
            _resetHandler?.Cleanup();
            _quitHandler?.Cleanup();

            _cameraHandler = null;
            _manipulatorHandler = null;
            _vrmLoadHandler = null;
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
            _expressionsHandler?.Update();
        }
    }
}
