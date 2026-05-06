// Copyright (c) You-Ri, 2026

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    [ExposedClass(Icon = "assignment")]
    public class ControlExpressionInfo
    {
        [ExposedField]
        public string name;

        [ExposedField]
        public string[] bindings;
    }
}
