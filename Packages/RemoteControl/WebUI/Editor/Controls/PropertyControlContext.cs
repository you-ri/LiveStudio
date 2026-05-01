// Copyright (c) You-Ri, 2026

using System;

namespace Lilium.RemoteControl.WebUI.Editor
{
    public struct PropertyControlContext
    {
        public ExposedObject obj;
        public ExposedPropertyType propType;
        public ExposedProperty prop;
        public object currentValue;
        public bool isReadOnly;
        public Func<bool> isUpdatingUI;
    }
}
