Shader "Evolution/TerrainSunShade"
{
    // Meme proprietes que TerrainOverlay — peut utiliser le meme material
    Properties
    {
        _MainTex ("Map Data", 2D) = "white" {}
        _MapSize ("Map Size", Vector) = (1280, 720, 0, 0)
        _SandThreshold   ("Sand Threshold",   Float) = 0.05
        _GrassThreshold  ("Grass Threshold",  Float) = 0.40
        _ForestThreshold ("Forest Threshold", Float) = 0.70
        _RockThreshold   ("Rock Threshold",   Float) = 0.95
        _WaterThreshold  ("Water Threshold",  Float) = 0.35
        _SlopeScaleWater  ("Slope Water",  Float) = 20
        _SlopeScaleSand   ("Slope Sand",   Float) = 20
        _SlopeScaleGrass  ("Slope Grass",  Float) = 20
        _SlopeScaleForest ("Slope Forest", Float) = 20
        _SlopeScaleRock   ("Slope Rock",   Float) = 20
        _SlopeScaleSnow   ("Slope Snow",   Float) = 20
        _SunShadowStrength ("Sun Shadow Strength", Float) = 0.5
        _GlobalSunPosition ("Global Sun Position", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        ZWrite Off Cull Off

        Pass
        {
            Name "TerrainSunShade"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4    _MainTex_TexelSize;
            float4    _MainTex_ST;
            float4    _MapSize;

            float _WaterThreshold;
            float _SandThreshold;
            float _GrassThreshold;
            float _ForestThreshold;
            float _RockThreshold;

            float _SlopeScaleWater;
            float _SlopeScaleSand;
            float _SlopeScaleGrass;
            float _SlopeScaleForest;
            float _SlopeScaleRock;
            float _SlopeScaleSnow;

            float _SunShadowStrength;
            float _GlobalSunPosition;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f    { float4 pos : SV_POSITION;  float2 uv : TEXCOORD0; };

            int getMapType(float2 uv, float dx, float dy) {
                float2 s = uv + float2(dx, dy) * _MainTex_TexelSize.xy;
                return round(tex2D(_MainTex, s).r * 255.0);
            }

            float GetHeight(float2 uv) {
                float2 pixel = uv * _MapSize.xy - 0.5;
                float2 f = frac(pixel);
                float2 i = floor(pixel);
                float2 uv00 = (i + 0.5) * _MainTex_TexelSize.xy;
                float2 uv10 = uv00 + float2(_MainTex_TexelSize.x, 0);
                float2 uv01 = uv00 + float2(0, _MainTex_TexelSize.y);
                float2 uv11 = uv00 + _MainTex_TexelSize.xy;
                float g0 = lerp(tex2D(_MainTex, uv00).g, tex2D(_MainTex, uv10).g, f.x);
                float g1 = lerp(tex2D(_MainTex, uv01).g, tex2D(_MainTex, uv11).g, f.x);
                return lerp(g0, g1, f.y);
            }

            v2f vert(appdata v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Biome : snappé au tile (discret)
                float2 mapPixel  = floor(i.uv * _MapSize.xy);
                float2 snappedUV = (mapPixel + 0.5) * _MainTex_TexelSize.xy;
                int c = getMapType(snappedUV, 0, 0);

                // Calcul sunShade identique à TerrainOverlay : GetHeight lissé sur uvX/uvY * 2
                float2 uvX = float2(_MainTex_TexelSize.x * 2.0, 0);
                float2 uvY = float2(0, _MainTex_TexelSize.y * 2.0);
                float hL = GetHeight(i.uv - uvX);
                float hR = GetHeight(i.uv + uvX);
                float hD = GetHeight(i.uv - uvY);
                float hU = GetHeight(i.uv + uvY);

                float slopeScale = _SlopeScaleGrass;
                if      (c == 0) slopeScale = _SlopeScaleWater;
                else if (c == 1) slopeScale = _SlopeScaleSand;
                else if (c == 2) slopeScale = _SlopeScaleGrass;
                else if (c == 3) slopeScale = _SlopeScaleForest;
                else if (c == 4) slopeScale = _SlopeScaleRock;
                else if (c == 5) slopeScale = _SlopeScaleSnow;

                float3 normal   = normalize(float3(-(hR - hL) * slopeScale, -(hU - hD) * slopeScale, 1.0));
                float3 lightDir = normalize(float3(_GlobalSunPosition, 0.0, 0.5));
                float NdotL     = saturate(dot(normal, lightDir));
                float flatLight = saturate(lightDir.z);
                float sunShade  = min(1.0, NdotL / max(flatLight, 0.001));

                // R = biome (0-5 encodé /255), G = sunShade continu
                return fixed4(c / 255.0, sunShade, 0, 1);
            }
            ENDCG
        }
    }
}
