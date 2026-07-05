using UnityEngine;
using Lilium.RemoteControl;


namespace Lilium.LiveStudio
{
    public interface IActor
    {
        public bool isActive { get; }

        public string name { get; }

        public Texture2D image { get; }

        public float actorHeight { get; }

        public void SetActorHeight(float height);

        public void StartCalibration();

        public void StopCalibration();
    }


    public interface IActorService
    {
        public IActor[] activeActors { get; }

    }


    public static class ActorService
    {
        public static IActor[] activeActors => SingletonService<IActorService>.subject?.activeActors ?? new IActor[0];

        public static void StartCalibration()
        {
            Service<IActor>.subjects.ForEach(listener => listener.StartCalibration());
        }

        public static void StopCalibration()
        {
            Service<IActor>.subjects.ForEach(listener => listener.StopCalibration());
        }
    }

}
