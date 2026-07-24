using System;
using UnityEngine;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    public static class AvatarService
    {
        public static void Load(string id, string filepath)
        {
            SelectableService<IAvatarService>.Select(id).RequestLoad(filepath);
        }

        public static void LoadPrefab(string id, GameObject prefab)
        {
            SelectableService<IAvatarService>.Select(id).RequestLoadPrefab(prefab);
        }

        public static void ResetAvatar(string id)
        {
            SelectableService<IAvatarService>.Select(id).ResetAvatar();
        }

    }

}
