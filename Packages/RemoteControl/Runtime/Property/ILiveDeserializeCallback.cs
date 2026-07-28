// Copyright (c) You-Ri, 2026
namespace Lilium.RemoteControl
{
    /// <summary>
    /// Implement this interface on an [LiveClass] type to receive a single
    /// callback after <see cref="LivePropertySerializer.FromJson"/> finishes
    /// writing all persistable properties on this instance.
    ///
    /// Use this hook to apply the deserialized field values to external state
    /// that the property setters would normally update (e.g. Unity engine state,
    /// wrapped components). The custom JSON deserializer writes fields via raw
    /// reflection and bypasses property setters, so any side effect normally
    /// performed in a setter must be re-applied here.
    ///
    /// Called exactly once per object after all properties on that object have
    /// been deserialized. Nested LiveObjectHandle instances each get their own
    /// callback.
    /// </summary>
    public interface ILiveDeserializeCallback
    {
        void OnAfterLiveDeserialize();
    }
}
