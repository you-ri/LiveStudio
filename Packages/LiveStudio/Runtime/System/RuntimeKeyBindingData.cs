using Unity.Collections;
using UnityEngine;

namespace Lilium.LiveStudio
{
    public struct RuntimeKeyBindingData
    {
        public bool isWaitingForKey;
        public int bindingIndexToRebind;
        public FixedString64Bytes actionNameBuffer;

        public void SetActionName(string actionName)
        {
            actionNameBuffer = actionName ?? string.Empty;
        }

        public string GetActionName()
        {
            return actionNameBuffer.ToString();
        }
    }
}
