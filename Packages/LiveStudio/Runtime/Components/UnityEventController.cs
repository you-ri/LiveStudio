using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using System.Linq;
using System.IO;
using UnityEngine.Events;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// UnityEventを使ってイベント処理を管理するコンポーネント
    /// </summary>
    public class UnityEventController : MonoBehaviour, IEventController
    {
        [System.Serializable]
        public class UnityEventElement : IEventData
        {
            public UnityEventElement(string name, string group, UnityEvent trigger)
            {
                this._name = name;
                this._group = group;
                this.trigger = trigger;
            }

            public string name => _name;

            public string group => _group;

            public string _name;

            public string _group;

            public UnityEvent trigger;
        }

        void OnEnable()
        {
            Service<IEventController>.Register(this);
        }

        void OnDisable()
        {
            Service<IEventController>.Unregister(this);
        }

        [SerializeField]
        public UnityEventElement[] _events;

        // implement IEventController
        public IEventData[] events => _events;

        public void RaiseEvent(string name)
        {
            _events.FirstOrDefault(e => e.name == name)?.trigger.Invoke();
        }
    }

}