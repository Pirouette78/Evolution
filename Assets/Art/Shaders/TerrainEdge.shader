Shader "Evolution/TerrainEdge"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "TerrainEdge"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float  _EdgeThreshold;
            float  _EdgeBlend;
            float4 _EdgeColor;
            int    _BiomeMask; // bit0=water bit1=sand bit2=grass bit3=forest bit4=rock bit5=snow

            TEXTURE2D(_SunShadeTex);
            SAMPLER(sampler_SunShadeTex);
            float4 _SunShadeTex_TexelSize;

            // Sample sunShade (canal G) depuis la RT terrain
            float SunShade(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_SunShadeTex, sampler_SunShadeTex, uv).g;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv  = i.texcoord;
                half4  src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                // Biome depuis canal R de la sunShadeRT (encodé c/255)
                float biomeRaw = SAMPLE_TEXTURE2D(_SunShadeTex, sampler_SunShadeTex, uv).r;
                int biome = (int)round(biomeRaw * 255.0);
                int bit = 1 << biome;
                bool biomeEnabled = (_BiomeMask & bit) != 0;
                if (!biomeEnabled) return src;

                // Sobel sur sunShade en espace écran (texels de la sunShadeRT = texels écran)
                float2 d = _SunShadeTex_TexelSize.xy;
                float nw = SunShade(uv + float2(-d.x,  d.y));
                float n  = SunShade(uv + float2(   0,  d.y));
                float ne = SunShade(uv + float2( d.x,  d.y));
                float w  = SunShade(uv + float2(-d.x,    0));
                float e  = SunShade(uv + float2( d.x,    0));
                float sw = SunShade(uv + float2(-d.x, -d.y));
                float s  = SunShade(uv + float2(   0, -d.y));
                float se = SunShade(uv + float2( d.x, -d.y));

                float gx = (-nw + ne) + 2.0 * (-w + e) + (-sw + se);
                float gy = ( nw + 2.0 * n + ne) - (sw + 2.0 * s + se);
                float mag = sqrt(gx * gx + gy * gy);

                float edge = step(_EdgeThreshold, mag);
                return lerp(src, _EdgeColor, edge * _EdgeBlend);
            }
            ENDHLSL
        }
    }
}
