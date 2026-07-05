using UnityEngine.InputSystem;

namespace Lilium.LiveStudio
{
    public interface IInputActionProvider
    {
        public InputActionMap inputActionMap { get; }
    }
}
