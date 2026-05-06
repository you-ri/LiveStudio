using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{

    public interface ISceneData
    {
        public string name { get; }

        public string group { get; }
    }


    public interface ISceneController
    {
        public ISceneData[] scenes { get; }

        void ChangeScene(string name);


    }

    public class SceneController : MonoBehaviour, ISceneController
    {
        [System.Serializable]
        public class UnityEventElement : ISceneData
        {
#if UNITY_EDITOR
            public UnityEventElement(SceneAsset scene, string group)
            {
                this.scene = scene;
                this._group = group;
            }
#endif
            public string name => "";

            public string group => _group;

            public string _group;
#if UNITY_EDITOR
            public SceneAsset scene;
#endif
        }

        void OnEnable()
        {
            Service<ISceneController>.Register(this);
        }

        void OnDisable()
        {
            Service<ISceneController>.Unregister(this);
        }

        [SerializeField]
        public UnityEventElement[] _scenes;

        // implement ISceneControllerStatus
        public ISceneData[] scenes => _scenes;

        // implement ITimelineController
        public void ChangeScene(string name)
        {
            //_events.FirstOrDefault(e => e.name == name && e.scene != null)?.scene.Play();
        }

    }


}