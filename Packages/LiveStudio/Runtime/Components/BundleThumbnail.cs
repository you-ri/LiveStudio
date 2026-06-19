// Copyright (c) You-Ri, 2026

using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Holds a preview thumbnail image packed inside a LiveStudio bundle
    /// (<c>*.scene.lsb</c> / <c>*.avatar.lsb</c> / <c>*.prop.lsb</c>, see <see cref="LiveStudioBundle"/>).
    ///
    /// The image is stored as the raw encoded bytes (PNG / JPEG) rather than a <see cref="Texture2D"/>
    /// so it is self-contained (no readable/compression import settings to worry about) and can be
    /// served straight over REST without a Texture2D round-trip — matching the bytes a VRM's embedded
    /// thumbnail yields. An exporter authors one of these into the bundle at build time; a loader reads
    /// it back after the bundle is opened.
    /// </summary>
    [CreateAssetMenu(
        fileName = "BundleThumbnail",
        menuName = "Lilium Live Studio/Bundle Thumbnail")]
    public sealed class BundleThumbnail : ScriptableObject
    {
        /// <summary>Conventional asset name used when authoring this into a bundle.</summary>
        public const string AssetName = "BundleThumbnail";

        [Tooltip("Encoded thumbnail image bytes (PNG or JPEG).")]
        [SerializeField]
        private byte[] imageData;

        [Tooltip("MIME type of imageData, e.g. image/png or image/jpeg.")]
        [SerializeField]
        private string mimeType = "image/png";

        /// <summary>Encoded thumbnail image bytes (PNG / JPEG). May be null/empty.</summary>
        public byte[] ImageData => imageData;

        /// <summary>MIME type of <see cref="ImageData"/> (e.g. <c>image/png</c>).</summary>
        public string MimeType => string.IsNullOrEmpty(mimeType) ? "image/png" : mimeType;

        /// <summary>True when this holds a usable image.</summary>
        public bool HasImage => imageData != null && imageData.Length > 0;

        /// <summary>Sets the encoded image and its MIME type (used by exporters / authoring tools).</summary>
        public void SetImage(byte[] data, string mime)
        {
            imageData = data;
            mimeType = string.IsNullOrEmpty(mime) ? "image/png" : mime;
        }
    }
}
