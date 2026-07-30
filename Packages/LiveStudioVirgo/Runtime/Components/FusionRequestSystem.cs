// Copyright (c) You-Ri, 2026

using System.Collections;

using UnityEngine;
using UnityEngine.Networking;

using Lilium.RemoteControl;


using Lilium.LiveStudio;
namespace Lilium.LiveStudio.Virgo
{
    public static class FusionRequestSystem
    {
        // Body for a LiveFunction that takes no arguments.
        const string kNoArgsBody = "{\"args\":[]}";

        // Sends the built avatar skeleton to Fusion. The result (and the error text on failure) is
        // reported through onComplete so the caller owns the logging and retry policy; Fusion may be
        // offline at startup, so a failure here is not necessarily an error.
        public static IEnumerator BuildAvatar(AvatarBuildData data, System.Action<bool, string> onComplete = null)
        {
            string baseUrl = FusionNetwork.BaseURL;
            var body = LiveTypeInfoSerializer.ToJsonForFunctionArgs(data, DefaultLiveObjectResolver.Instance);

            string requestUrl = baseUrl + "/live/function/347f14d4-bca0-48fc-9f41-c6979afdacef/buildavatar";
            using (UnityWebRequest request = UnityWebRequest.Post(requestUrl, body, "application/json"))
            {
                yield return request.SendWebRequest();

                bool ok = request.result == UnityWebRequest.Result.Success;
                onComplete?.Invoke(ok, ok ? null : $"{requestUrl}: {request.error}");
            }
        }

        /// <summary>
        /// Asks Fusion to re-lock its own capture timing (the frame-offset baseline of every capture
        /// channel), so a single Studio-side resync covers both stages of the pipeline. Fusion being
        /// offline is a normal case, so the caller owns the logging and retry policy.
        /// </summary>
        public static IEnumerator ResyncTiming(System.Action<bool, string> onComplete = null)
        {
            // Addressed by Fusion's exposed names, not by a type reference (Fusion is a separate
            // process and package). Renaming them on the Fusion side silently turns this into a 404,
            // so the Fusion function carries a note pointing back here.
            yield return _PostFunction("FusionPage", "resynctiming", kNoArgsBody, onComplete);
        }

        /// <summary>
        /// Fusion 側 LicenseSettings.EnterLicense を LiveFunction REST で呼び出す (永続化あり)。
        /// onComplete のレスポンス本文 (<c>{ "result": LicenseSnapshot }</c>) は呼び出し側で parse する。
        /// </summary>
        public static IEnumerator EnterLicense(string key, System.Action<bool, string> onComplete = null)
        {
            yield return _PostLicenseFunction("enterlicense", key, onComplete);
        }

        /// <summary>
        /// Fusion 側 LicenseSettings.EnterSessionLicense を LiveFunction REST で呼び出す
        /// (永続化なし、プロセス内のみ有効)。Studio 起動時の自動セッションライセンス送信用。
        /// </summary>
        public static IEnumerator EnterSessionLicense(string key, System.Action<bool, string> onComplete = null)
        {
            yield return _PostLicenseFunction("entersessionlicense", key, onComplete);
        }

        /// <summary>
        /// Fusion 側 LicenseSettings.ClearLicense を LiveFunction REST で呼び出す。
        /// </summary>
        public static IEnumerator ClearLicense(System.Action<bool, string> onComplete = null)
        {
            yield return _PostLicenseFunction("clearlicense", null, onComplete);
        }

        private static IEnumerator _PostLicenseFunction(string functionName, string keyArg, System.Action<bool, string> onComplete)
        {
            string body = keyArg != null
                ? LiveTypeInfoSerializer.ToJsonForFunctionArgs(keyArg, DefaultLiveObjectResolver.Instance)
                : kNoArgsBody;

            bool ok = false;
            string payload = null;
            yield return _PostFunction("LicenseSettings", functionName, body, (success, text) =>
            {
                ok = success;
                payload = text;
            });

            if (!ok)
            {
                Debug.LogWarning($"[Studio] License {functionName} failed: {payload}");
            }
            // Callers only parse the body on success, so a failure keeps handing them null.
            onComplete?.Invoke(ok, ok ? payload : null);
        }

        /// <summary>
        /// Posts to one of Fusion's LiveFunctions. onComplete receives the response body on success
        /// and an error text carrying the request URL on failure; logging and retries are the
        /// caller's business (Fusion may legitimately be offline).
        /// </summary>
        private static IEnumerator _PostFunction(string objectId, string functionName, string body,
            System.Action<bool, string> onComplete)
        {
            string requestUrl = $"{FusionNetwork.BaseURL}/live/function/{objectId}/{functionName}";
            using (UnityWebRequest request = UnityWebRequest.Post(requestUrl, body, "application/json"))
            {
                yield return request.SendWebRequest();

                bool ok = request.result == UnityWebRequest.Result.Success;
                string payload = ok ? request.downloadHandler?.text : $"{requestUrl}: {request.error}";
                onComplete?.Invoke(ok, payload);
            }
        }
    }
}
