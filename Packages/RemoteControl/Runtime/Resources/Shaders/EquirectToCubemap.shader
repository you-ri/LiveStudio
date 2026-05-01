Shader "Hidden/Lilium/EquirectToCubemap"
{
    Properties
    {
        _MainTex ("Equirectangular Texture", 2D) = "white" {}
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

            sampler2D _MainTex;
            int _Face; // 0-5 for cubemap faces

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // Convert cubemap UV to 3D direction
            float3 CubemapUVToDirection(float2 uv, int face)
            {
                // Convert UV from [0,1] to [-1,1]
                float u = uv.x * 2.0 - 1.0;
                float v = uv.y * 2.0 - 1.0;

                float3 dir;

                // Map to cubemap face
                if (face == 0) // PositiveX
                    dir = float3(1.0, -v, -u);
                else if (face == 1) // NegativeX
                    dir = float3(-1.0, -v, u);
                else if (face == 2) // PositiveY
                    dir = float3(u, 1.0, v);
                else if (face == 3) // NegativeY
                    dir = float3(u, -1.0, -v);
                else if (face == 4) // PositiveZ
                    dir = float3(u, -v, 1.0);
                else // NegativeZ (face == 5)
                    dir = float3(-u, -v, -1.0);

                return normalize(dir);
            }

            // Convert 3D direction to equirectangular UV
            float2 DirectionToEquirectUV(float3 dir)
            {
                float phi = atan2(dir.z, dir.x);
                float theta = asin(dir.y);

                float u = phi / (2.0 * 3.14159265) + 0.5;
                float v = theta / 3.14159265 + 0.5;

                return float2(u, v);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Get direction from cubemap face and UV
                float3 dir = CubemapUVToDirection(i.uv, _Face);

                // Convert direction to equirectangular UV
                float2 equirectUV = DirectionToEquirectUV(dir);

                // Sample the equirectangular texture
                fixed4 col = tex2D(_MainTex, equirectUV);

                return col;
            }
            ENDCG
        }
    }
}
