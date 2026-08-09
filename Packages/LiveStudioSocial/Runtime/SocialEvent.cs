// Copyright (c) You-Ri, 2026

using System;

namespace Lilium.LiveStudio.Social
{
    /// <summary>
    /// Well-known <see cref="SocialEvent.source"/> values. The vocabulary is open: a feeder may send any
    /// string and it round-trips untouched, so a platform this package has never heard of still works.
    /// These constants exist so first-party code and tests stop misspelling the common ones.
    /// </summary>
    public static class SocialEventSources
    {
        public const string YouTube = "youtube";
        public const string Twitch = "twitch";
        public const string Niconico = "niconico";
        public const string TwitCasting = "twitcasting";
        public const string Kick = "kick";
        public const string Bilibili = "bilibili";
        public const string Showroom = "showroom";
        public const string StreamerBot = "streamerbot";

        /// <summary>Reserved for manual / automated smoke tests so real traffic can be told apart.</summary>
        public const string Test = "test";
    }

    /// <summary>
    /// Well-known <see cref="SocialEvent.type"/> values. Open vocabulary, same as
    /// <see cref="SocialEventSources"/>.
    /// </summary>
    public static class SocialEventTypes
    {
        public const string Chat = "chat";
        public const string SuperChat = "superchat";
        public const string Sticker = "sticker";
        public const string Membership = "membership";
        public const string Gift = "gift";
        public const string Follow = "follow";
        public const string Subscribe = "subscribe";
        public const string Cheer = "cheer";
        public const string Raid = "raid";

        /// <summary>
        /// A result decided upstream. Composite logic (AND/OR, counting, raffles) is deliberately not built
        /// into this package; a feeder such as Streamer.bot evaluates it and posts the verdict as a custom
        /// event whose <see cref="SocialEvent.message"/> names the outcome.
        /// </summary>
        public const string Custom = "custom";
    }

    /// <summary>
    /// The viewer a <see cref="SocialEvent"/> came from (or is about, for a gift/raid). Every field is
    /// optional on the wire; the role flags default to false, which is also what an unknown feeder implies.
    /// </summary>
    [Serializable]
    public class SocialUser
    {
        /// <summary>Platform-scoped user id. Not unique across platforms.</summary>
        public string id;

        /// <summary>Display name as the platform renders it. Untrusted plain text.</summary>
        public string name;

        public bool isModerator;
        public bool isMember;
        public bool isOwner;
    }

    /// <summary>
    /// One normalized viewer event from a streaming platform. This type is the public contract between
    /// LiveStudio and every external feeder, so its shape may only ever grow: fields are added, never
    /// removed or repurposed, and unknown incoming fields are ignored. See Documentation~ for the wire
    /// form of the same schema.
    ///
    /// A plain class rather than the unmanaged struct + system pairing used elsewhere in LiveStudio: an
    /// event carries string payloads and arrives at most a few dozen times a second, so it fails the
    /// "bulk-processed numeric data" test that design applies to. GC pressure is held down by the bounded
    /// queue in <see cref="SocialEventHub"/> instead.
    /// </summary>
    [Serializable]
    public class SocialEvent
    {
        /// <summary>Upper bound on <see cref="message"/>; longer text is truncated on intake. Chat lines are
        /// far shorter than this everywhere, so the limit only bites on malformed or hostile input.</summary>
        public const int kMaxMessageLength = 1000;

        /// <summary>Platform identifier, e.g. <c>youtube</c>. See <see cref="SocialEventSources"/>. Required.</summary>
        public string source;

        /// <summary>Event kind, e.g. <c>superchat</c>. See <see cref="SocialEventTypes"/>. Required.</summary>
        public string type;

        /// <summary>Feeder-assigned unique id. Reserved for de-duplication when several feeders overlap;
        /// nothing reads it yet.</summary>
        public string id;

        /// <summary>Never null once the event has passed through <see cref="SocialEventHub.Enqueue"/>.</summary>
        public SocialUser user;

        /// <summary>Chat body or, for a custom event, the outcome name. Untrusted plain text: no markup or
        /// command syntax is interpreted here.</summary>
        public string message;

        /// <summary>Money, bits or gift count, in the unit named by <see cref="currency"/>. 0 when absent.
        /// Deliberately not converted between currencies — that is the consumer's call. Single precision
        /// represents integers exactly only up to 2^24 (~16.7 million), which covers every realistic tip
        /// even in low-denomination currencies; feeders sending larger raw values should scale them.</summary>
        public float amount;

        /// <summary>Unit of <see cref="amount"/>, e.g. <c>JPY</c>. Empty when the event carries no amount.</summary>
        public string currency;

        /// <summary>
        /// <c>Time.unscaledTimeAsDouble</c> at which the event became visible to consumers. Stamped by
        /// <see cref="SocialEventHub"/> on the main thread at the frame swap, not at enqueue time: feeders
        /// run on worker threads, where Unity's clock cannot be read. Resolution is therefore one frame,
        /// which is also the resolution consumers observe events at.
        /// </summary>
        public double receivedTime;
    }
}
