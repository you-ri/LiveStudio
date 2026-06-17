// Copyright (c) You-Ri, 2026
#if VRMC_VRM10
using System;
using UnityEngine;
using UniGLTF;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Installs a UniGLTF log handler that drops the benign spring-bone warning
    /// "&lt;bone&gt; does not exist on load." UniVRM logs this for every spring-bone joint whose bone
    /// is missing from <see cref="UniVRM10.Vrm10Instance.DefaultTransformStates"/>. For avatars
    /// instantiated from a prefab / AssetBundle (e.g. <c>.avatar.lsb</c>) that dictionary is built
    /// from <c>GetComponentsInChildren&lt;Transform&gt;()</c> (active transforms only), so spring
    /// joints on inactive bones trigger the warning even though they are harmless — inactive bones
    /// do not animate. All other UniGLTF / UniVRM logs pass through unchanged.
    /// </summary>
    static class UniGltfLogFilter
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            UniGLTFLogger.SetLogger(new FilteringLogHandler());
        }

        sealed class FilteringLogHandler : ILogHandler
        {
            const string SuppressedSuffix = "does not exist on load.";

            public void LogException(Exception exception, UnityEngine.Object context)
            {
                Debug.LogException(exception, context);
            }

            public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
            {
                if (logType == LogType.Warning && format != null
                    && format.EndsWith(SuppressedSuffix, StringComparison.Ordinal))
                {
                    return;
                }

                // Mirror UniGLTFLogger's DefaultUnityLogger for everything else.
                var message = (args != null && args.Length > 0) ? string.Format(format, args) : format;
                switch (logType)
                {
                    case LogType.Error:
                    case LogType.Exception:
                        Debug.LogError(message, context);
                        break;
                    case LogType.Assert:
                        Debug.LogAssertion(message, context);
                        break;
                    case LogType.Warning:
                        Debug.LogWarning(message, context);
                        break;
                    default:
                        Debug.Log(message, context);
                        break;
                }
            }
        }
    }
}
#endif
