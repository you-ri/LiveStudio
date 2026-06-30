// Copyright (c) You-Ri, 2026

using UnityEngine;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// A plain avatar prop loaded under an avatar (from a <c>*.prop.lsb</c>). It binds to a named socket
    /// via its <see cref="attachment"/> (socket name + position / rotation / scale offsets) and follows
    /// the socket world pose each frame. This is one of the mutually-exclusive prop behavior components
    /// (<see cref="IProp"/>): an item (<see cref="AvatarItem"/>) or a chair (<see cref="AvatarChair"/>)
    /// takes its place on the prop root when those behaviors are needed — they are never combined.
    ///
    /// The follow is applied directly to the transform in <see cref="LateUpdate"/> (after the avatar's
    /// bones are posed for the frame); offsets / scale are read every frame so runtime edits apply
    /// immediately.
    /// </summary>
    [DefaultExecutionOrder(20)] // after the avatar (VRCFTAvatar order 10) has posed its bones this frame
    [ExposedClass("Prop", Category = "Avatar", Icon = "deployed_code")]
    public class Prop : MonoBehaviour, IProp
    {
        [ExposedProperty("name"), Hide]
        public string displayName => this.name;

        // Shared socket-attachment surface (socket name + offsets + follow math), exposed as a nested
        // "PropAttachment" under this component's @type.
        [SerializeField, ExposedField]
        public PropAttachment attachment = new PropAttachment();

        void Start()
        {
            attachment.CaptureBaseScale(transform);
        }

        void Update()
        {
            // Apply the scale offset every frame so runtime edits take effect immediately. The follow
            // drives position / rotation only, so scale is owned here; the avatar's own scale still
            // propagates through the parent's lossyScale.
            attachment.ApplyScale(transform);
        }

        void LateUpdate()
        {
            // Follow the socket directly, after the avatar's bones are posed for the frame.
            if (attachment.TryResolveFollowTarget(out var worldPos, out var worldRot))
            {
                transform.SetPositionAndRotation(worldPos, worldRot);
            }
        }
    }
}
