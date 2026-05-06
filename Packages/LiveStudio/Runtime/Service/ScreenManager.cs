using Lilium.RemoteControl;
using UnityEngine;

#if false
namespace Lilium.LiveStudio
{
    [ExposedClass("Screen", Icon = "monitor")]
    public static class ScreenManager
    {
        [ExposedProperty]
        public static int width
        {
            get => Screen.width;
            set => Screen.SetResolution(value, Screen.height, isFullScreen);
        }

        [ExposedProperty]
        public static int height
        {
            get => Screen.height;
            set => Screen.SetResolution(Screen.width, value, isFullScreen);
        }

        [ExposedProperty]
        public static bool isFullScreen
        {
            get => Screen.fullScreen;
            set => Screen.fullScreen = value;
        }



        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void Initialize()
        {
            var exposedType = ExposedClass.Get(typeof(ScreenManager));
            exposedType.onPropertyChanged += (property, oldValue) =>
            {
                SetResolution(width, height, isFullScreen);
            };
            width = Screen.width;
            height = Screen.height;
            isFullScreen = Screen.fullScreen;
        }

    }

}

#endif
