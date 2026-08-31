// Copyright (c) You-Ri, 2026
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Lilium.RemoteControl.Editor
{
    /// <summary>
    /// The fixed-width face the editor windows put ticking numbers in.
    ///
    /// Shared rather than built per window: the face has to be rebuilt when Unity sweeps it, and
    /// every window that draws numbers has to notice. One owner means one rebuild and one
    /// <see cref="generation"/> for everyone watching it.
    /// </summary>
    internal static class RemoteControlEditorFonts
    {
        private static FontAsset _monoFont;
        private static int _generation;

        /// <summary>
        /// Bumped whenever the face is rebuilt.
        ///
        /// Rows take the face when they are built and never look again, so a rebuilt face has to
        /// reach them somehow. A window folds this into whatever it uses to decide that a list has
        /// to be built again rather than written into.
        /// </summary>
        public static int generation => _generation;

        /// <summary>
        /// Sets the fixed-width face, if one could be built.
        ///
        /// As a font asset, not a plain Font: UI Toolkit draws a legacy Font through its old path,
        /// which renders at the size the font was created with and ignores the one the style asks
        /// for. That is what made this read smaller than everything beside it and sit high in its
        /// line, and no amount of font-size moved it.
        ///
        /// Nothing happens when the machine has none of the candidate faces -- the label keeps the
        /// inherited font, and columns still line up as long as the numbers are padded to a fixed
        /// width and the column is sized for it. An empty font definition must never be assigned:
        /// that is not "inherit", it is "no font", and the label draws nothing at all.
        /// </summary>
        public static void ApplyMonospace(Label label)
        {
            if (label == null) return;

            var font = Monospace();
            if (font == null) return;

            label.style.unityFontDefinition = FontDefinition.FromSDFFont(font);
        }

        /// <summary>The fixed-width face, built on first use and rebuilt after a sweep.</summary>
        public static FontAsset Monospace()
        {
            // Checked rather than cached blindly: a font built this way belongs to nobody, so an
            // asset unload takes it away and leaves a reference that is not null in C# but is dead in
            // Unity. Labels holding it then render blank, which is how this was found.
            // The atlas material has to be alive too, not just the asset: the renderer reads its
            // main texture every time it draws a label, and a swept material throws from inside the
            // paint. Checked here rather than trusted, because that throw is not catchable at the
            // call site -- it happens during the repaint, once per frame, forever.
            if (_monoFont != null && _monoFont.material != null) return _monoFont;

            string[] candidates = { "Consolas", "Courier New", "Menlo", "DejaVu Sans Mono", "monospace" };

            for (int i = 0; i < candidates.Length && _monoFont == null; i++)
            {
                var os = Font.CreateDynamicFontFromOSFont(candidates[i], 16);
                if (os == null) continue;

                os.hideFlags = HideFlags.HideAndDontSave;
                _monoFont = FontAsset.CreateFontAsset(os);
            }

            _generation++;
            _KeepAlive(_monoFont);
            return _monoFont;
        }

        /// <summary>
        /// Marks a font asset and everything it brought with it as not-to-be-swept.
        ///
        /// CreateFontAsset makes three separate objects -- the asset, an atlas material and the
        /// atlas textures -- and an unload takes any of them that nobody owns. Flagging only the
        /// asset leaves the material to be collected, and the label then throws from inside the
        /// repaint rather than anywhere a caller could see.
        /// </summary>
        private static void _KeepAlive(FontAsset font)
        {
            if (font == null) return;

            font.hideFlags = HideFlags.HideAndDontSave;

            if (font.material != null) font.material.hideFlags = HideFlags.HideAndDontSave;

            var textures = font.atlasTextures;
            if (textures == null) return;

            for (int i = 0; i < textures.Length; i++)
            {
                if (textures[i] != null) textures[i].hideFlags = HideFlags.HideAndDontSave;
            }
        }
    }
}
