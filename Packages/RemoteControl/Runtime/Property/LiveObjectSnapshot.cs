// Copyright (c) You-Ri, 2026

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Public facade for capturing a single <see cref="LiveObjectHandle"/>'s persistable
    /// property values to a JSON string and restoring them onto a (possibly different) handle
    /// for the same logical target.
    ///
    /// This wraps the internal <see cref="LivePropertySerializer"/> so callers outside this
    /// assembly (e.g. LiveStudio's prop manager) can snapshot/restore an object across an
    /// unload/reload cycle, where the GameObject is destroyed and re-instantiated and the new
    /// instance must be brought back to the previously edited state.
    /// </summary>
    public static class LiveObjectSnapshot
    {
        /// <summary>
        /// Serializes all persistable property values of <paramref name="handle"/> to JSON.
        /// Uses <c>forPersistence: true</c>, which strips session-bound metadata (e.g. instance
        /// ids) and excludes <c>persistable = false</c> fields, so the result is safe to store
        /// and reapply later.
        /// </summary>
        public static string Capture(LiveObjectHandle handle)
        {
            return LivePropertySerializer.ToJson(
                handle, DefaultLiveObjectResolver.Instance, isDirtyOnly: false, forPersistence: true);
        }

        /// <summary>
        /// <see cref="Capture(LiveObjectHandle)"/> restricted to the members declared with
        /// <paramref name="scope"/>. Lets an owner of <see cref="PersistScope.Custom"/> members write
        /// exactly those to its own file, the same way the live scene and the project settings write
        /// their own scopes. Restoring needs no counterpart: deserialization ignores the scope.
        /// </summary>
        public static string Capture(LiveObjectHandle handle, PersistScope scope)
        {
            return LivePropertySerializer.ToJson(
                handle, DefaultLiveObjectResolver.Instance, isDirtyOnly: false, forPersistence: true,
                scopeFilter: scope);
        }

        /// <summary>
        /// Serializes only the persistable property values of <paramref name="handle"/> that differ
        /// from its captured defaults (the delta). Requires defaults to have been captured for the
        /// handle's target beforehand (see <see cref="LiveObjectDefaultRegistry.CaptureDefaults"/>);
        /// without a baseline the current values are treated as the defaults and the result is empty.
        /// Uses <c>forPersistence: true</c> so the delta is safe to store and reapply later.
        /// </summary>
        public static string CaptureDelta(LiveObjectHandle handle)
        {
            return LivePropertySerializer.ToJson(
                handle, DefaultLiveObjectResolver.Instance, isDirtyOnly: true, forPersistence: true);
        }

        /// <summary>
        /// <see cref="CaptureDelta(LiveObjectHandle)"/> restricted to the members declared with
        /// <paramref name="scope"/>, for an owner writing its own file (see
        /// <see cref="Capture(LiveObjectHandle, PersistScope)"/>). The baseline the delta is taken
        /// against covers every scope, so the result holds exactly the members of this scope that
        /// differ from their defaults.
        /// </summary>
        public static string CaptureDelta(LiveObjectHandle handle, PersistScope scope)
        {
            return LivePropertySerializer.ToJson(
                handle, DefaultLiveObjectResolver.Instance, isDirtyOnly: true, forPersistence: true,
                scopeFilter: scope);
        }

        /// <summary>
        /// Writes down what <paramref name="handle"/>'s <paramref name="scope"/> members hold right now as
        /// their defaults, unless that was already done. An owner calls this before anything has been
        /// applied to the object, so a later delta says what was changed rather than everything.
        /// </summary>
        public static void EnsureScopedDefaults(LiveObjectHandle handle, PersistScope scope)
        {
            LiveObjectDefaultRegistry.EnsureDefaultsCaptured(handle, DefaultLiveObjectResolver.Instance, scope);
        }

        /// <summary>
        /// The captured defaults of <paramref name="handle"/>, restricted to the members declared with
        /// <paramref name="scope"/>, as a JSON string ready for <see cref="Restore"/>. Restoring it puts
        /// those members back to what the object started with, leaving every other scope alone — what an
        /// owner needs before applying another subject's saved values onto a shared object (the one
        /// <c>AvatarController</c> driving whichever avatar is out). Null when no baseline was captured.
        /// </summary>
        public static string CaptureScopedDefaults(LiveObjectHandle handle, PersistScope scope)
        {
            var defaults = LiveObjectDefaultRegistry.GetDefaults(handle, scope);
            if (defaults == null) return null;

            var result = new Newtonsoft.Json.Linq.JObject();
            foreach (var property in defaults.Properties())
            {
                if (property.Name.Length > 0 && property.Name[0] == '@')
                {
                    result[property.Name] = property.Value.DeepClone();
                    continue;
                }
                var member = handle.targetType?.FindProperty(property.Name);
                if (member == null || !member.isPersistable || member.persistScope != scope) continue;
                result[property.Name] = property.Value.DeepClone();
            }
            return result.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>
        /// Restores values previously produced by <see cref="Capture"/> onto <paramref name="handle"/>.
        /// Treats the restored values as authoritative (<c>captureDefaults: false</c>) and invokes
        /// <see cref="ILiveDeserializeCallback.OnAfterLiveDeserialize"/> on the target.
        /// </summary>
        public static bool Restore(string json, LiveObjectHandle handle)
        {
            return LivePropertySerializer.FromJson(
                json, handle, DefaultLiveObjectResolver.Instance, captureDefaults: false);
        }
    }
}
