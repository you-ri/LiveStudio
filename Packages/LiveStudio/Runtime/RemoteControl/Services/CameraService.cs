using System.Linq;
using System.Collections.Generic;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    public static class CameraService
    {
        public static IEnumerable<ILiveCamera> cameras => Service<ILiveCamera>.subjects.AsEnumerable();

        public static ILiveCamera GetCamera(string displayName)
        {
            return Service<ILiveCamera>.subjects.FirstOrDefault(x => x.displayName == displayName);
        }

        public static ILiveCamera GetCamera(System.Guid id)
        {
            return Service<ILiveCamera>.subjects.FirstOrDefault(x => x.guid == id);
        }

        public static void SwitchCamera(string displayName)
        {
            Service<ILiveCamera>.ForEach(x =>
            {
                if (x.displayName == displayName)
                {
                    x.SetPriority(1);
                }
                else
                {
                    x.SetPriority(0);
                }
            });
        }

        public static void SwitchCamera(System.Guid id)
        {
            Service<ILiveCamera>.ForEach(x =>
            {
                if (x.guid == id)
                {
                    x.SetPriority(1);
                }
                else
                {
                    x.SetPriority(0);
                }
            });
        }
    }
}
