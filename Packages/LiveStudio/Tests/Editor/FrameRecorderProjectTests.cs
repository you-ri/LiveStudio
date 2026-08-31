// Copyright (c) You-Ri, 2026

using System.IO;
using NUnit.Framework;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio.EditorTests
{
    /// <summary>
    /// Where takes are written. A take replays by rebuilding the world it was recorded against, so it
    /// only means anything in the project it came from -- it belongs to that project the same way
    /// scenes, decks and avatars do, and must not be shared across all of them.
    /// </summary>
    public class FrameRecorderProjectTests
    {
        private const string kOpen = "/projects/open";
        private const string kPersisted = "/projects/persisted";
        private const string kFallback = "/projects/default";

        [Test]
        public void TheOpenProjectWins()
        {
            Assert.AreEqual(
                Path.Combine(kOpen, FrameRecorderController.kFolderName),
                FrameRecorderProject.ResolveRecordingFolder(kOpen, kPersisted, kFallback));
        }

        [Test]
        public void WithNothingPlaying_ThePersistedProjectIsUsed()
        {
            // The page is served in the editor too, where no runtime callback has filled the open
            // project in. The picker still has to list the right project's takes.
            Assert.AreEqual(
                Path.Combine(kPersisted, FrameRecorderController.kFolderName),
                FrameRecorderProject.ResolveRecordingFolder("", kPersisted, kFallback));
        }

        [Test]
        public void OnAFirstLaunch_TheOpenProjectIsUsedEvenThoughNothingIsPersisted()
        {
            // A first launch opens the default project folder without persisting it. Reading only the
            // persisted value would drop takes somewhere else than everything else in that project.
            Assert.AreEqual(
                Path.Combine(kOpen, FrameRecorderController.kFolderName),
                FrameRecorderProject.ResolveRecordingFolder(kOpen, "", kFallback));
        }

        [Test]
        public void WithNoProjectKnownYet_TheFallbackIsUsed()
        {
            // The caller passes the project that would be opened next, so a take never lands outside
            // a project at all.
            Assert.AreEqual(
                Path.Combine(kFallback, FrameRecorderController.kFolderName),
                FrameRecorderProject.ResolveRecordingFolder("", "", kFallback));
        }
    }
}
