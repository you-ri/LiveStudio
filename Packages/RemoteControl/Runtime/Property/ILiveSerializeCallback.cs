// Copyright (c) You-Ri, 2026
namespace Lilium.RemoteControl
{
    /// <summary>
    /// Implement this interface on an [LiveClass] type to receive a single
    /// callback immediately before <see cref="LivePropertySerializer"/> reads
    /// persistable properties on this instance into JSON.
    ///
    /// Use this hook to refresh shadow fields whose canonical value is derived
    /// from external state (e.g. wrapped Unity components, runtime configuration
    /// objects). Without it, a stale shadow value may be written to JSON because
    /// the serializer reads the field directly and does not invoke property
    /// getters.
    ///
    /// Called exactly once per object before any property on that object is
    /// serialized. Nested LiveObjectHandle instances each get their own callback.
    /// </summary>
    public interface ILiveSerializeCallback
    {
        void OnBeforeLiveSerialize();
    }
}
