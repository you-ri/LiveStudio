// Copyright (c) You-Ri, 2026
Shader "Skybox/ImageBackground"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "black" {}
        _ScaleOffset ("Scale Offset", Vector) = (1, 1, 0, 0)
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 screenPos : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _ScaleOffset;

            v2f vert(float4 vertex : POSITION)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(vertex);
                o.screenPos = ComputeScreenPos(o.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.screenPos.xy / i.screenPos.w;
                uv = uv * _ScaleOffset.xy + _ScaleOffset.zw;
                float mask = step(0, uv.x) * step(uv.x, 1) * step(0, uv.y) * step(uv.y, 1);
                fixed4 col = tex2D(_MainTex, uv);
                return col * mask;
            }
            ENDCG
        }
    }
}
