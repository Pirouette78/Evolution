Shader "Evolution/AgentTacticalShadow"
{
    Properties
    {
        _Color ("Main Color", Color) = (0,0,0,1)
        _GlobalAlpha ("Global Alpha", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100

        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap

            #include "UnityCG.cginc"

            // Must match the C# struct Agent exactly
            struct Agent {
                float2 position;    
                float  angle;       
                int    speciesIndex;
                float  age;         
                float  health;      
                float  hunger;      
                int    navState;    
                int    targetWp;    
                int    cargo;       
            };

            StructuredBuffer<Agent> _AgentBuffer;
            StructuredBuffer<int>   _VisibleAgentIds;

            UNITY_DECLARE_TEX2DARRAY(_SpriteArray);
            float4 _SpriteData[32]; // x=cols, y=rows, z=uScale, w=vScale
            float4 _SpriteScaleAnchor[32]; // x=w, y=h, z=ancX, w=ancY
            float  _GlobalAlpha;
            
            float _GlobalShadowSkew;
            float _GlobalShadowOpacity;
            float _GlobalShadowScaleY;

            float4 _MapWorldBounds;
            float4 _MapSimParams;
            float4 _MapTerrainParams;

            struct appdata_t {
                float4 vertex    : POSITION;
                float2 uv        : TEXCOORD0;
                uint   instanceID: SV_InstanceID;
            };

            struct v2f {
                float4 pos       : SV_POSITION;
                float2 uv        : TEXCOORD0;
                float4 agentData : TEXCOORD1; 
            };

            v2f vert (appdata_t v)
            {
                v2f o;

                int agentId = _VisibleAgentIds[v.instanceID];
                Agent a = _AgentBuffer[agentId];

                float2 worldPos2D = _MapWorldBounds.xy
                                  + (a.position / _MapSimParams.xy) * _MapWorldBounds.zw;

                // --- Pixel-snap : aligne l'ancre sur la grille de pixels écran.
                float pixelSize = (unity_OrthoParams.y * 2.0) / _ScreenParams.y;
                worldPos2D = round(worldPos2D / pixelSize) * pixelSize;

                float4 scaleAnchor = _SpriteScaleAnchor[a.speciesIndex];
                float stW = scaleAnchor.x;
                float stH = scaleAnchor.y;
                float ancX = scaleAnchor.z;
                float ancY = scaleAnchor.w;
                
                float pixelToWorldX = _MapWorldBounds.z / _MapTerrainParams.x;
                float pixelToWorldY = _MapWorldBounds.w / _MapTerrainParams.y;
                
                float scaleX = stW * pixelToWorldX;
                float scaleY = stH * pixelToWorldY;

                // --- SHADOW DISTORTION ---
                float relativeHeight = v.vertex.y + 0.5;
                float2 modVert = v.vertex.xy;
                modVert.y = -0.5 + (relativeHeight * _GlobalShadowScaleY);
                modVert.x += relativeHeight * _GlobalShadowSkew;

                float2 uvOffset = modVert + float2(0.5 - ancX, 0.5 - ancY);
                float2 offset = float2(uvOffset.x * scaleX, uvOffset.y * scaleY);
                float2 finalXY  = worldPos2D + offset;

                // --- Z-Sorting ---
                float normY  = (worldPos2D.y - _MapWorldBounds.y) / max(0.0001, _MapWorldBounds.w);
                
                // On met ZDepth très légèrement derrière les agents (+0.002f) pour ne pas croiser avec le sprite
                float zDepth = _MapSimParams.z - 1.0 + normY * 0.9 + 0.002;

                float4 worldPos4 = float4(finalXY.x, finalXY.y, zDepth, 1.0);
                o.pos = mul(UNITY_MATRIX_VP, worldPos4);

                o.uv        = v.uv;
                o.agentData = float4(a.speciesIndex, a.angle, a.position.x + a.position.y, 0);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                int   spIndex   = (int)round(i.agentData.x);
                float angle     = i.agentData.y;
                float timeOffset = i.agentData.z;

                float cols   = _SpriteData[spIndex].x;
                float rows   = _SpriteData[spIndex].y;
                float uScale = _SpriteData[spIndex].z;
                float vScale = _SpriteData[spIndex].w;

                if (cols <= 0.0) { clip(-1); return fixed4(0,0,0,0); }
                if (rows < 1.0 || uScale <= 0.0)
                {
                    float dist = length(i.uv - float2(0.5, 0.5));
                    clip(0.5 - dist);
                    return fixed4(0, 0, 0, _GlobalAlpha * _GlobalShadowOpacity);
                }

                float na = fmod(angle + 3.14159, 6.28318) / 6.28318;
                int row = 0;
                if (rows >= 4)
                {
                    if      (na < 0.25) row = 2;
                    else if (na < 0.5 ) row = 3;
                    else if (na < 0.75) row = 0;
                    else                row = 1;
                }
                float invRow = rows - 1.0 - row;

                int col = (int)fmod(floor(_Time.y * 10.0 + timeOffset), cols);

                float2 uv = float2(
                    (col + i.uv.x) / cols * uScale,
                    (invRow + i.uv.y) / rows * vScale
                );

                fixed4 tex = UNITY_SAMPLE_TEX2DARRAY(_SpriteArray, float3(uv.x, uv.y, spIndex));

                // Ombre noire pure, mélangée avec opacité texture + global
                float alpha = tex.a * _GlobalAlpha * _GlobalShadowOpacity;
                clip(alpha - 0.01);
                
                return fixed4(0, 0, 0, alpha);
            }
            ENDCG
        }
    }
}
