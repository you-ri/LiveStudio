// Copyright (c) You-Ri, 2026
using System.Collections.Generic;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>
    /// Writes the inventory -- what exists, of what type, under whom -- into each live frame.
    ///
    /// The state lane says what the values are; this says what they belong to. Without it a
    /// recording holds values addressed to objects it never mentions, and a replay has nothing to
    /// tell it whether the world it is writing into is the world that was recorded.
    ///
    /// This is the capture half only. Applying an inventory is not assignment but a reconcile
    /// against reality -- create what is missing, destroy what is not in it -- and that half is
    /// what makes scrubbing past a spawn work. It is not here yet; see the design note.
    /// </summary>
    public static class LiveStructureSystem
    {
        private static int _users;
        private static int _objectCount;

        // Ids seen this frame, so what is no longer registered can be taken out. A set rather than
        // a walk of the block per object: the inventory is small but the walk is quadratic, and
        // this runs at every frame head.
        private static readonly HashSet<int> _present = new HashSet<int>();

        // What this system put in the inventory. Only these are ever taken out again: the block is
        // shared, and another producer may have entries of its own in it -- a spawned prop, a test
        // standing something up. Removing those because the registry does not know them would be
        // this system quietly deciding it is the only one allowed to say what exists.
        private static readonly HashSet<int> _owned = new HashSet<int>();

        /// <summary>Objects in the inventory as of the most recent live frame.</summary>
        public static int objectCount => _objectCount;

        /// <summary>True while the per-frame capture is running.</summary>
        public static bool isRunning => _users > 0;

        /// <summary>
        /// Asks for the inventory to be written at each frame head. Counted, and balanced by
        /// <see cref="Release"/> -- see <see cref="LiveStateSystem.Retain"/> for why.
        /// </summary>
        public static void Retain()
        {
            if (_users++ > 0) return;

            FrameGate.AddFrameHeadHandler(_OnFrameHead);
        }

        /// <summary>Gives it up. Stops once nobody wants it.</summary>
        public static void Release()
        {
            if (_users == 0 || --_users > 0) return;

            FrameGate.RemoveFrameHeadHandler(_OnFrameHead);
        }

        private static void _OnFrameHead(ref Frame frame)
        {
            // A supplied frame brought its own inventory. Writing ours over it would replace the
            // world being replayed with the world that happens to be loaded.
            if (frame.isSupplied)
            {
                _objectCount = frame.structure?.count ?? 0;
                return;
            }

            _objectCount = CaptureInto(frame.structure, FrameGate.symbols);
        }

        /// <summary>
        /// Reconciles the block against the registry. Exposed separately from the frame head so a
        /// caller can take an inventory without waiting for one.
        ///
        /// Returns how many objects are in it afterwards.
        /// </summary>
        public static int CaptureInto(StructureBlock structure, InputSymbolTable symbols)
        {
            if (structure == null || symbols == null) return 0;

            _present.Clear();

            foreach (var handle in LiveObjectRegistry.instances)
            {
                // No id means nothing can address it, so it cannot be in an inventory that exists to
                // be addressed. Invalid means the object behind it is gone.
                if (!handle.hasId || !handle.isValid) continue;

                var id = symbols.Intern(handle.id);
                if (id == InputSymbolTable.kNone) continue;

                _present.Add(id);

                structure.AddOrUpdate(id, symbols.Intern(handle.targetTypeName),
                    _ParentId(handle, symbols));
            }

            _RemoveMissing(structure);

            _owned.Clear();
            foreach (var id in _present) _owned.Add(id);

            return structure.count;
        }

        /// <summary>
        /// Takes out the entries this system put there that the registry no longer has. Walked back
        /// to front because removing shifts everything after it down, and a forward walk would step
        /// over the entry that moved up.
        /// </summary>
        private static void _RemoveMissing(StructureBlock structure)
        {
            if (_owned.Count == 0) return;

            for (int i = structure.count - 1; i >= 0; i--)
            {
                var id = structure[i].id;
                if (_present.Contains(id) || !_owned.Contains(id)) continue;

                structure.Remove(id);
            }
        }

        private static int _ParentId(LiveObjectHandle handle, InputSymbolTable symbols)
        {
            var parent = handle.target is LiveUnityObjectBase proxy ? proxy.parentId : null;
            return string.IsNullOrEmpty(parent) ? InputSymbolTable.kNone : symbols.Intern(parent);
        }
    }
}
