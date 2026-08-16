// Copyright (c) You-Ri, 2026

using System;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// The deck representation of an <see cref="OperationSet"/>: embedded 1:1 as <see cref="OperationSet.control"/>,
    /// so an operation and its deck control are the same object (adding/removing the operation adds/removes the
    /// control — no separate tile list to keep in sync). The concrete kind (<see cref="DeckButton"/> /
    /// <see cref="DeckToggle"/> / <see cref="DeckSlider"/>) decides which of the manager's operate
    /// functions the tile drives (momentary hold / toggle / value) — independent of the operation set's input
    /// <see cref="InputMode"/> (the physical key binding). The control is pure data; the actual firing runs
    /// through <see cref="OperationManager"/>'s existing remote functions.
    ///
    /// <see cref="deckName"/> says which <see cref="Deck"/> it is placed on (matched by the deck's unique
    /// name; empty/unknown = treated as unplaced and re-placed on the default page). The remote
    /// app draws a deck's tiles as the operation sets whose control points at that deck. Polymorphic via
    /// <c>[SerializeReference]</c> + the RemoteControl <c>@type</c> discriminator, exactly like
    /// <see cref="OperationBase"/>. <c>[LiveClass]</c> on the abstract base lets the owning field's
    /// derived-type enumeration surface the concrete kinds; the base itself is never instantiated.
    /// </summary>
    [Serializable]
    [LiveClass]
    public abstract class DeckControl
    {
        /// <summary>Name of the <see cref="Deck"/> this control is placed on (deck names are kept unique),
        /// or empty when unplaced. An empty or unknown name is normalized to the default page on load.</summary>
        [LiveField]
        public string deckName = string.Empty;

        /// <summary>Grid column of the tile's top-left cell (0-based). Driven by the deck's drag layout,
        /// not hand-edited, so it is hidden from the generic editor.</summary>
        [LiveField, Hide]
        public int x;

        /// <summary>Grid row of the tile's top-left cell (0-based). Hidden from the generic editor.</summary>
        [LiveField, Hide]
        public int y;

        /// <summary>Column span (cells). Hidden from the generic editor.</summary>
        [LiveField, Hide]
        public int w = 1;

        /// <summary>Row span (cells). Hidden from the generic editor.</summary>
        [LiveField, Hide]
        public int h = 1;

        /// <summary>Optional asset id whose preview thumbnail (served by <c>/live/asset/{key}/@image</c>) the
        /// remote app draws as this tile's full-frame background. Empty = no background (the tile keeps its
        /// plain glass face). Set by the remote app's "add avatar switch tile" affordance to the switched
        /// avatar's id so the deck tile shows that avatar. Hidden from the generic editor (an asset id is not
        /// hand-edited) but still sent over the wire and persisted, like <see cref="OperationSet.id"/>.</summary>
        [LiveField, Hide]
        public string backgroundAssetId = string.Empty;

        /// <summary>The fixed column span this tile kind occupies. <see cref="OperationManager"/> enforces
        /// <see cref="w"/> to this value, so a new tile size is declared here (override per kind) rather than
        /// switched on by the manager. Defaults to 1.</summary>
        public virtual int fixedWidth => 1;

        /// <summary>The single behaviour axis of the operation set: the control kind decides both how its deck
        /// tile operates and how its bound <see cref="InputSource"/> interprets the raw input (momentary /
        /// latching / continuous). Declared per kind here so the tile type is the one source of truth — there
        /// is no separate input mode to keep in sync. Defaults to <see cref="InputMode.Button"/>.</summary>
        public virtual InputMode mode => InputMode.Button;
    }

    /// <summary>Momentary tile: held on while pressed, released on pointer-up
    /// (<see cref="OperationManager.SetOperationSetHeld"/>). Drives its input in <see cref="InputMode.Button"/>.</summary>
    [Serializable]
    [LiveClass(Category = "Operation", Icon = "bolt")]
    public class DeckButton : DeckControl
    {
    }

    /// <summary>Latching tile: each tap flips the operation set on/off
    /// (<see cref="OperationManager.ToggleOperationSet"/>). Drives its input in <see cref="InputMode.Toggle"/>.</summary>
    [Serializable]
    [LiveClass(Category = "Operation", Icon = "check_box")]
    public class DeckToggle : DeckControl
    {
        /// <summary>Each tap latches on/off.</summary>
        public override InputMode mode => InputMode.Toggle;
    }

    /// <summary>Continuous tile: drag sets a 0..1 manual value
    /// (<see cref="OperationManager.SetOperationSetValue"/>). Drives its input in <see cref="InputMode.Value"/>.</summary>
    [Serializable]
    [LiveClass(Category = "Operation", Icon = "sliders")]
    public class DeckSlider : DeckControl
    {
        /// <summary>Sliders span 2 cells so the gauge stays legible.</summary>
        public override int fixedWidth => 2;

        /// <summary>Drag sets a continuous 0..1 value.</summary>
        public override InputMode mode => InputMode.Value;

        /// <summary>Low end of the value range the drag maps onto. Inherited from the source slider control's
        /// min when the tile is created from the remote app's "bind to key" affordance. Defaults to 0, which
        /// (with <see cref="max"/> = 1) is the identity range, so the raw 0..1 value is written unchanged.</summary>
        [LiveField]
        public float min = 0f;

        /// <summary>High end of the value range. Inherited from the source slider control's max. Defaults to 1.
        /// The normalized 0..1 drag is written to the target property as <c>min + (max - min) * value</c>
        /// (see <see cref="OperationContext.MappedValue"/>).</summary>
        [LiveField]
        public float max = 1f;
    }
}
