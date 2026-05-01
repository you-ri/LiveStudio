Shader "Hidden/Lilium/CubemapToEquirect"
{
    Properties
    {
        _Tex ("Cubemap", CUBE) = "" {}
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            samplerCUBE _Tex;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // Convert equirectangular UV to 3D direction for cubemap sampling
            float3 EquirectUVToDirection(float2 uv)
            {
                // UV範囲: [0,1] x [0,1]
                // 経度 phi: [-π, π]
                // 緯度 theta: [-π/2, π/2]

                float phi = (uv.x - 0.5) * 2.0 * 3.14159265; // 経度
                float theta = (0.5 - uv.y) * 3.14159265;      // 緯度

                float3 dir;
                dir.x = cos(theta) * cos(phi);
                dir.y = sin(theta);
                dir.z = cos(theta) * sin(phi);

                return normalize(dir);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Equirectangular UV から 3D方向ベクトルを取得
                float3 dir = EquirectUVToDirection(i.uv);

                // Cubemapからサンプリング
                fixed4 col = texCUBE(_Tex, dir);

                return col;
            }
            ENDCG
        }
    }
}
