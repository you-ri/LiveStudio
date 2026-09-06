// Copyright (c) You-Ri, 2026
namespace Lilium.RemoteControl
{
    /// <summary>
    /// How a member's lane follows from how it is persisted.
    ///
    /// The rule is one sentence: <b>what the live scene saves, the frame carries; what it does not
    /// save, the frame leaves alone.</b> A recording is the scene changing one frame at a time, so
    /// the two answer the same question, and answering it twice -- once as <c>persistScope</c>, once
    /// as <c>lane</c> -- had the two drift apart: a project setting recorded because nobody wrote
    /// <c>lane = None</c> beside it, a scene value declared off the frame and so lost from every take
    /// that changed it. Deriving the lane from the persistence leaves one place to say it.
    ///
    /// There is one exception, and it is said out loud: a member the scene does not save but a take
    /// must carry -- a pose, an expression weight, which avatar is out. Such a member declares its
    /// lane (<see cref="FrameLane.State"/> or <see cref="FrameLane.Event"/>) explicitly, and the
    /// declaration wins over the persistence. The reverse -- a saved member declared
    /// <see cref="FrameLane.None"/> -- is not an exception but an error, because it would leave the
    /// scene file describing a world the recording disagrees with (<c>LRC013</c>).
    ///
    /// The generator (<c>StateBlockEmitter._TryReadStateMember</c>) applies the same rule to decide
    /// what goes into a state block. The two must agree, or the runtime refuses a record for a value
    /// a block is copying every frame -- the failure this area keeps having.
    /// </summary>
    public static class FrameLaneRules
    {
        /// <summary>
        /// The lane a member is on.
        /// </summary>
        /// <param name="declared">The lane the member asked for, or null when it said nothing.</param>
        /// <param name="isPersistable">Whether any writer saves the member at all.</param>
        /// <param name="persistScope">Where it is saved when it is.</param>
        /// <param name="typeIsOffFrame">Whether the owner type declared itself off the frame
        /// (<c>[LiveClass(lane = FrameLane.None)]</c>), which absorbs everything below it.</param>
        public static FrameLane Resolve(FrameLane? declared, bool isPersistable, PersistScope persistScope,
            bool typeIsOffFrame)
        {
            if (typeIsOffFrame) return FrameLane.None;

            // Said out loud: the exception (carried but not saved) or the opt-in to the dense lane.
            // A declared None is refused upstream (LRC013 / a registration error) and treated as
            // unsaid here, so it cannot take a saved member off the frame.
            if (declared.HasValue && declared.Value != FrameLane.None) return declared.Value;

            return IsSavedToScene(isPersistable, persistScope) ? FrameLane.Event : FrameLane.None;
        }

        /// <summary>Whether the live scene file is where this member goes.</summary>
        public static bool IsSavedToScene(bool isPersistable, PersistScope persistScope)
            => isPersistable && persistScope == PersistScope.Scene;
    }
}
