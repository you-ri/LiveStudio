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
    /// <see cref="OperationSet.control"/> has <see cref="DeckControl.deckId"/> equal to this deck's
    /// <see cref="id"/> (so an operation and its placement are one object — see <see cref="DeckControl"/>).
    /// Persisted in its own deck file (<see cref="DeckFileStore"/>), not in the scene.
    /// </summary>
    [Serializable]
    [LiveClass(Category = "Operation", Icon = "grid_view")]
    public class Deck
    {
        /// <summary>The deck's identity: the stem of its deck file, decided when the deck is created and never
        /// changed (renaming does not move the file). Placed controls point at it
        /// (<see cref="DeckControl.deckId"/>), and it is the element key, so a rename never breaks a reference.
        /// </summary>
        [LiveField(lane = FrameLane.State, textCapacity = 128), LiveKey]
        public string id = "Deck";

        /// <summary>The deck's display name, stored inside its deck file. Free text: no character is replaced
        /// and two decks may share a name, since nothing refers to a deck by it. Changed through
        /// <see cref="OperationManager.RenameDeck"/> so the file is rewritten.</summary>
        [LiveField(lane = FrameLane.State, textCapacity = 128)]
        public string name = "Deck";

        /// <summary>Logical column count of the grid. The remote app keeps this fixed and lets the physical
        /// cell width follow the viewport; tile spans are expressed in these columns.</summary>
        [LiveField]
        public int columns = 8;
    }
}
