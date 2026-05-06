// Copyright (c) You-Ri, 2026

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    public static class AvatarBuildNotifier
    {
        public static void NotifyAvatarBuilt(in AvatarBuildData data)
        {
            var subjects = Service<IAvatarBuildObserver>.subjects;
            for (int i = 0; i < subjects.Count; i++)
            {
                subjects[i].OnAvatarBuilt(in data);
            }
        }
    }
}
