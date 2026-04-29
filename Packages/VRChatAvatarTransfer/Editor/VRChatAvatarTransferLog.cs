// Copyright (c) You-Ri, 2026
using UnityEngine;

namespace Lilium.VRChatAvatarTransfer.Editor
{
    internal static class VRChatAvatarTransferLog
    {
        private const string Prefix = "[VRChatTransfer]";

        public static void Info(string message) => Debug.Log($"{Prefix} {message}");
        public static void Warn(string message) => Debug.LogWarning($"{Prefix} {message}");
        public static void Error(string message) => Debug.LogError($"{Prefix} {message}");
    }
}
