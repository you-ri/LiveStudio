// Copyright (c) You-Ri, 2026

using System;
using UnityEngine;
using Lilium.RemoteControl;
using Lilium.RemoteControl.LiveScene;
using Lilium.RemoteControl.Server;

namespace Lilium.LiveStudio.Social
{
    /// <summary>
    /// Turns streaming-event intake on for a scene. Add it to the scene's
    /// <see cref="RemoteControlBehaviour"/> object list and the HTTP routes appear on the server that
    /// component is already running; remove it and they go away. Presence in the scene is the on switch,
    /// which is how the rest of LiveStudio expresses an optional feature.
    ///
    /// A plain serializable <see cref="ILiveObject"/> like <c>StageManager</c> and
    /// <c>ExternalAssetManager</c>, not a <c>MonoBehaviour</c>. There is one intake per process and it
    /// has no place in the world, so hanging it off a transform would be a lie — and a
    /// <c>[LiveClass]</c> component is only reachable through whichever GameObject happens to own it,
    /// which is a poor home for a token and two counters.
    ///
    /// The server is resolved from the scene's host rather than configured by port number here. A port
    /// field would be a second source of truth for something the scene already knows, and getting it
    /// wrong produces the least diagnosable failure there is: a feeder posting successfully into nothing.
    /// The resolved port is reported back as <see cref="serverPort"/> so it can be copied into the feeder.
    ///
    /// One per scene. The id is fixed, so a second instance would take over the first's registration and
    /// its routes would lose to whichever registered first.
    /// </summary>
    [Serializable]
    [LiveClass("Social", Category = "Social", Icon = "forum")]
    public class SocialGateway : ILiveObject
    {
        private const string kId = "b1f4c6d2-3a07-4e59-9c8b-5d2e7f0a6b31";

        public string name { get; set; } = "Social";

        public LiveObjectHandle? liveObject => LiveObjectRegistry.FindByTarget(this);

        public string id => kId;

        /// <summary>
        /// Shared secret a feeder must send as <c>X-Social-Token</c>. Empty (the default) leaves the
        /// endpoint open, which is safe while the server only listens on loopback. Project-scoped rather
        /// than scene-scoped: it pairs with the feeder installed on this machine, so switching live scenes
        /// must not lose it.
        /// </summary>
        [LiveField(persistScope = PersistScope.Project)]
        [Help("SOCIAL_GATEWAY_AUTHTOKEN")]
        public string authToken = string.Empty;

        /// <summary>Port the intake is listening on, taken from the scene's remote-control server. 0 while
        /// the server has not started yet. This is the number a feeder posts to.</summary>
        [LiveProperty]
        [Help("SOCIAL_GATEWAY_SERVERPORT")]
        public int serverPort => _server != null ? _server.Port : 0;

        /// <summary>Events accepted since the app started.</summary>
        [LiveProperty]
        [Help("SOCIAL_GATEWAY_EVENTSRECEIVED")]
        public int eventsReceived => _AsCount(SocialEventHub.totalReceived);

        /// <summary>Events discarded because they arrived faster than they could be consumed. A number
        /// climbing here means a feeder is flooding, not that events are being ignored quietly.</summary>
        [LiveProperty]
        [Help("SOCIAL_GATEWAY_EVENTSDROPPED")]
        public int eventsDropped => _AsCount(SocialEventHub.totalDropped);

        // The scene's host. Found once and kept: a scene does not swap hosts, and re-searching costs a
        // native scan of every object. Cleared if the host is destroyed (Unity's fake null).
        [NonSerialized] private RemoteControlBehaviour _host;
        [NonSerialized] private RemoteControlServerCore _server;
        [NonSerialized] private SocialEventHandler _handler;

        public void OnEnable()
        {
            LiveObjectRegistry.Create<SocialGateway>(this, kId);
            _TryAttach();
        }

        public void OnDisable()
        {
            _Detach();
            LiveObjectRegistry.FindByTarget(this)?.Unregister();
        }

        // Deleting the object from the remote app calls only this, never OnDisable, so it has to do the
        // full teardown. Both are idempotent, so the paths that call both in sequence stay correct.
        public void OnDispose() => OnDisable();

        public void Update()
        {
            // Nothing here means anything outside play mode: the host only starts its server then, and the
            // hub's frame counter does not advance either. Without this the retry below would rescan the
            // scene on every editor tick, forever, for a server that cannot exist yet.
            if (!Application.isPlaying) return;

            // Re-attach when there is no live route. That covers the ordinary startup race — the host
            // starts its server during its own startup, possibly after this object was enabled — and also
            // a server that was stopped and started again mid-session, which silently drops every route
            // it had.
            if (_handler == null || _server == null || !_server.IsRunning)
            {
                _Detach();
                _TryAttach();
            }
            else
            {
                // The handler stores an empty token rather than null, so compare against the same shape —
                // otherwise a null field here would look different every frame and rewrite forever.
                string token = authToken ?? string.Empty;
                if (_handler.authToken != token) _handler.authToken = token;
            }

            // Advance the hub even when nothing else reads it. The hub publishes lazily, so without this a
            // scene whose only consumer is a SocialEventHub.onEvent subscriber would never see an event —
            // and a scene with no consumer at all would let the queue fill and start counting drops.
            _ = SocialEventHub.currentEvents;
        }

        public void Reset()
        {
            authToken = string.Empty;
        }

        private void _TryAttach()
        {
            if (_handler != null) return;

            // A scene with several hosts would make this ambiguous — it would pick one arbitrarily and the
            // routes could land on a server this object has nothing to do with. Studio runs a single host,
            // so this stays a lookup rather than a search for the host that owns us.
            if (_host == null) _host = _FindHost();

            _server = _host != null ? _host.server : null;
            if (_server == null || !_server.IsRunning) { _server = null; return; }

            _handler = new SocialEventHandler(_server) { authToken = authToken ?? string.Empty };
            _server.RegisterRoute(_handler);
        }

        // The hub counts in long, but the remote app's property wire format has no 64-bit integer — a long
        // serializes as a bare type name with no value, which would show the user nothing at all. These
        // are session counters of chat events, so int is the honest width; the saturation only exists so
        // an absurd number degrades to "very large" instead of wrapping into a negative one.
        private static int _AsCount(long value) => value > int.MaxValue ? int.MaxValue : (int)value;

        private void _Detach()
        {
            // UnregisterRoute calls Cleanup() itself. A null server means it went away before we did, in
            // which case there is no route left to remove.
            if (_handler != null) _server?.UnregisterRoute(_handler);
            _handler = null;
            _server = null;
        }

        private static RemoteControlBehaviour _FindHost()
            => UnityEngine.Object.FindFirstObjectByType<RemoteControlBehaviour>();
    }
}
