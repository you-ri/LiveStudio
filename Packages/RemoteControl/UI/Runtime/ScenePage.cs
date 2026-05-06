// Copyright (c) You-Ri, 2026

using System;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Lilium.RemoteControl.UI
{
    /// <summary>
    /// UI page definition.
    /// Corresponds to the RemoteApp ScenePage.
    /// Displays all objects in the ExposedObjectContainer grouped by category.
    /// Has no selector; the RemoteApp uses fetchAll to retrieve every object.
    /// </summary>
    [Serializable]
    [ExposedClass]
    [MovedFrom(true, "Lilium.RemoteControl.WebUI", "Lilium.RemoteControl.WebUI")]
    public class ScenePage : IPage
    {
        public bool experimental = true;

        [SerializeReference, Select]
        public IObjectFactory factory = new StandardObjectFactory();
    }
}
