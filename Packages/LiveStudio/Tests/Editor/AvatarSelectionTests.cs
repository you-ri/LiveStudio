// Copyright (c) You-Ri, 2026

using System;
using System.Reflection;
using System.Threading.Tasks;

using NUnit.Framework;

using Lilium.LiveStudio;

namespace Lilium.LiveStudio.EditorTests
{
    /// <summary>
    /// Which avatar the selection reports as chosen.
    ///
    /// Selecting one raises its <see cref="AssetBase.enabled"/> flag and leaves lowering the others
    /// to the manager's reconcile, so between the request and the next diff two flags are up. A
    /// reader that answered "the first raised flag in the list" therefore named whichever avatar was
    /// registered earlier -- which, right after someone picked a new one, is the old one. That answer
    /// travelled into recordings as the value of the write that had just been made: a switch to one
    /// avatar written down as a switch to another.
    /// </summary>
    public class AvatarSelectionTests
    {
        /// <summary>An avatar asset that loads nothing. Only its flags and name matter here.</summary>
        private sealed class FakeAvatarAsset : AvatarAssetBase
        {
            public override Task LoadAsync(AssetLoadContext context) => Task.CompletedTask;

            public override void Unload(AssetLoadContext context) { }
        }

        private static readonly FieldInfo kAssetsField = typeof(ExternalAssetManager)
            .GetField("assets", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo kSelectedField = typeof(ExternalAssetManager)
            .GetField("_selectedExclusiveId", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// Puts assets in the manager without going through file discovery.
        ///
        /// By reflection because the list is filled by loading files, and what is under test is how
        /// the selection is read rather than how the list is built.
        /// </summary>
        private static ExternalAssetManager ManagerWith(params AssetBase[] assets)
        {
            Assert.IsNotNull(kAssetsField, "ExternalAssetManager.assets was renamed; update this test");

            var manager = new ExternalAssetManager();
            kAssetsField.SetValue(manager, assets);
            return manager;
        }

        /// <summary>
        /// Says the manager has already settled on this asset, as it has after any reconcile.
        ///
        /// The distinction between "the selection" and "a newly raised flag" only exists once there
        /// is a selection to compare against. A manager that has never reconciled has no answer to
        /// give and falls back to list order, which is the state this stands in for the absence of.
        /// </summary>
        private static void Settle(ExternalAssetManager manager, AssetBase asset)
        {
            Assert.IsNotNull(kSelectedField, "ExternalAssetManager._selectedExclusiveId was renamed; update this test");
            kSelectedField.SetValue(manager, asset?.id);
        }

        private static FakeAvatarAsset Avatar(string name, bool enabled)
        {
            return new FakeAvatarAsset { id = Guid.NewGuid().ToString(), name = name, enabled = enabled };
        }

        [Test]
        public void WithNothingEnabled_TheDefaultAvatarIsReported()
        {
            var manager = ManagerWith(Avatar("Lapwing FT", false), Avatar("MAYO FT", false));

            Assert.AreEqual(string.Empty, AvatarSelection.GetSelectedName(manager));
        }

        [Test]
        public void TheEnabledAvatarIsReported()
        {
            var mayo = Avatar("MAYO FT", true);
            var manager = ManagerWith(Avatar("Lapwing FT", false), mayo);
            Settle(manager, mayo);

            Assert.AreEqual("MAYO FT", AvatarSelection.GetSelectedName(manager));
        }

        /// <summary>
        /// The regression this exists for. Both flags are up because the reconcile has not run yet,
        /// and the newly chosen one is the answer -- not the one that comes first in the list.
        /// </summary>
        [Test]
        public void BetweenTheRequestAndTheReconcile_TheNewlyChosenAvatarIsReported()
        {
            var lapwing = Avatar("Lapwing FT", true);
            var manager = ManagerWith(lapwing, Avatar("MAYO FT", false));
            Settle(manager, lapwing);

            AvatarSelection.SelectByName(manager, "MAYO FT");

            Assert.AreEqual("MAYO FT", AvatarSelection.GetSelectedName(manager),
                "the previous avatar was reported as the selection right after a switch");
        }

        [Test]
        public void SelectingNothing_ReportsTheDefaultAvatar()
        {
            var lapwing = Avatar("Lapwing FT", true);
            var manager = ManagerWith(lapwing, Avatar("MAYO FT", false));
            Settle(manager, lapwing);

            AvatarSelection.SelectByName(manager, string.Empty);

            Assert.AreEqual(string.Empty, AvatarSelection.GetSelectedName(manager));
        }

        [Test]
        public void ANullManagerReportsTheDefaultAvatar()
        {
            Assert.AreEqual(string.Empty, AvatarSelection.GetSelectedName(null));
        }
    }
}
