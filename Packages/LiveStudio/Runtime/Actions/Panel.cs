// Copyright (c) You-Ri, 2026

using System;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// One control panel in <see cref="ActionManager.panels"/>: a named grid that a streaming operator
    /// arranges their action sets onto as a touch console. Several panels can hold different layouts (the
    /// remote app switches between them).
    ///
    /// The panel holds no tile list of its own: its tiles are the action sets whose
    /// <see cref="ActionSet.control"/> has <see cref="PanelControl.panelName"/> equal to this panel's
    /// <see cref="name"/> (so an action and its placement are one object — see <see cref="PanelControl"/>).
    /// A plain serializable type, persisted with the manager in the scene.
    /// </summary>
    [Serializable]
    [ExposedClass(Category = "Action", Icon = "grid_view")]
    public class Panel
    {
        /// <summary>The panel's display name, also its identity: <see cref="ActionManager"/> keeps it unique
        /// (auto-suffixing on collision) so placed controls can reference it by name
        /// (<see cref="PanelControl.panelName"/>). Renaming goes through <see cref="ActionManager.RenamePanel"/>
        /// so referencing controls follow.</summary>
        [ExposedField]
        public string name = "Panel";

        /// <summary>Logical column count of the grid. The remote app keeps this fixed and lets the physical
        /// cell width follow the viewport; tile spans are expressed in these columns.</summary>
        [ExposedField]
        public int columns = 8;
    }
}
