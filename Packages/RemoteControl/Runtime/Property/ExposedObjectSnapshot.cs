// Copyright (c) You-Ri, 2026

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Public facade for capturing a single <see cref="ExposedObjectHandle"/>'s persistable
    /// property values to a JSON string and restoring them onto a (possibly different) handle
    /// for the same logical target.
    ///
    /// This wraps the internal <see cref="ExposedPropertySerializer"/> so callers outside this
    /// assembly (e.g. LiveStudio's prop manager) can snapshot/restore an object across an
    /// unload/reload cycle, where the GameObject is destroyed and re-instantiated and the new
    /// instance must be brought back to the previously edited state.
    /// </summary>
    public static class ExposedObjectSnapshot
    {
        /// <summary>
        /// Serializes all persistable property values of <paramref name="handle"/> to JSON.
        /// Uses <c>forPersistence: true</c>, which strips session-bound metadata (e.g. instance
        /// ids) and excludes <c>persistable = false</c> fields, so the result is safe to store
        /// and reapply later.
        /// </summary>
        public static string Capture(ExposedObjectHandle handle)
        {
            return ExposedPropertySerializer.ToJson(
                handle, DefaultExposedObjectResolver.Instance, isDirtyOnly: false, forPersistence: true);
        }

        /// <summary>
        /// Serializes only the persistable property values of <paramref name="handle"/> that differ
        /// from its captured defaults (the delta). Requires defaults to have been captured for the
        /// handle's target beforehand (see <see cref="ExposedObjectDefaultRegistry.CaptureDefaults"/>);
        /// without a baseline the current values are treated as the defaults and the result is empty.
        /// Uses <c>forPersistence: true</c> so the delta is safe to store and reapply later.
        /// </summary>
        public static string CaptureDelta(ExposedObjectHandle handle)
        {
            return ExposedPropertySerializer.ToJson(
                handle, DefaultExposedObjectResolver.Instance, isDirtyOnly: true, forPersistence: true);
        }

        /// <summary>
        /// Restores values previously produced by <see cref="Capture"/> onto <paramref name="handle"/>.
        /// Treats the restored values as authoritative (<c>captureDefaults: false</c>) and invokes
        /// <see cref="IExposedDeserializeCallback.OnAfterExposedDeserialize"/> on the target.
        /// </summary>
        public static bool Restore(string json, ExposedObjectHandle handle)
        {
            return ExposedPropertySerializer.FromJson(
                json, handle, DefaultExposedObjectResolver.Instance, captureDefaults: false);
        }
    }
}
