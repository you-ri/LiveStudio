using UnityEngine;
using UnityEngine.InputSystem;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Lilium.LiveStudio
{
    public static class InputActionMapUtils
    {
        public static void RefreshInputActionMap(InputActionMap inputActionMap)
        {
            if (inputActionMap == null) return;
            
            bool wasEnabled = inputActionMap.enabled;
            if (wasEnabled)
            {
                inputActionMap.Disable();
                inputActionMap.Enable();
            }
            else
            {
                inputActionMap.Enable();
            }
        }
        
        public static void MarkInputActionMapDirty(InputActionMap inputActionMap)
        {
#if UNITY_EDITOR
            if (inputActionMap?.asset != null)
            {
                EditorUtility.SetDirty(inputActionMap.asset);
            }
#endif
        }
        
        public static void RefreshAndMarkDirty(InputActionMap inputActionMap)
        {
            RefreshInputActionMap(inputActionMap);
            MarkInputActionMapDirty(inputActionMap);
        }
        
        public static InputAction SafeCreateAction(InputActionMap inputActionMap, string actionName, string controlLayout = null, UnityEngine.InputSystem.InputActionType actionType = UnityEngine.InputSystem.InputActionType.Value)
        {
            if (inputActionMap == null)
            {
                Debug.LogError("[LiveStudio] InputActionMap is null in SafeCreateAction");
                return null;
            }
            
            var existingAction = inputActionMap.FindAction(actionName);
            if (existingAction != null)
            {
                Debug.LogWarning($"[LiveStudio] Action '{actionName}' already exists");
                return existingAction;
            }

            // InputActionMapの状態を保存して一時的に無効化
            bool wasEnabled = inputActionMap.enabled;
            if (wasEnabled)
            {
                inputActionMap.Disable();
            }

            try
            {
                var newAction = inputActionMap.AddAction(actionName, type: actionType, expectedControlLayout: controlLayout);

                // 元の状態に復元
                if (wasEnabled)
                {
                    inputActionMap.Enable();
                }

                RefreshAndMarkDirty(inputActionMap);
                return newAction;
            }
            catch (System.Exception e)
            {
                // エラー時も元の状態に復元
                if (wasEnabled)
                {
                    inputActionMap.Enable();
                }

                Debug.LogError($"[LiveStudio] Exception while creating action '{actionName}': {e.Message}");
                return null;
            }
        }
        
        public static bool SafeRemoveAction(InputActionMap inputActionMap, string actionName)
        {
            if (inputActionMap == null)
            {
                Debug.LogError("[LiveStudio] InputActionMap is null in SafeRemoveAction");
                return false;
            }
            
            var action = inputActionMap.FindAction(actionName);
            if (action == null)
            {
                Debug.LogWarning($"[LiveStudio] Action '{actionName}' not found for removal");
                return false;
            }
            
            try
            {
                action.RemoveAction();
                RefreshAndMarkDirty(inputActionMap);
                
                Debug.Log($"[LiveStudio] Successfully removed action: {actionName}");
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[LiveStudio] Exception while removing action '{actionName}': {e.Message}");
                return false;
            }
        }
    }
}
