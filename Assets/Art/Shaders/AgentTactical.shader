Shader "Evolution/AgentTactical"
{
    Properties
    {
        _Color ("Main Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" "IgnoreProjector"="True" }
        LOD 100

        Cull Off
        ZWrite On

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0

            #include "UnityCG.cginc"

            // Must match the C# struct Agent exactly (40 bytes)
            struct Agent {
                float2 position;    // 8
                float  angle;       // 4
                int    speciesIndex;// 4
                float  age;         // 4
                float  health;      // 4
                float  hunger;      // 4
                int    navState;    // 4
                int    targetWp;    // 4
                int    cargo;       // 4
            };

            StructuredBuffer<Agent> _AgentBuffer;
            StructuredBuffer<int>   _VisibleAgentIds;

            UNITY_DECLARE_TEX2DARRAY(_SpriteArray);
            float4 _SpriteData[32]; // x=cols, y=rows, z=uScale, w=vScale

            // [min.x, min.y, size.x, size.y] in WORLD SPACE of DisplayTarget
            float4 _MapWorldBounds;
            // [mapW, mapH, center.z, 0]
            float4 _MapSimParams;

            struct appdata_t {
                float4 vertex    : POSITION;
                float2 uv        : TEXCOORD0;
                uint   instanceID: SV_InstanceID;
            };

            struct v2f {
                float4 pos       : SV_POSITION;
                float2 uv        : TEXCOORD0;
                float4 agentData : TEXCOORD1; // x=spIndex, y=angle, z=timeOffset
            };

            v2f vert (appdata_t v)
            {
                v2f o;

                // --- Fetch agent from indirect buffer ---
                int agentId = _VisibleAgentIds[v.instanceID];
                Agent a = _AgentBuffer[agentId];

                // --- Sim → World XY -----------------------------------------------
                // a.position is in sim space [0..mapW] x [0..mapH]
                // _MapWorldBounds = (worldMinX, worldMinY, worldSizeX, worldSizeY)
                float2 worldPos2D = _MapWorldBounds.xy
                                  + (a.position / _MapSimParams.xy) * _MapWorldBounds.zw;

                // --- Scale of the quad (world units per sprite) --------------------
                // One sprite = (worldSize / mapSize) * spritePixels
                // We use 8-sim-pixel radius (agentRadius=1 default, x8 for visibility)
                float pixelToWorld = max(_MapWorldBounds.z / _MapSimParams.x,
                                         _MapWorldBounds.w / _MapSimParams.y);
                float scale = pixelToWorld * 8.0;

                // Local quad vertex offset in world XY
                float2 offset   = v.vertex.xy * scale;
                float2 finalXY  = worldPos2D + offset;

                // --- Z : place agents at zBase, spread by Y for Y-sorting ----------
                float normY  = (worldPos2D.y - _MapWorldBounds.y)
                             / max(0.0001, _MapWorldBounds.w);
                float zDepth = _MapSimParams.z - 1.0 + normY * 0.9;

                // --- KEY FIX : project directly from WORLD SPACE ------------------
                // UnityObjectToClipPos uses UNITY_MATRIX_M (the GO transform), which
                // is irrelevant for GPU-indirect calls.  Use VP directly instead.
                float4 worldPos4 = float4(finalXY.x, finalXY.y, zDepth, 1.0);
                o.pos = mul(UNITY_MATRIX_VP, worldPos4);

                o.uv        = v.uv;
                o.agentData = float4(a.speciesIndex, a.angle,
                                     a.position.x + a.position.y, 0);
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

                // cols = 0 → espèce gérée par un autre renderer (ex: UnitSpriteRenderer), invisible ici
                if (cols <= 0.0) { clip(-1); return fixed4(0,0,0,0); }

                // No sprite loaded → RED circle for debug
                if (rows < 1.0 || uScale <= 0.0)
                {
                    float dist = length(i.uv - float2(0.5, 0.5));
                    clip(0.5 - dist);
                    return fixed4(1, 0, 0, 1);
                }

                // Direction row (4-dir: up=0, left=1, down=2, right=3)
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

                // Animation column
                int col = (int)fmod(floor(_Time.y * 10.0 + timeOffset), cols);

                // Sample sprite atlas
                float2 uv = float2(
                    (col + i.uv.x) / cols * uScale,
                    (invRow + i.uv.y) / rows * vScale
                );

                fixed4 c = UNITY_SAMPLE_TEX2DARRAY(_SpriteArray, float3(uv.x, uv.y, spIndex));

                clip(c.a - 0.1);
                return c;
            }
            ENDCG
        }
    }
}
