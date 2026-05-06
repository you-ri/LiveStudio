using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// 個別のアクションバインディング保存データ
    /// </summary>
    [System.Serializable]
    public class InputBindingSaveData
    {
        public string actionName;
        public string actionType;
        public List<string> bindingPaths;

        public InputBindingSaveData()
        {
            bindingPaths = new List<string>();
            actionType = "Value"; // デフォルト値
        }

        public InputBindingSaveData(string actionName)
        {
            this.actionName = actionName;
            this.bindingPaths = new List<string>();
            this.actionType = "Value"; // デフォルト値
        }
    }

    /// <summary>
    /// AvatarInputの全バインド情報保存データ
    /// </summary>
    [System.Serializable]
    public class AvatarInputSettings
    {
        public string deviceName;
        public InputBindingSaveData[] bindings;

        public AvatarInputSettings()
        {
            deviceName = "";
            bindings = new InputBindingSaveData[0];
        }
    }
}