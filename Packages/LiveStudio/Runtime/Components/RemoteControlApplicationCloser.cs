using System;
using System.Net.Http;
using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// リモートコントロールサーバーに対して終了/リセットを要求するコンポーネント
    /// </summary>
    public class RemoteControlApplicationCloser : MonoBehaviour
    {
        [Header("Connection Settings")]
        [SerializeField] private string _host = "127.0.0.1";
        [SerializeField] private int _port = 3002;

        private static readonly HttpClient _httpClient = new HttpClient();

        public string host
        {
            get => _host;
            set => _host = value;
        }

        public int port
        {
            get => _port;
            set => _port = value;
        }

        private string _BaseUrl => $"http://{_host}:{_port}";

        /// <summary>
        /// リモートアプリケーションに終了を要求
        /// </summary>
        /// <returns>成功した場合はtrue</returns>
        public bool RequestQuit()
        {
            return _SendCommand("/api/commands/quit");
        }

        /// <summary>
        /// リモートアプリケーションにリセットを要求
        /// </summary>
        /// <returns>成功した場合はtrue</returns>
        public bool RequestReset()
        {
            return _SendCommand("/api/commands/reset");
        }

        /// <summary>
        /// POSTコマンドを送信
        /// </summary>
        private bool _SendCommand(string endpoint)
        {
            try
            {
                var url = $"{_BaseUrl}{endpoint}";
                _httpClient.Timeout = TimeSpan.FromSeconds(2);
                _ = _httpClient.PostAsync(url, null);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Studio] Failed to send command {endpoint}: {ex.Message}");
                return false;
            }
        }

        private void OnApplicationQuit()
        {
            if (isActiveAndEnabled)
            {
                RequestQuit();
            }
        }
    }
}
