// Copyright (c) You-Ri, 2026

using System;

namespace Lilium.RemoteControl.UI.Editor
{
    public struct PropertyControlContext
    {
        public LiveObjectHandle obj;
        public LivePropertyType propType;
        public LiveProperty prop;
        public object currentValue;
        public bool isReadOnly;
        public Func<bool> isUpdatingUI;
    }
}
