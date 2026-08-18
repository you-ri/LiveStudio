// Copyright (c) You-Ri, 2026

using System.Collections.Generic;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Choosing which avatar is out.
    /// <para>
    /// Loading and unloading assets is kind-agnostic — an asset names the single-selection group it
    /// belongs to (<see cref="AssetBase.exclusiveGroup"/>) and the manager reconciles without knowing
    /// what the groups mean. Picking an avatar <em>by display name</em> is avatar-only business, so it
    /// lives here instead of on the general-purpose manager.
    /// </para>
    /// <para>
    /// ⚠ Matching is by display name, not id (same as the UE port). A switch is recorded in a deck
    /// file, and a name a person typed travels between machines while a path-derived id does not.
    /// </para>
    /// </summary>
    public static class AvatarSelection
    {
        /// <summary>
        /// Display names of the registered avatars, with an empty entry first that represents
        /// "none / default avatar". Used as the option source for an avatar selector UI.
        /// </summary>
        public static string[] GetNames(ExternalAssetManager manager)
        {
            var names = new List<string> { string.Empty };
            if (manager == null) return names.ToArray();

            var view = manager.assetsView;
            for (int i = 0; i < view.Count; i++)
            {
                if (_IsAvatar(view[i])) names.Add(view[i].name ?? string.Empty);
            }
            return names.ToArray();
        }

        /// <summary>Name of the currently selected avatar, or empty when on the default avatar.</summary>
        public static string GetSelectedName(ExternalAssetManager manager)
        {
            if (manager == null) return string.Empty;

            var view = manager.assetsView;
            for (int i = 0; i < view.Count; i++)
            {
                if (_IsAvatar(view[i]) && view[i].enabled) return view[i].name ?? string.Empty;
            }
            return string.Empty;
        }

        /// <summary>
        /// Selects the avatar with the given display name (empty resets to the default avatar), driving
        /// the same reconcile as toggling the asset's <see cref="AssetBase.enabled"/> flag directly.
        /// Turning the others off is the group reconcile's job, so this only raises the chosen one.
        /// </summary>
        public static void SelectByName(ExternalAssetManager manager, string avatarName)
        {
            if (manager == null) return;

            var view = manager.assetsView;
            if (string.IsNullOrEmpty(avatarName))
            {
                // Drop the current avatar; the reconcile then falls back to the default one.
                for (int i = 0; i < view.Count; i++)
                {
                    if (_IsAvatar(view[i]) && view[i].enabled) manager.SetAssetEnabled(view[i].id, false);
                }
                return;
            }

            for (int i = 0; i < view.Count; i++)
            {
                if (!_IsAvatar(view[i]) || view[i].name != avatarName) continue;
                manager.SetAssetEnabled(view[i].id, true);
                return;
            }
        }

        /// <summary>
        /// Whether the entry is listed as an avatar.
        /// <para>
        /// ⚠ Decided by the declared group, not by the C# type. "Shows up as an avatar" is the same
        /// question as "belongs to the avatar group", and is separate from how the instance is produced
        /// (bundle, built-in, swapped in). Testing the type would mean editing this every time a new
        /// avatar-shaped kind is added.
        /// </para>
        /// </summary>
        private static bool _IsAvatar(AssetBase asset)
        {
            return asset != null && asset.exclusiveGroup == AvatarAssetBase.AvatarGroup;
        }
    }
}
