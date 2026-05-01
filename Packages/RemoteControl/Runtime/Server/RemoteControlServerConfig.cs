using UnityEngine;


namespace Lilium.RemoteControl.Server
{

    /// <summary>
    /// Remote Control Server設定をScriptableObjectとして管理
    /// 各サーバーインスタンスの設定を個別のアセットファイルとして保存
    /// </summary>
    public class RemoteControlServerConfig : ScriptableObject
    {
        [Tooltip("Server port number")]
        public int port = 3002;

        [Tooltip("Enable CORS for cross-origin requests")]
        public bool enableCors = true;

        [Tooltip("Keep this server running in Unity Editor")]
        public bool runningInEditor = false;

        public virtual RemoteControlServerCore CreateServer()
        {
            return null;
        }

        public virtual RemoteControlServerCore CreateServer(ExposedObjectContainer container)
        {
            return CreateServer();
        }

    }


}
