// Copyright (c) You-Ri, 2026

using System;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// One control deck in <see cref="ActionManager.decks"/>: a named grid that a streaming operator
    /// arranges their action sets onto as a touch console. Several decks can hold different layouts (the
    /// remote app switches between them).
    ///
    /// The deck holds no tile list of its own: its tiles are the action sets whose
    /// <see cref="ActionSet.control"/> has <see cref="DeckControl.deckName"/> equal to this deck's
    /// <see cref="name"/> (so an action and its placement are one object — see <see cref="DeckControl"/>).
    /// A plain serializable type, persisted with the manager in the scene.
    /// </summary>
    [Serializable]
    [ExposedClass(Category = "Action", Icon = "grid_view")]
    public class Deck
    {
        /// <summary>The deck's display name, also its identity: <see cref="ActionManager"/> keeps it unique
        /// (auto-suffixing on collision) so placed controls can reference it by name
        /// (<see cref="DeckControl.deckName"/>). Renaming goes through <see cref="ActionManager.RenameDeck"/>
        /// so referencing controls follow.</summary>
        [ExposedField]
        public string name = "Deck";

        /// <summary>Logical column count of the grid. The remote app keeps this fixed and lets the physical
        /// cell width follow the viewport; tile spans are expressed in these columns.</summary>
        [ExposedField]
        public int columns = 8;
    }
}
