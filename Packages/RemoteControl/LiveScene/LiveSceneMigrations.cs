// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;

using Newtonsoft.Json.Linq;

namespace Lilium.RemoteControl.LiveScene
{
    /// <summary>
    /// Brings a live scene file written by an older build forward, before any of it is read.
    ///
    /// A restore hands each entry in <c>objects[]</c> to the object it names, so an entry naming a
    /// member this build no longer has is not an error anything can report -- it finds no owner and
    /// is dropped. A member that moved therefore takes the show's state with it in silence, and the
    /// scene opens looking merely empty. This is where that is answered, in the one place the whole
    /// file is still in front of us: the parsed JSON, before the passes run.
    ///
    /// <para>
    /// Registered by the host application rather than built in here. What an old file meant is
    /// knowledge of that application's own classes; this package knows only *when* to ask, so the
    /// hook lives here and every rewrite lives beside the classes it is about.
    /// </para>
    /// <para>
    /// Reading only, and one way. A rewrite changes what was just read off disk; nothing writes the
    /// old shape again, so opening and saving once settles a file for good. The file on disk is
    /// untouched until the operator saves, which is what makes a migration safe to get wrong: the
    /// original is still there.
    /// </para>
    /// </summary>
    public static class LiveSceneMigrations
    {
        // Keyed so an installer that runs more than once -- [RuntimeInitializeOnLoadMethod] and
        // [InitializeOnLoadMethod] both fire in the editor -- replaces its own entry instead of
        // stacking a second copy of the same rewrite. A list rather than a dictionary because the
        // order they were registered in is the order they run.
        private static readonly List<KeyValuePair<string, Action<JObject>>> _migrations
            = new List<KeyValuePair<string, Action<JObject>>>();

        /// <summary>
        /// Registers a rewrite to run over every live scene about to be restored, replacing whatever
        /// was registered under the same <paramref name="id"/>.
        ///
        /// Not cleared on a domain reload, the same way <see cref="PrefabRegistry.RegisterResolver"/>
        /// is not: what is registered is a stable function its owner installs once, not per-run state.
        /// </summary>
        public static void Register(string id, Action<JObject> migration)
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("A migration needs an id to be replaceable.", nameof(id));
            if (migration == null) throw new ArgumentNullException(nameof(migration));

            var entry = new KeyValuePair<string, Action<JObject>>(id, migration);
            for (int i = 0; i < _migrations.Count; i++)
            {
                if (_migrations[i].Key != id) continue;
                _migrations[i] = entry;
                return;
            }
            _migrations.Add(entry);
        }

        /// <summary>Takes one back out. True when there was one to take.</summary>
        public static bool Unregister(string id)
        {
            for (int i = 0; i < _migrations.Count; i++)
            {
                if (_migrations[i].Key != id) continue;
                _migrations.RemoveAt(i);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Runs every registered rewrite over the parsed file, in the order they were registered.
        ///
        /// Not guarded: a rewrite that throws is a bug in the rewrite, and swallowing it here would
        /// turn it into the silent half-restore the whole mechanism exists to prevent.
        /// </summary>
        public static void Apply(JObject root)
        {
            if (root == null) return;
            for (int i = 0; i < _migrations.Count; i++) _migrations[i].Value(root);
        }
    }
}
