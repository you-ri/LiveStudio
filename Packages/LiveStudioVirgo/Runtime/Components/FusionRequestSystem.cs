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
            var body = ExposedPropertySerializer.ToJsonForFunctionArgs(data, DefaultExposedObjectResolver.Instance);


            string requestUrl = baseUrl + "/exposed/function/347f14d4-bca0-48fc-9f41-c6979afdacef/buildavatar";
            UnityWebRequest request = UnityWebRequest.Post(requestUrl, body, "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError($"[Studio] {requestUrl}: {request.error}");
            }
        }


    }
}
