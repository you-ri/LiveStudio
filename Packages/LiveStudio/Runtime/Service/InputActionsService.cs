using System.Linq;
using UnityEngine.InputSystem;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    public interface IInputActionProvider
    {
        public InputActionMap inputActionMap { get; }
    }

    public static class InputActionService
    {
        public static InputActionMap[] inputActionMaps => Service<IInputActionProvider>.subjects.Select(s => s.inputActionMap).ToArray();

        public static InputAction FindInputAction(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new System.ArgumentException($"Input Key '{name}' does not defined");

            foreach (var provider in Service<IInputActionProvider>.subjects)
            {
                var action = provider.inputActionMap.FindAction(name);
                if (action != null)
                {
                    return action;
                }
            }

            return null;
        }

    }
}