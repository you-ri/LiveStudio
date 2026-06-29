// Copyright (c) You-Ri, 2026

using System;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// The deck representation of an <see cref="ActionSet"/>: embedded 1:1 as <see cref="ActionSet.control"/>,
    /// so an action and its deck control are the same object (adding/removing the action adds/removes the
    /// control — no separate tile list to keep in sync). The concrete kind (<see cref="DeckButton"/> /
    /// <see cref="DeckToggle"/> / <see cref="DeckSlider"/>) decides which of the manager's operate
    /// functions the tile drives (momentary hold / toggle / value) — independent of the action set's input
    /// <see cref="InputMode"/> (the physical key binding). The control is pure data; the actual firing runs
    /// through <see cref="ActionManager"/>'s existing remote functions.
    ///
    /// <see cref="deckName"/> says which <see cref="Deck"/> it is placed on (matched by the deck's unique
    /// name; empty/unknown = treated as unplaced and re-placed on the default page). The remote
    /// app draws a deck's tiles as the action sets whose control points at that deck. Polymorphic via
    /// <c>[SerializeReference]</c> + the RemoteControl <c>@type</c> discriminator, exactly like
    /// <see cref="ActionBase"/>. <c>[ExposedClass]</c> on the abstract base lets the owning field's
    /// derived-type enumeration surface the concrete kinds; the base itself is never instantiated.
    /// </summary>
    [Serializable]
    [ExposedClass]
    public abstract class DeckControl
    {
        /// <summary>Name of the <see cref="Deck"/> this control is placed on (deck names are kept unique),
        /// or empty when unplaced. An empty or unknown name is normalized to the default page on load.</summary>
        [ExposedField]
        public string deckName = string.Empty;

        /// <summary>Grid column of the tile's top-left cell (0-based). Driven by the deck's drag layout,
        /// not hand-edited, so it is hidden from the generic editor.</summary>
        [ExposedField, Hide]
        public int x;

        /// <summary>Grid row of the tile's top-left cell (0-based). Hidden from the generic editor.</summary>
        [ExposedField, Hide]
        public int y;

        /// <summary>Column span (cells). Hidden from the generic editor.</summary>
        [ExposedField, Hide]
        public int w = 1;

        /// <summary>Row span (cells). Hidden from the generic editor.</summary>
        [ExposedField, Hide]
        public int h = 1;

        /// <summary>The fixed column span this tile kind occupies. <see cref="ActionManager"/> enforces
        /// <see cref="w"/> to this value, so a new tile size is declared here (override per kind) rather than
        /// switched on by the manager. Defaults to 1.</summary>
        public virtual int fixedWidth => 1;

        /// <summary>The single behaviour axis of the action set: the control kind decides both how its deck
        /// tile operates and how its bound <see cref="InputSource"/> interprets the raw input (momentary /
        /// latching / continuous). Declared per kind here so the tile type is the one source of truth — there
        /// is no separate input mode to keep in sync. Defaults to <see cref="InputMode.Button"/>.</summary>
        public virtual InputMode mode => InputMode.Button;
    }

    /// <summary>Momentary tile: held on while pressed, released on pointer-up
    /// (<see cref="ActionManager.SetActionSetHeld"/>). Drives its input in <see cref="InputMode.Button"/>.</summary>
    [Serializable]
    [ExposedClass(Category = "Action", Icon = "bolt")]
    public class DeckButton : DeckControl
    {
    }

    /// <summary>Latching tile: each tap flips the action set on/off
    /// (<see cref="ActionManager.ToggleActionSet"/>). Drives its input in <see cref="InputMode.Toggle"/>.</summary>
    [Serializable]
    [ExposedClass(Category = "Action", Icon = "check_box")]
    public class DeckToggle : DeckControl
    {
        /// <summary>Each tap latches on/off.</summary>
        public override InputMode mode => InputMode.Toggle;
    }

    /// <summary>Continuous tile: drag sets a 0..1 manual value
    /// (<see cref="ActionManager.SetActionSetValue"/>). Drives its input in <see cref="InputMode.Value"/>.</summary>
    [Serializable]
    [ExposedClass(Category = "Action", Icon = "sliders")]
    public class DeckSlider : DeckControl
    {
        /// <summary>Sliders span 2 cells so the gauge stays legible.</summary>
        public override int fixedWidth => 2;

        /// <summary>Drag sets a continuous 0..1 value.</summary>
        public override InputMode mode => InputMode.Value;
    }
}
