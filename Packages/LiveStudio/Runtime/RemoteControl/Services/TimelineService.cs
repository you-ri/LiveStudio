using System.Linq;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{

    public interface ILiveTimeline
    {
        public System.Guid guid { get; }

        public string name { get; }

        public double currentTime { get; }

        public double duration { get; }

        public bool isPlaying { get; }

        void Play();

        void Stop();

        void SetTime(double time);
    }


    public static class TimelineService
    {
        public static ILiveTimeline GetTimeline(System.Guid id)
        {
            return Service<ILiveTimeline>.subjects.FirstOrDefault(t => t.guid == id);
        }

        public static ILiveTimeline GetTimeline(string name)
        {
            return Service<ILiveTimeline>.subjects.FirstOrDefault(t => t.name == name);
        }

        public static void PlayTimeline(System.Guid id)
        {
            var timeline = GetTimeline(id);
            timeline?.Play();
        }

        public static void PlayTimeline(string name)
        {
            var timeline = Service<ILiveTimeline>.subjects.FirstOrDefault(t => t.name == name);
            timeline?.Play();
        }

        public static void StopTimeline(System.Guid id)
        {
            var timeline = GetTimeline(id);
            timeline?.Stop();
        }

        public static void StopTimeline(string name)
        {
            var timeline = GetTimeline(name);
            timeline?.Stop();
        }

        public static void SetTimelineCurrentTime(System.Guid id, double time)
        {
            var timeline = GetTimeline(id);
            timeline?.SetTime(time);
        }

        public static void SetTimelineCurrentTime(string name, double time)
        {
            var timeline = GetTimeline(name);
            timeline?.SetTime(time);
        }

        public static ILiveTimeline[] GetTimelines()
        {
            return Service<ILiveTimeline>.subjects.ToArray();
        }

        public static double? GetTimelineCurrentTime(System.Guid id)
        {
            var timeline = GetTimeline(id);
            return timeline?.currentTime;
        }

        public static double? GetTimelineCurrentTime(string name)
        {
            var timeline = GetTimeline(name);
            return timeline?.currentTime;
        }

        public static double? GetTimelineDuration(System.Guid id)
        {
            var timeline = GetTimeline(id);
            return timeline?.duration;
        }

        public static double? GetTimelineDuration(string name)
        {
            var timeline = GetTimeline(name);
            return timeline?.duration;
        }

        public static bool? GetTimelineIsPlaying(System.Guid id)
        {
            var timeline = GetTimeline(id);
            return timeline?.isPlaying;
        }

        public static bool? GetTimelineIsPlaying(string name)
        {
            var timeline = GetTimeline(name);
            return timeline?.isPlaying;
        }
    }
}