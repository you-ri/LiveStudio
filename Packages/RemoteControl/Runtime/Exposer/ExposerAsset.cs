// このファイルは削除予定。IExposedObjectResolverとDefaultExposedObjectResolverはIExposedObjectResolver.csに移動済み。
// ExposerAssetクラスは廃止。既存アセット参照が壊れないよう空クラスとして残す。
using UnityEngine;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// 廃止: ExposerAssetは不要になりました。
    /// 既存のシリアライズ参照が壊れないよう型定義のみ残しています。
    /// </summary>
    [System.Obsolete("ExposerAsset is no longer needed. Use ExposedPropertyUtility and IExposedObjectResolver directly.")]
    public class ExposerAsset : ScriptableObject
    {
    }
}
