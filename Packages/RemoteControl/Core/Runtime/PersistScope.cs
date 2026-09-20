// Copyright (c) You-Ri, 2026
namespace Lilium.RemoteControl
{
    /// <summary>
    /// Where a persisted member is written.
    ///
    /// <see cref="Scene"/> goes into the live scene file (<c>*.scene.json</c>, and a snapshot of it);
    /// <see cref="Project"/> into the project-wide settings file
    /// (<c>{projectPath}/Settings/{ClassName}.settings.json</c>); <see cref="Custom"/> into a file the
    /// owner writes itself. The default is <see cref="Scene"/> (= 0).
    ///
    /// The scope also decides whether the frame carries the member. The live scene holds the state
    /// of the show, and the frame is that state one frame at a time, so a member the scene saves is
    /// one a recording carries -- and a member saved anywhere else is a setting of this machine,
    /// which no take should put back. See <see cref="FrameLaneRules"/>.
    /// </summary>
    public enum PersistScope
    {
        Scene = 0,
        Project = 1,

        /// <summary>
        /// The owner persists these members into its own file, so neither the live scene nor the
        /// project settings write them. Serialization is still available -- the owner asks for this
        /// scope explicitly (see <c>LiveObjectSnapshot.Capture(handle, scope)</c>) -- but the built-in
        /// writers (scene / project settings) and the dirty comparison, which both filter on
        /// <see cref="Scene"/> or <see cref="Project"/>, skip them. Such an owner is therefore
        /// responsible for its own saving, and its members are invisible to
        /// <c>LiveSceneSaveSystem.HasUnsavedChanges</c>. That is why every owner so far writes
        /// continuously -- an edit lands in the file moments later -- rather than offering a save:
        /// nothing would tell the operator that something was still waiting to be saved.
        /// Deserialization ignores the scope, so a legacy file that still holds these members inline
        /// keeps restoring them.
        /// </summary>
        Custom = 2,
    }
}
