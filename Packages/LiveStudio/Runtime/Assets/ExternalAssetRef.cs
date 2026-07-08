// Copyright (c) You-Ri, 2026

using System;
using System.Threading.Tasks;

using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Runtime resolution state for a single reference to an asset that lives inside an external bundle
    /// (e.g. an <see cref="AnimationClip"/> in a <c>*.anim.lsb</c>). Such an asset does not exist until
    /// its bundle is loaded asynchronously, so the durable reference is a key string (resolved via a
    /// supplied async resolver), not a live <see cref="UnityEngine.Object"/>. This type owns the small
    /// state machine that keeps a resolved asset in sync with a changing key — the caller keeps only the
    /// persisted key field and this helper, instead of a handful of loose bookkeeping members.
    ///
    /// A reference type (not a struct): it holds mutable resolution state that an async continuation
    /// updates through <c>this</c>, which a value copy would lose.
    ///
    /// Not thread-safe; drive it from the main thread (the resolver's continuation resumes there, as the
    /// bundle loaders complete on the main thread).
    /// </summary>
    public sealed class ExternalAssetRef<T> where T : UnityEngine.Object
    {
        private readonly Func<string, Task<T>> _resolver;

        // The resolved asset for _appliedKey, or null (empty key / failed / not-yet-resolved).
        private T _asset;

        // The key currently reflected in _asset. Empty = cleared.
        private string _appliedKey = string.Empty;

        // The key whose resolution is in flight, or null. Used to (a) skip a redundant re-resolve of the
        // same key and (b) supersede a stale resolution when a newer Sync started a different one.
        private string _resolvingKey;

        public ExternalAssetRef(Func<string, Task<T>> resolver)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        }

        /// <summary>The resolved asset for the last applied key, or null.</summary>
        public T asset => _asset;

        /// <summary>
        /// True when <paramref name="key"/> is the one currently reflected in <see cref="asset"/> — i.e.
        /// resolution has settled for it. Callers use this to hold off applying while a resolve is still
        /// in flight, so the current state is not clobbered mid-load.
        /// </summary>
        public bool IsResolved(string key)
            => string.Equals(_appliedKey ?? string.Empty, key ?? string.Empty, StringComparison.Ordinal);

        /// <summary>
        /// Brings <see cref="asset"/> in line with <paramref name="key"/>: a no-op when the key is already
        /// applied or already resolving, otherwise starts an async resolve. An empty key clears the asset.
        /// <paramref name="onResolved"/> is invoked once resolution settles (on the main thread), so the
        /// caller can (re)apply the newly resolved asset.
        /// </summary>
        public void Sync(string key, Action onResolved)
        {
            var target = key ?? string.Empty;
            if (string.Equals(target, _appliedKey ?? string.Empty, StringComparison.Ordinal)) return;
            if (string.Equals(target, _resolvingKey ?? string.Empty, StringComparison.Ordinal)) return;
            _ = _ResolveAsync(target, onResolved);
        }

        private async Task _ResolveAsync(string key, Action onResolved)
        {
            _resolvingKey = key;

            T resolved = null;
            if (!string.IsNullOrEmpty(key))
            {
                resolved = await _resolver(key);

                // Superseded: a newer Sync started resolving a different key while we awaited. Abandon so
                // the latest intent wins (its own continuation applies).
                if (!string.Equals(_resolvingKey ?? string.Empty, key, StringComparison.Ordinal)) return;
            }

            _asset = resolved;
            _appliedKey = key;
            _resolvingKey = null;
            onResolved?.Invoke();
        }
    }
}
