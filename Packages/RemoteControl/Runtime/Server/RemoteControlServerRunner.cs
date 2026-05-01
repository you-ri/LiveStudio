using System.Collections;
using System.Threading;
using System;

using UnityEngine;
using Lilium.RemoteControl;

namespace Lilium.RemoteControl.Server
{
    /// <summary>
    /// スタジオ用のRemoteControlServerアダプター
    /// </summary>
    public class RemoteControlServerRunner : MonoBehaviour
    {
        private const int kDefaultPort = 3002;

        public RemoteControlServerConfig serverConfig => _serverConfig;

        [SerializeField]
        [Tooltip("Server configuration to use")]
        private RemoteControlServerConfig _serverConfig;

        private RemoteControlServerCore _server;

        // サーバーの保持者かどうか
        // エディター終了時にサーバーを停止するかどうかの判定に使用
        private bool _isServerOwner;

        // UpdateHandlers実行用のスレッドと実行中フラグ
        private Thread _updateThread;
        private volatile bool _isRunning = false;

        public int GetPort()
        {
            return _serverConfig?.port ?? kDefaultPort;
        }

        void Awake()
        {
            if (!RemoteControlServerManager.IsServerRunning(GetPort()))
            {
                StartServer();
            }
        }

        void OnDestroy()
        {
            StopUpdateThread();
            ShutdownServer();
        }


        public void StartServer()
        {
            if (_server == null)
            {
                var port = GetPort();

                // Containerを取得してServerに渡す
                var container = GetComponent<ExposedObjectContainer>();
                if (container == null)
                {
                    container = FindObjectOfType<ExposedObjectContainer>();
                }

                _server = RemoteControlServerManager.GetOrCreateServer(port, _serverConfig, container);

                if (_server == null)
                {
                    Debug.LogWarning($"[Studio] StudioRemoteControlServerAdapter: No server configuration found for port {port}. Please configure server in RemoteControlServerWindow.");
                    return;
                }

                if (!_server.IsRunning)
                {
                    _server.StartServer();

                    // サーバー起動に失敗した場合、マネージャーから削除してクリーンアップ
                    if (!_server.IsRunning)
                    {
                        Debug.LogWarning($"[RemoteControl] Server failed to start on port {port}. Cleaning up.");
                        RemoteControlServerManager.RemoveServer(port);
                        _server = null;
                        return;
                    }

                    _isServerOwner = true;
                }

                StartUpdateThread();
            }
        }

        private void StartUpdateThread()
        {
            if (_updateThread != null && _updateThread.IsAlive)
            {
                return;
            }

            _isRunning = true;
            _updateThread = new Thread(() =>
            {
                while (_isRunning)
                {
                    try
                    {
                        if (_server != null && _server.IsRunning)
                        {
                            _server.UpdateHandlers();
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[Studio] Error in UpdateHandlers: {ex.Message}");
                    }

                    Thread.Sleep(1);
                }
            })
            {
                IsBackground = true,
                Name = $"StudioRemoteControlAdapterUpdate_{GetPort()}"
            };

            _updateThread.Start();
        }

        private void StopUpdateThread()
        {
            if (_updateThread != null && _updateThread.IsAlive)
            {
                _isRunning = false;

                if (!_updateThread.Join(TimeSpan.FromSeconds(1)))
                {
                    Debug.LogWarning($"[Studio] UpdateHandlers thread did not stop in time");
                }

                _updateThread = null;
            }
        }

        public void ShutdownServer()
        {
            StopUpdateThread();

            if (_isServerOwner && _server != null)
            {
                _server.StopServer();
                RemoteControlServerManager.RemoveServer(GetPort());
            }
            _server = null;
            _isServerOwner = false;
        }
   }
}