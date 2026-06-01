using UnityEngine;
using System;

namespace Lilium.RemoteControl
{
    public static class RemoteControlService
    {
        public static event Action onResetData;

        public static void ResetData()
        {
            onResetData?.Invoke();
        }
    }
}
