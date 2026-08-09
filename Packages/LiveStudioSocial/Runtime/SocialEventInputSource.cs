// Copyright (c) You-Ri, 2026

using System;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio.Social
{
    /// <summary>
    /// Which viewers an event has to come from for a <see cref="SocialEventInputSource"/> to accept it.
    /// Judged from the role flags the feeder set on <see cref="SocialUser"/>; a feeder that reports no
    /// roles therefore only ever satisfies <see cref="Any"/>.
    /// </summary>
    [LiveEnum]
    public enum SocialUserFilter
    {
        /// <summary>Anyone in chat.</summary>
        Any,

        /// <summary>Channel members / subscribers only (the owner and moderators count as members here,
        /// since a stricter reading would exclude the streamer from their own membership perks).</summary>
        MemberOnly,

        /// <summary>Moderators and the channel owner only.</summary>
        ModeratorOnly,
    }

    /// <summary>
    /// Fires an operation set from streaming chat: a comment containing a keyword, a superchat over some
    /// amount, a follow. The declarative counterpart to <see cref="KeyInputSource"/> — instead of a bound
    /// key it matches the events <see cref="SocialEventHub"/> published this frame.
    ///
    /// Conditions are plain fields on purpose. Anything compound — AND/OR across events, counting,
    /// raffles — belongs in the feeder (Streamer.bot and the OneComme plugin are themselves logic
    /// engines) which then posts the verdict as a single <c>custom</c> event for a keyword to catch.
    /// This package does not grow a second rules engine.
    ///
    /// How it behaves is decided by the tile the operation set carries, not by anything here:
    /// <list type="bullet">
    /// <item><description><c>DeckButton</c> — the operation runs once per burst of matching chat.</description></item>
    /// <item><description><c>DeckToggle</c> — each burst flips the set on/off.</description></item>
    /// <item><description><c>DeckSlider</c> — with <see cref="valueFromAmount"/> the tip amount becomes the
    /// slider's position and stays there until the next matching event.</description></item>
    /// </list>
    ///
    /// "Burst" rather than "event" is deliberate. The output is a pulse the frame a match lands on, and
    /// the operation set fires on its edges — so matches on consecutive frames read as one long press and
    /// produce one firing, exactly as holding a key down does. In quiet chat that is one event, one
    /// firing; in a flood it collapses, which is usually what you want from a confetti cannon. Set
    /// <see cref="cooldownSeconds"/> when you want an explicit, predictable rate instead.
    /// </summary>
    [Serializable]
    [LiveClass(Category = "Operation", Icon = "forum")]
    public class SocialEventInputSource : InputSource
    {
        /// <summary>Platform to accept, e.g. <c>youtube</c>. Empty (the default) accepts every platform.
        /// Matched case-insensitively against <see cref="SocialEvent.source"/>.</summary>
        [LiveField]
        [Help("SOCIAL_INPUT_SOURCE")]
        public string source = string.Empty;

        /// <summary>Event kind to accept, e.g. <c>chat</c> or <c>superchat</c>. Empty accepts every kind.
        /// Named <c>eventType</c> rather than <c>type</c> so it cannot be confused with the <c>@type</c>
        /// discriminator that tells the remote app which input source this is.</summary>
        [LiveField]
        [Help("SOCIAL_INPUT_EVENTTYPE")]
        public string eventType = string.Empty;

        /// <summary>Text the message must contain, e.g. <c>!confetti</c>. Empty matches any message.
        /// A case-insensitive substring test — no regular expressions and no command grammar, so a
        /// viewer writing "!confetti pls" still fires it.</summary>
        [LiveField]
        [Help("SOCIAL_INPUT_KEYWORD")]
        public string keyword = string.Empty;

        /// <summary>Which viewers may fire this.</summary>
        [LiveField]
        [Help("SOCIAL_INPUT_USERFILTER")]
        public SocialUserFilter userFilter = SocialUserFilter.Any;

        /// <summary>Smallest <see cref="SocialEvent.amount"/> that counts. 0 (the default) accepts any,
        /// including events that carry no amount at all.</summary>
        [LiveField]
        [Help("SOCIAL_INPUT_MINAMOUNT")]
        public float minAmount;

        /// <summary>Seconds to ignore further matches after one fires. 0 (the default) disables it.
        /// The defence against a viewer spamming the same command.</summary>
        [LiveField]
        [Help("SOCIAL_INPUT_COOLDOWNSECONDS")]
        public float cooldownSeconds;

        /// <summary>
        /// Turns the tip amount into the output value instead of a one-frame pulse, and holds it until the
        /// next matching event. For a slider tile driving something like an effect's intensity.
        ///
        /// The hold is not optional for that use: a pulse would drop back to 0 the next frame and the
        /// operation would immediately write 0 over the value it just wrote. Leave this off for button and
        /// toggle tiles — a held value above the activation threshold looks like a key that is never
        /// released, so the tile would fire once and then stay stuck on.
        /// </summary>
        [LiveField]
        [Help("SOCIAL_INPUT_VALUEFROMAMOUNT")]
        public bool valueFromAmount;

        /// <summary>The amount that maps to a full 1.0 when <see cref="valueFromAmount"/> is on;
        /// <see cref="minAmount"/> maps to 0. Amounts above it clamp. Defaults to 1000, a plausible top
        /// end for a single superchat in yen.</summary>
        [LiveField]
        [Help("SOCIAL_INPUT_AMOUNTRANGE")]
        public float amountRange = 1000f;

        // Held output for the valueFromAmount case, and the time the last accepted event fired at.
        // Runtime state, so it is never serialized: a reload starts from "nothing has fired yet".
        //
        // Setup() is deliberately not overridden to clear these, even though the operation manager calls it
        // on every rebind. A rebind here means the user edited a filter from the remote app, and dropping
        // the latch would blank a slider mid-show just because someone adjusted the keyword.
        [NonSerialized] private float _latchedValue;
        [NonSerialized] private double _lastFireTime = double.NegativeInfinity;

        /// <summary>
        /// 1 on a frame where an event got through the filters, 0 otherwise — or, with
        /// <see cref="valueFromAmount"/>, the held amount. Runs once per frame per operation set and
        /// allocates nothing: the comparisons are ordinal and index-based, never <c>ToLower</c>.
        ///
        /// The first match in a frame wins and the rest are left alone. With two superchats in the same
        /// frame the slider therefore shows the earlier one, not the larger — chat order is the order the
        /// viewers experienced, and picking the biggest would make the smaller tip look ignored.
        ///
        /// The base does not hand the mode down, so this cannot know which tile it is driving. That is
        /// what keeps <see cref="valueFromAmount"/> a user choice rather than something inferred here.
        /// </summary>
        protected override float ReadRawValue()
        {
            var events = SocialEventHub.currentEvents;

            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                if (!_Matches(e)) continue;

                // Cooldown is measured from the accepted event's own timestamp rather than the clock, so
                // every source looking at the same frame agrees on when "now" is.
                if (cooldownSeconds > 0f && e.receivedTime - _lastFireTime < cooldownSeconds) continue;

                _lastFireTime = e.receivedTime;
                if (!valueFromAmount) return 1f;

                // Only the latching mode writes the latch. Writing 1 here as well would mean that turning
                // valueFromAmount on later starts from a stale full-strength value instead of from 0.
                _latchedValue = _NormalizeAmount(e.amount);
                return _latchedValue;
            }

            // Nothing matched: hold the last value for a slider, fall back to 0 for a pulse.
            return valueFromAmount ? _latchedValue : 0f;
        }

        // Ordered cheapest-first so a flood of non-matching chat is rejected on an int compare rather than
        // a scan of the message text.
        private bool _Matches(SocialEvent e)
        {
            if (minAmount > 0f && e.amount < minAmount)
                return false;

            switch (userFilter)
            {
                case SocialUserFilter.MemberOnly:
                    if (!e.user.isMember && !e.user.isModerator && !e.user.isOwner) return false;
                    break;
                case SocialUserFilter.ModeratorOnly:
                    if (!e.user.isModerator && !e.user.isOwner) return false;
                    break;
            }

            if (!string.IsNullOrEmpty(source) && !string.Equals(e.source, source, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrEmpty(eventType) && !string.Equals(e.type, eventType, StringComparison.OrdinalIgnoreCase))
                return false;

            // Last: the only test whose cost grows with the message. IsNullOrEmpty rather than Length so a
            // field left null by hand-written C# degrades to "no filter" instead of throwing every frame
            // inside the operation manager's loop, where it would take the other sets down with it.
            if (!string.IsNullOrEmpty(keyword) && e.message.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            return true;
        }

        // minAmount..amountRange onto 0..1. A range that is not above minAmount would divide by zero or
        // invert, so it degenerates to a full 1 — the user asked for "any amount is maximum".
        private float _NormalizeAmount(float amount)
        {
            float span = amountRange - minAmount;
            if (span <= 0f) return 1f;

            float normalized = (amount - minAmount) / span;
            if (normalized < 0f) return 0f;
            return normalized > 1f ? 1f : normalized;
        }
    }
}
