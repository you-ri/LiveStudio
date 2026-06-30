// Copyright (c) You-Ri, 2026

using System;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// One control deck in <see cref="OperationManager.decks"/>: a named grid that a streaming operator
    /// arranges their operation sets onto as a touch console. Several decks can hold different layouts (the
    /// remote app switches between them).
    ///
    /// The deck holds no tile list of its own: its tiles are the operation sets whose
    /// <see cref="OperationSet.control"/> has <see cref="DeckControl.deckName"/> equal to this deck's
    /// <see cref="name"/> (so an operation and its placement are one object — see <see cref="DeckControl"/>).
    /// A plain serializable type, persisted with the manager in the scene.
    /// </summary>
    [Serializable]
    [ExposedClass(Category = "Operation", Icon = "grid_view")]
    public class Deck
    {
        /// <summary>The deck's display name, also its identity: <see cref="OperationManager"/> keeps it unique
        /// (auto-suffixing on collision) so placed controls can reference it by name
        /// (<see cref="DeckControl.deckName"/>). Renaming goes through <see cref="OperationManager.RenameDeck"/>
        /// so referencing controls follow.</summary>
        [ExposedField]
        public string name = "Deck";

        /// <summary>Logical column count of the grid. The remote app keeps this fixed and lets the physical
        /// cell width follow the viewport; tile spans are expressed in these columns.</summary>
        [ExposedField]
        public int columns = 8;
    }
}
