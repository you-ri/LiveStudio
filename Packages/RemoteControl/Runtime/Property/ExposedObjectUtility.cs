// Copyright (c) You-Ri, 2026
using System.Reflection;
using UnityEngine;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// ExposedObject 関連の共通ユーティリティ。
    /// </summary>
    public static class ExposedObjectUtility
    {
        private static MethodInfo _findObjectFromInstanceIdMethod;

        /// <summary>
        /// UnityEngine.Object.FindObjectFromInstanceID (internal) をリフレクションで呼び出し、
        /// instanceId から生存している UnityEngine.Object を引き当てる。
        /// 見つからない場合は null を返す。
        /// </summary>
        public static UnityEngine.Object InstanceIDToObject(int instanceId)
        {
            if (_findObjectFromInstanceIdMethod == null)
            {
                _findObjectFromInstanceIdMethod = typeof(UnityEngine.Object)
                    .GetMethod("FindObjectFromInstanceID", BindingFlags.NonPublic | BindingFlags.Static);

                if (_findObjectFromInstanceIdMethod == null)
                {
                    Debug.LogError("[RemoteControl] UnityEngine.Object.FindObjectFromInstanceID not found via reflection.");
                    return null;
                }
            }

            return _findObjectFromInstanceIdMethod.Invoke(null, new object[] { instanceId }) as UnityEngine.Object;
        }
    }
}
