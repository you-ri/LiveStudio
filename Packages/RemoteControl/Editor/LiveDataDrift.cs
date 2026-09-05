// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using System.Text;
using Lilium.RemoteControl.Frames;

namespace Lilium.RemoteControl.Editor
{
    /// <summary>
    /// Says, in one line, how a recording's idea of a type differs from this build's.
    ///
    /// Both live-data windows carry the same banner, so they say it the same way. What makes it
    /// worth saying at all is that the difference is otherwise invisible: a member the take predates
    /// is left as the world already has it, which looks exactly like a member nobody is writing.
    /// </summary>
    public static class LiveDataDrift
    {
        /// <summary>
        /// One short phrase per type: its short name, then what is only here and what was only in
        /// the recording.
        ///
        /// The type's last name segment rather than its full one -- these go into a banner, and
        /// three namespaced names fill it before any of them says anything.
        /// </summary>
        public static List<string> Describe(IReadOnlyDictionary<string, StateReadPlan> drift)
        {
            var lines = new List<string>(drift?.Count ?? 0);
            if (drift == null) return lines;

            var sb = new StringBuilder();

            foreach (var pair in drift)
            {
                var plan = pair.Value;
                if (plan == null) continue;

                sb.Clear();
                sb.Append(_ShortName(pair.Key));

                // "+" for a member only this build has: the recording says nothing about it, so it
                // keeps whatever the world holds. "-" for one only the recording has, which has
                // nowhere to land.
                _Append(sb, '+', plan.unwrittenMembers);
                _Append(sb, '-', plan.droppedMembers);

                lines.Add(sb.ToString());
            }

            return lines;
        }

        private static void _Append(StringBuilder sb, char sign, string[] members)
        {
            if (members == null || members.Length == 0) return;

            for (int i = 0; i < members.Length; i++)
            {
                sb.Append(i == 0 ? " " : ", ").Append(sign).Append(members[i]);
            }
        }

        private static string _ShortName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return string.Empty;

            var cut = typeName.LastIndexOf('.');
            return cut < 0 || cut == typeName.Length - 1 ? typeName : typeName.Substring(cut + 1);
        }
    }
}
