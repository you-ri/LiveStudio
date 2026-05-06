using System;
using UnityEngine;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    public static class AvatarService
    {
        public static void LoadVRM(string id, string filepath)
        {
            SelectableService<IAvatarService>.Select(id).RequestLoadVRM(filepath);
        }

        public static void ResetAvatar(string id)
        {
            SelectableService<IAvatarService>.Select(id).ResetAvatar();
        }

    }

}
