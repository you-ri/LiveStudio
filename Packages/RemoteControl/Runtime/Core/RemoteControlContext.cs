using Lilium.RemoteControl.Core;
using UnityEngine;

namespace Lilium.RemoteControl.Server
{
    /// <summary>
    /// RemoteControlServer インスタンスごとのコンテキスト
    /// 複数のサーバーインスタンスを独立して動作させるための依存関係をカプセル化
    /// </summary>
    public class RemoteControlContext
    {
        /// <summary>
        /// このサーバーが扱うExposedObjectContainer
        /// </summary>
        public ExposedObjectContainer objectContainer { get; }

        /// <summary>
        /// このサーバーインスタンス専用のイベントキュー
        /// </summary>
        public EventQueue eventQueue { get; }

        /// <summary>
        /// このサーバーインスタンス専用の接続マネージャー
        /// </summary>
        public RestApiConnectionManager connectionManager { get; }

        /// <summary>
        /// このコンテキストのスコープ識別子（シーン名、ポート番号など）
        /// </summary>
        public string scope { get; }

        /// <summary>
        /// WebUI定義（オプション）。設定されている場合は /webui エンドポイントが有効になる。
        /// </summary>
        public ScriptableObject webUIDefinition { get; }

        /// <summary>
        /// RemoteControlContext を作成
        /// </summary>
        /// <param name="scope">スコープ識別子（デフォルト: "default"）</param>
        /// <param name="webUIDefinition">WebUI定義（オプション）</param>
        /// <param name="container">ExposedObjectContainer（オプション）</param>
        public RemoteControlContext(string scope = "default", ScriptableObject webUIDefinition = null, ExposedObjectContainer container = null)
        {
            this.objectContainer = container;
            this.scope = scope;
            this.webUIDefinition = webUIDefinition;
            this.eventQueue = new EventQueue();
            this.connectionManager = new RestApiConnectionManager();
        }
    }
}
