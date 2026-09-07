// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using System.Threading.Tasks;

using UnityEngine;

using Lilium.RemoteControl;
using Lilium.RemoteControl.Frames;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Fetches the prefab behind a prop instance a recording names, so a replay can stand it back up.
    ///
    /// The structure lane rebuilds an instance from the same key a saved scene writes as <c>@prefab</c>,
    /// through <c>PrefabRecipe</c> — which can only answer for a prefab already in hand. A built-in prop's
    /// key resolves that way (its catalogue entry loads from Resources synchronously), but an external
    /// <c>*.prop.lsb</c> prop's key is a project-relative path whose prefab has to be read out of a bundle
    /// first. Nothing was doing that read: the take carried the key, no maker could answer for it, and the
    /// instance simply never came back.
    ///
    /// So this stands as a second maker-resolver and does one thing — it starts the load and puts the result
    /// in the <see cref="PrefabRegistry"/>. It never creates the object. That is deliberate: the thing that
    /// creates is the reconcile, which is looking at the inventory of the frame it is on, so an instance
    /// that was scrubbed away while its bundle was being read is simply never made. A resolver that stood
    /// objects up on load completion would have to answer for a world that had moved on.
    ///
    /// While the read is in flight the replay is held (<see cref="FrameGate.HoldSupply"/>) rather than run
    /// on without the instance: how long a bundle takes is a property of this machine, not of the take. It
    /// is the same wait <see cref="ExternalAssetManager"/> takes for an asset load.
    ///
    /// Lives in LiveStudio because it reaches the asset manager, which the generic RemoteControl layer does
    /// not depend on (mirrors <see cref="PropObjectFactory"/>).
    /// </summary>
    public static class PropInstanceRecipeResolver
    {
        /// <summary>What a stalled replay is waiting on, for a viewer to show.</summary>
        private const string kLoadHold = "prop load";

        // Keys being read right now. Held so a reconcile that runs every frame asks for one load rather
        // than one per frame, and so the hold is balanced exactly once per key.
        private static readonly HashSet<string> _loading = new HashSet<string>();

        // Keys whose owner could not produce a prefab (a bundle file that is gone). Kept because the loader
        // reports a missing file as an error, and a reconcile retrying every frame would turn one broken
        // prop into a console full of the same line. Dropped when the catalogue changes, which is the only
        // thing that can make the answer different.
        private static readonly HashSet<string> _failed = new HashSet<string>();

        private static bool _watchingCatalog;

        /// <summary>
        /// Puts this resolver in front of the recipe table.
        ///
        /// The catalogue is not subscribed to here on purpose: <see cref="ExternalAssetManager"/> clears
        /// that event in the same startup phase this runs in, and which of the two goes first is not
        /// something either can say. Subscribed on first use instead, which is long after both.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Install()
        {
            _loading.Clear();
            _failed.Clear();
            _watchingCatalog = false;

            LiveRecipes.AddResolver(_Resolve);
        }

#if UNITY_EDITOR
        // The recorder also runs in an editor that is not playing, where the runtime hook never fires.
        [UnityEditor.InitializeOnLoadMethod]
        private static void _InstallInEditor() => Install();
#endif

        private static ILiveRecipe _Resolve(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            // Already in hand: the key belongs to PrefabRecipe from here on. This is also the case right
            // after a load this resolver ran, which is how a fetched prefab reaches the maker that uses it.
            if (PrefabRegistry.TryFind(key, out var known) && known != null) return null;

            if (_loading.Contains(key) || _failed.Contains(key)) return null;

            // Asked for the owner before the prefab, so a key nothing here claims stays somebody else's
            // question. Returning "not mine" quietly is what lets several resolvers stand at once.
            var prop = ExternalAssetManager.FindInstantiableProp(key);
            if (prop == null) return null;

            _WatchCatalog();

            var load = prop.LoadInstancePrefabAsync();
            if (load == null) return null;

            // A prop whose bundle was already read hands back a finished task, so the instance comes back
            // on this frame instead of costing the replay a stall for something that is in memory.
            if (load.IsCompleted)
            {
                _Adopt(key, load.Status == TaskStatus.RanToCompletion ? load.Result : null);

                // Back through the table rather than making the recipe here: the maker is PrefabRecipe's to
                // own, and it now answers for this key. One level deep only -- the guard above sends this
                // resolver straight back out for a key that is in the registry.
                return LiveRecipes.TryGet(key, out var recipe) ? recipe : null;
            }

            _loading.Add(key);
            FrameGate.HoldSupply(kLoadHold);
            _ = _AwaitLoad(key, load);

            // Nothing to make yet. The reconcile counts it as unresolved for this frame and the gate stops
            // supplying frames, so the next reconcile is the one that lands after the prefab arrives.
            return null;
        }

        private static async Task _AwaitLoad(string key, Task<GameObject> load)
        {
            try
            {
                _Adopt(key, await load);
            }
            catch (System.Exception e)
            {
                _failed.Add(key);
                Debug.LogError($"[LiveStudio] PropInstanceRecipeResolver: loading '{key}' failed: {e.Message}");
            }
            finally
            {
                _loading.Remove(key);
                FrameGate.ReleaseSupply(kLoadHold);
            }
        }

        // Takes the load's answer: registers what came back, or remembers that nothing did.
        private static void _Adopt(string key, GameObject prefab)
        {
            if (prefab == null)
            {
                _failed.Add(key);
                return;
            }

            PrefabRegistry.Register(key, prefab);
        }

        private static void _WatchCatalog()
        {
            if (_watchingCatalog) return;

            _watchingCatalog = true;
            ExternalAssetManager.onCatalogChanged -= _OnCatalogChanged;
            ExternalAssetManager.onCatalogChanged += _OnCatalogChanged;
        }

        // A crawl can bring back the file that was missing, so what failed is worth trying once more.
        private static void _OnCatalogChanged() => _failed.Clear();

        /// <summary>Forgets what is in flight and what failed. For tests.</summary>
        internal static void Reset()
        {
            _loading.Clear();
            _failed.Clear();
        }
    }
}
