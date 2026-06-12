// Copyright (c) You-Ri, 2026

using UnityEngine;
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

        /// <summary>
        /// Extracts avatar build data from the animator's humanoid avatar and
        /// notifies observers. Shared by avatar components to avoid duplicating
        /// the same null/empty validation. Returns false (and logs an error
        /// prefixed with <paramref name="ownerLabel"/>) when the animator has no
        /// valid humanoid avatar.
        /// </summary>
        public static bool BuildAndNotify(Animator animator, string ownerLabel)
        {
            if (animator == null || animator.avatar == null)
            {
                Debug.LogError($"[Studio] {ownerLabel}: Animator or Avatar is null.");
                return false;
            }

            var humanDescription = animator.avatar.humanDescription;
            var avatarBuildData = AvatarBuildSystem.CreateAvatarBuildData(animator.transform, humanDescription);
            if (avatarBuildData.humanBones == null || avatarBuildData.humanBones.Length == 0)
            {
                Debug.LogError($"[Studio] {ownerLabel}: Failed to extract Avatar data.");
                return false;
            }

            NotifyAvatarBuilt(in avatarBuildData);
            return true;
        }
    }
}
