using System;

namespace Lilium.RemoteControl
{
    public interface IExposedObjectResolver
    {
        public ExposedObject FindById(string id);
        public ExposedObject FindByTarget(object target);
    }

    /// <summary>
    /// ファイル（シーン）スコープ専用のリゾルバー拡張。
    /// ExposedSceneSerializer が保存時に利用し、プロパティ走査中の UnityEngine.Object 参照を
    /// fileid ベースの @ref に置き換えつつ、参照先を別エントリとして収集する。
    /// REST API など file-scope 外の経路は通常の <see cref="IExposedObjectResolver"/> を使うため
    /// 既存挙動に影響しない。
    /// </summary>
    public interface IFileScopedResolver : IExposedObjectResolver
    {
        /// <summary>
        /// 現在のプロパティパスにセグメントを追加する（配列添字は "[i]" 形式）。
        /// </summary>
        void PushPath(string segment);

        /// <summary>
        /// <see cref="PushPath"/> で積んだ最上位セグメントを取り除く。
        /// </summary>
        void PopPath();

        /// <summary>
        /// 現在処理中の root ExposedObject を設定する。null で解除。
        /// </summary>
        void SetCurrentRoot(ExposedObject root);

        /// <summary>
        /// UnityEngine.Object 参照を fileid 付きの @ref トークンとしてエンコードし、
        /// 本体を objects[] に後で書き出せるよう内部キューに登録する。
        /// </summary>
        /// <returns>置換用の @ref トークン（"{ "@ref": "{guid}" }" 相当）</returns>
        Newtonsoft.Json.Linq.JToken EncodeUnityObjectReference(UnityEngine.Object obj);
    }

    /// <summary>
    /// デフォルトのリゾルバー（ExposedObjectRegistry.FindById と FindByTarget を直接呼び出す）
    /// </summary>
    public class DefaultExposedObjectResolver : IExposedObjectResolver
    {
        public static readonly DefaultExposedObjectResolver Instance = new DefaultExposedObjectResolver();

        public ExposedObject FindById(string id)
        {
            return ExposedObjectRegistry.FindById(id);
        }

        public ExposedObject FindByTarget(object target)
        {
            return ExposedObjectRegistry.FindByTarget(target);
        }
    }
}
