using System;
using System.Reflection;

namespace Lilium.RemoteControl.Reflection
{
    /// <summary>
    /// Single migration point for enumerating loaded assemblies.
    ///
    /// AppDomain.CurrentDomain.GetAssemblies() is deprecated under the CoreCLR 2026
    /// (Unity 6.8) runtime because it may return assemblies pending unload and block
    /// domain reload. When moving to Unity 6.8 / CoreCLR, replace the body below with
    /// UnityEngine.Assemblies.CurrentAssemblies.GetLoadedAssemblies()
    /// (optionally behind a CoreCLR-only preprocessor branch). No other file needs to change.
    /// </summary>
    public static class AssemblyUtility
    {
        public static Assembly[] GetLoadedAssemblies()
            => AppDomain.CurrentDomain.GetAssemblies();
    }
}
