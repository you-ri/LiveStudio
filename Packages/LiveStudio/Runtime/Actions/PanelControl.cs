// Copyright (c) You-Ri, 2026

using System;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// The panel representation of an <see cref="ActionSet"/>: embedded 1:1 as <see cref="ActionSet.control"/>,
    /// so an action and its panel control are the same object (adding/removing the action adds/removes the
    /// control — no separate tile list to keep in sync). The concrete kind (<see cref="PanelPush"/> /
    /// <see cref="PanelCheckbox"/> / <see cref="PanelSlider"/>) decides which of the manager's operate
    /// functions the tile drives (momentary hold / toggle / value) — independent of the action set's input
    /// <see cref="InputMode"/> (the physical key binding). The control is pure data; the actual firing runs
    /// through <see cref="ActionManager"/>'s existing remote functions.
    ///
    /// <see cref="panelName"/> says which <see cref="Panel"/> it is placed on (matched by the panel's unique
    /// name; empty/unknown = treated as unplaced and re-placed on the default page). The remote
    /// app draws a panel's tiles as the action sets whose control points at that panel. Polymorphic via
    /// <c>[SerializeReference]</c> + the RemoteControl <c>@type</c> discriminator, exactly like
    /// <see cref="ActionBase"/>. <c>[ExposedClass]</c> on the abstract base lets the owning field's
    /// derived-type enumeration surface the concrete kinds; the base itself is never instantiated.
    /// </summary>
    [Serializable]
    [ExposedClass]
    public abstract class PanelControl
    {
        /// <summary>Name of the <see cref="Panel"/> this control is placed on (panel names are kept unique),
        /// or empty when unplaced. An empty or unknown name is normalized to the default page on load.</summary>
        [ExposedField]
        public string panelName = string.Empty;

        /// <summary>Grid column of the tile's top-left cell (0-based). Driven by the panel's drag layout,
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
    }

    /// <summary>Momentary tile: held on while pressed, released on pointer-up
    /// (<see cref="ActionManager.SetActionSetHeld"/>).</summary>
    [Serializable]
    [ExposedClass(Category = "Action", Icon = "bolt")]
    public class PanelPush : PanelControl
    {
    }

    /// <summary>Latching tile: each tap flips the action set on/off
    /// (<see cref="ActionManager.ToggleActionSet"/>).</summary>
    [Serializable]
    [ExposedClass(Category = "Action", Icon = "check_box")]
    public class PanelCheckbox : PanelControl
    {
    }

    /// <summary>Continuous tile: drag sets a 0..1 manual value
    /// (<see cref="ActionManager.SetActionSetValue"/>).</summary>
    [Serializable]
    [ExposedClass(Category = "Action", Icon = "sliders")]
    public class PanelSlider : PanelControl
    {
        /// <summary>Sliders span 2 cells so the gauge stays legible.</summary>
        public override int fixedWidth => 2;
    }
}
