// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Where a tile goes on a deck.
    /// <para>
    /// Pure arithmetic over the decks and the tiles on them; it shares nothing with the desk's other
    /// jobs — taking input, evaluating each frame, syncing with files — so it does not live with them.
    /// </para>
    /// <para>
    /// Creating a deck and deciding its name stay on the desk. What is here is only "which cell is free"
    /// and "does this stay inside the deck".
    /// </para>
    /// </summary>
    internal static class DeckLayout
    {
        /// <summary>
        /// Column count used when a deck has none or a broken one.
        /// ⚠ This value also becomes the default <c>columns</c> of a deck file, so it reaches file
        /// contents. Keep it in one place.
        /// </summary>
        internal const int FallbackColumns = 8;

        /// <summary>The logical column count of the deck with the given name.</summary>
        internal static int ColumnsOf(List<Deck> decks, string deckName)
        {
            var deck = decks?.Find(p => p != null && p.name == deckName);
            return deck != null && deck.columns > 0 ? deck.columns : FallbackColumns;
        }

        /// <summary>
        /// True when no other control on the deck overlaps the given grid rectangle.
        /// <paramref name="placing"/> is the tile being placed and never counts against itself.
        /// </summary>
        internal static bool IsAreaFree(
            List<OperationSet> sets, string deckName, DeckControl placing, int x, int y, int w, int h)
        {
            if (sets == null) return true;
            for (int i = 0; i < sets.Count; i++)
            {
                var c = sets[i]?.control;
                if (c == null || c == placing || c.deckName != deckName) continue;
                int cw = Mathf.Max(1, c.w);
                int ch = Mathf.Max(1, c.h);
                if (x < c.x + cw && c.x < x + w && y < c.y + ch && c.y < y + h) return false;
            }
            return true;
        }

        /// <summary>
        /// Finds the first grid cell on the deck where the control's span fits without overlapping
        /// another tile, scanning row by row. Falls back to the top-left when nothing fits.
        /// </summary>
        internal static void FindFreeCell(
            List<Deck> decks, List<OperationSet> sets, string deckName, DeckControl placing,
            out int x, out int y)
        {
            int columns = ColumnsOf(decks, deckName);

            int w = Mathf.Clamp(placing != null ? placing.w : 1, 1, columns);
            int h = Mathf.Max(1, placing != null ? placing.h : 1);

            // The lowest fully-empty row is always free, so bound the scan there.
            int maxRow = 0;
            if (sets != null)
            {
                for (int i = 0; i < sets.Count; i++)
                {
                    var c = sets[i]?.control;
                    if (c != null && c != placing && c.deckName == deckName)
                        maxRow = Mathf.Max(maxRow, c.y + Mathf.Max(1, c.h));
                }
            }

            for (int row = 0; row <= maxRow; row++)
            {
                for (int col = 0; col + w <= columns; col++)
                {
                    if (IsAreaFree(sets, deckName, placing, col, row, w, h)) { x = col; y = row; return; }
                }
            }
            x = 0;
            y = 0;
        }

        /// <summary>
        /// Enforces a control's fixed per-kind width (see <see cref="DeckControl.fixedWidth"/>) and
        /// re-clamps x so the tile stays within the deck's columns. No type switch — each kind declares
        /// its own span.
        /// </summary>
        internal static void ApplyControlWidth(List<Deck> decks, DeckControl control)
        {
            if (control == null) return;
            control.w = control.fixedWidth;
            int columns = ColumnsOf(decks, control.deckName);
            if (control.x + control.w > columns) control.x = Mathf.Max(0, columns - control.w);
        }
    }
}
