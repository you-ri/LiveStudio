// Copyright (c) You-Ri, 2026

using System.Threading.Tasks;

using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// .vrm ファイル用の外部アバターローダー。
    /// 既存の <see cref="VRMLoader"/> をラップする。完了通知は <see cref="VRMLoadObserver"/> の
    /// ブロードキャスト（ExternalAvatarSource 自身 + VrmLoadApiHandler の進捗通知）が担うため、
    /// <see cref="LoadAsync"/> は GameObject を直接返さず null を返す。
    /// </summary>
    internal sealed class VrmExternalAvatarLoader : IExternalAvatarLoader
    {
        public async Task<GameObject> LoadAsync(string filePath, Transform parent)
        {
            await VRMLoader.LoadVRMModel(filePath, parent);
            // 完了は IVRMLoadObserver.OnVRMLoaded 経由で通知されるため、ここでは GameObject を返さない。
            return null;
        }

        public void Dispose()
        {
        }
    }
}
