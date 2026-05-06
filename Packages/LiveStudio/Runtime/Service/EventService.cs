using System.Linq;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    public interface IEventController
    {
        IEventData[] events { get; }

        void RaiseEvent(string name);
    }

    public interface IEventData
    {
        public string name { get; }

        public string group { get; }
    }

    public static class EventService
    {
        public static IEventData[] events => Service<IEventController>.subjects.SelectMany(l => l.events).ToArray();

        public static void RaiseEvent(string name)
        {
            Service<IEventController>.subjects.ForEach(l => l.RaiseEvent(name));
        }
    }
}