// Copyright (c) You-Ri, 2026

namespace Lilium.RemoteControl
{
    /// <summary>
    /// The value of an image-bearing live member: encoded picture bytes plus their MIME type.
    ///
    /// Declare a getter-only <c>[LiveProperty, ImagePreview]</c> member of this type and the
    /// generic object surface serves the picture itself: a direct
    /// <c>GET /live/object/{id}/{path}</c> answers with the raw bytes (correct Content-Type),
    /// while every JSON-shaped read — the whole-object serialization, <c>/live/batch</c> —
    /// folds the member to that address string instead of invoking the getter, so listing an
    /// object never renders its picture. The remote app's ImagePreview control polls whatever
    /// address the JSON value carries, which makes the declaration self-describing.
    ///
    /// The getter runs on the main thread once per picture request and should render, encode
    /// and return without caching — the poll IS the refresh loop. Return <see cref="none"/>
    /// (or any invalid value) when there is no picture; the route answers 404 and clients fall
    /// back to their placeholder.
    ///
    /// Supported on top-level members of a live object only. Inside nested composites the
    /// serializer has no address to fold to, so the value degrades to null there.
    /// </summary>
    public readonly struct LiveImageData
    {
        /// <summary>Encoded picture bytes (PNG, JPEG, ...). Null or empty means "no picture".</summary>
        public readonly byte[] bytes;

        /// <summary>MIME type of <see cref="bytes"/>. Defaults to image/png when empty.</summary>
        public readonly string mimeType;

        public bool isValid => bytes != null && bytes.Length > 0;

        public LiveImageData(byte[] bytes, string mimeType = "image/png")
        {
            this.bytes = bytes;
            this.mimeType = mimeType;
        }

        /// <summary>The "no picture right now" answer; the route turns it into a 404.</summary>
        public static readonly LiveImageData none = default;
    }
}
