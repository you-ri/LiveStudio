// Copyright (c) You-Ri, 2026

using System;
using System.IO;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Reads <c>-name value</c> options off the process command line.
    ///
    /// Exists for the few questions that have to be answered before anything else runs -- where
    /// saved files go, which project to open -- and so cannot come from a settings asset or a REST
    /// call. The integration test harness is the caller these were added for: it points a run at a
    /// scratch folder so the run never touches the operator's real projects.
    ///
    /// Unity's own player options stay on the same command line and are read by the engine. An
    /// option this class does not know about is simply not found rather than an error, so engine
    /// and application options can be passed together.
    /// </summary>
    public static class LaunchArgs
    {
        /// <summary>Base directory for every user-saved file (see <see cref="SavedPaths"/>).</summary>
        public const string kSavedBase = "-savedBase";

        /// <summary>
        /// Project folder to open, overriding the persisted one (see <see cref="ProjectManager"/>).
        /// </summary>
        public const string kProject = "-project";

        /// <summary>Suppresses the remote app this build would otherwise launch alongside itself.</summary>
        public const string kNoRemoteApp = "-noRemoteApp";

        /// <summary>
        /// Suppresses the companion application this build would otherwise launch alongside
        /// itself (Fusion). Used when the caller wants to start it with arguments of its own.
        /// </summary>
        public const string kNoCompanionApp = "-noCompanionApp";

        /// <summary>
        /// The value following <paramref name="name"/>, or <c>null</c> when the option is absent, is
        /// last on the line, or is followed by another option. Matching is case-insensitive: scripts
        /// and Windows shortcuts are inconsistent about it and a case slip would fail silently.
        /// </summary>
        public static string Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            var args = _SafeArgs();
            // Stop one short of the end: the last token cannot be an option with a value.
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) continue;

                var value = args[i + 1];
                // A "value" that looks like the next option means this option was passed without
                // one. Accepting it would open a project named "-batchmode" and the run would look
                // like it started correctly, so treat it as absent instead.
                if (string.IsNullOrEmpty(value) || value[0] == '-') return null;
                return value;
            }
            return null;
        }

        /// <summary>Whether a valueless flag such as <see cref="kNoRemoteApp"/> is present.</summary>
        public static bool Has(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;

            var args = _SafeArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>
        /// <see cref="Get"/>'s value as an absolute path, or <c>null</c>. A relative path resolves
        /// against the working directory the process was started in, which is what a caller passing
        /// one means. A path that cannot be resolved is reported as absent rather than thrown: these
        /// are read during startup, where an exception would take the whole launch down.
        /// </summary>
        public static string GetPath(string name)
        {
            var value = Get(name);
            if (string.IsNullOrEmpty(value)) return null;

            try
            {
                return Path.GetFullPath(value);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string[] _SafeArgs()
        {
            try
            {
                return Environment.GetCommandLineArgs() ?? Array.Empty<string>();
            }
            catch (Exception)
            {
                // Sandboxed players can refuse the command line. No options is a valid answer.
                return Array.Empty<string>();
            }
        }
    }
}
