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
        public static IEnumerator BuildAvatar(AvatarBuildData data)
        {
            string baseUrl = FusionNetwork.BaseURL;
            var body = ExposedTypeInfoSerializer.ToJsonForFunctionArgs(data, DefaultExposedObjectResolver.Instance);


            string requestUrl = baseUrl + "/exposed/function/347f14d4-bca0-48fc-9f41-c6979afdacef/buildavatar";
            UnityWebRequest request = UnityWebRequest.Post(requestUrl, body, "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError($"[Studio] {requestUrl}: {request.error}");
            }
        }

        /// <summary>
        /// Fusion 側 LicenseSettings.EnterLicense を ExposedFunction REST で呼び出す (永続化あり)。
        /// onComplete のレスポンス本文 (<c>{ "result": LicenseSnapshot }</c>) は呼び出し側で parse する。
        /// </summary>
        public static IEnumerator EnterLicense(string key, System.Action<bool, string> onComplete = null)
        {
            yield return _PostLicenseFunction("enterlicense", key, onComplete);
        }

        /// <summary>
        /// Fusion 側 LicenseSettings.EnterSessionLicense を ExposedFunction REST で呼び出す
        /// (永続化なし、プロセス内のみ有効)。Studio 起動時の自動セッションライセンス送信用。
        /// </summary>
        public static IEnumerator EnterSessionLicense(string key, System.Action<bool, string> onComplete = null)
        {
            yield return _PostLicenseFunction("entersessionlicense", key, onComplete);
        }

        /// <summary>
        /// Fusion 側 LicenseSettings.ClearLicense を ExposedFunction REST で呼び出す。
        /// </summary>
        public static IEnumerator ClearLicense(System.Action<bool, string> onComplete = null)
        {
            yield return _PostLicenseFunction("clearlicense", null, onComplete);
        }

        private static IEnumerator _PostLicenseFunction(string functionName, string keyArg, System.Action<bool, string> onComplete)
        {
            string baseUrl = FusionNetwork.BaseURL;
            string requestUrl = baseUrl + "/exposed/function/LicenseSettings/" + functionName;
            string body = keyArg != null
                ? ExposedTypeInfoSerializer.ToJsonForFunctionArgs(keyArg, DefaultExposedObjectResolver.Instance)
                : "{\"args\":[]}";

            UnityWebRequest request = UnityWebRequest.Post(requestUrl, body, "application/json");
            yield return request.SendWebRequest();

            bool ok = request.result == UnityWebRequest.Result.Success;
            string responseBody = ok && request.downloadHandler != null ? request.downloadHandler.text : null;
            if (!ok)
            {
                Debug.LogWarning($"[Studio] License {functionName} failed: {request.error}");
            }
            onComplete?.Invoke(ok, responseBody);
        }
    }
}
