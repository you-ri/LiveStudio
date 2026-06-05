// Copyright (c) You-Ri, 2026

namespace Lilium.RemoteControl
{
    /// <summary>
    /// ファクトリ要素のアクセスレベル
    /// </summary>
    public enum AccessLevel
    {
        /// <summary>公開（常に表示）</summary>
        Public = 0,
        /// <summary>実験的機能</summary>
        Experimental = 1,
        /// <summary>開発ビルドでのみ表示</summary>
        Development = 2,
    }

    public interface IExposedObjectFactory
    {
        string name { get; }

        AccessLevel accessLevel { get; }

        IExposedObject Create();

        void Destroy(IExposedObject obj);

        void RegisterPrefabs();

#if UNITY_EDITOR
        /// <summary>
        /// Editor 時に prefab Asset から GUID を解決し、シリアライズフィールドへ反映する。
        /// Runtime からは呼ばれない。
        /// </summary>
        void RefreshPrefabKey();
#endif
    }
}
