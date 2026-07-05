using System.Linq;
using System.Collections.Generic;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    public static class CameraService
    {
        public static IEnumerable<IExposedCamera> cameras => Service<IExposedCamera>.subjects.AsEnumerable();

        public static IExposedCamera GetCamera(string displayName)
        {
            return Service<IExposedCamera>.subjects.FirstOrDefault(x => x.displayName == displayName);
        }

        public static IExposedCamera GetCamera(System.Guid id)
        {
            return Service<IExposedCamera>.subjects.FirstOrDefault(x => x.guid == id);
        }

        public static void SwitchCamera(string displayName)
        {
            Service<IExposedCamera>.subjects.ForEach(x =>
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
            Service<IExposedCamera>.subjects.ForEach(x =>
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
