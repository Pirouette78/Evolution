Shader "Evolution/TerrainOverlay"
{
    Properties
    {
        _MainTex ("Map Data (R=Type Raw)", 2D) = "white" {}
        _TilesetTex ("Tileset Atlas", 2D) = "white" {}
        _Alpha ("Alpha", Range(0, 1)) = 1
        _Tint ("Tint Color", Color) = (1, 1, 1, 1)

        _MapSize ("Map Size (Width, Height)", Vector) = (1280, 720, 0, 0)
        _TileSize ("Tile Size (Pixels)", Float) = 32

        // Starting column/row for the 7x7 block of each terrain
        _WaterOffset ("Water Offset (Col, Row)", Vector) = (0, 0, 0, 0)
        _SandOffset ("Sand Offset (Col, Row)", Vector) = (7, 0, 0, 0)
        _GrassOffset ("Grass Offset (Col, Row)", Vector) = (14, 0, 0, 0)
        _ForestOffset ("Forest Offset", Vector) = (21, 0, 0, 0)
        _RockOffset ("Rock Offset", Vector) = (28, 0, 0, 0)
        _SnowOffset ("Snow Offset", Vector) = (35, 0, 0, 0)

        // Cliff overlays (drawn on top of water tiles bordering land)
        _CliffOffset ("Cliff Offset (Col, Row)", Vector) = (0, 7, 0, 0)

        // Shading parameters
        _ShadeSteps ("Shade Steps", Float) = 4
        _ShadeDarkness ("Max Darkness", Range(0, 1)) = 0.4
        _NoiseStrength ("Noise Strength", Range(0, 1)) = 0.1
        _Waviness ("Organic Waviness", Range(0, 1)) = 0.3
        
        // Sun Shading
        _SunShadowStrength ("Sun Shadow Strength", Range(0, 1)) = 0.5
        _SlopeScale ("Slope Exaggeration", Range(0, 100)) = 20.0

        [Toggle] _ShadeWater ("Shade Water", Float) = 0
        [Toggle] _ShadeSand ("Shade Sand", Float) = 1
        [Toggle] _ShadeGrass ("Shade Grass", Float) = 1
        [Toggle] _ShadeForest ("Shade Forest", Float) = 1
        [Toggle] _ShadeRock ("Shade Rock", Float) = 1
        [Toggle] _ShadeSnow ("Shade Snow", Float) = 1

        [Toggle] _SunShadeWater ("Sun Shade Water", Float) = 0
        [Toggle] _SunShadeSand ("Sun Shade Sand", Float) = 1
        [Toggle] _SunShadeGrass ("Sun Shade Grass", Float) = 1
        [Toggle] _SunShadeForest ("Sun Shade Forest", Float) = 1
        [Toggle] _SunShadeRock ("Sun Shade Rock", Float) = 1
        [Toggle] _SunShadeSnow ("Sun Shade Snow", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            
            sampler2D _TilesetTex;
            float4 _TilesetTex_TexelSize;
            
            float4 _MainTex_ST;
            float _Alpha;
            float4 _Tint;

            float4 _MapSize;
            float _TileSize;

            float4 _WaterOffset;
            float4 _SandOffset;
            float4 _GrassOffset;
            float4 _ForestOffset;
            float4 _RockOffset;
            float4 _SnowOffset;
            float4 _CliffOffset;

            float _ShadeSteps;
            float _ShadeDarkness;
            float _NoiseStrength;
            float _Waviness;
            float _WaterThreshold;
            float _ShadeWater;
            float _ShadeSand;
            float _ShadeGrass;
            float _ShadeForest;
            float _ShadeRock;
            float _ShadeSnow;

            float _SunShadeWater;
            float _SunShadeSand;
            float _SunShadeGrass;
            float _SunShadeForest;
            float _SunShadeRock;
            float _SunShadeSnow;

            float _GlobalSunPosition;
            float _SunShadowStrength;
            float _SlopeScale;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            float2 GetGodotImagePosition(int mask)
            {
                if(mask==28) return float2(0, 0);
                if(mask==116) return float2(1, 0);
                if(mask==84) return float2(2, 0);
                if(mask==92) return float2(3, 0);
                if(mask==124) return float2(4, 0);
                if(mask==112) return float2(5, 0);
                if(mask==16) return float2(6, 0);
                if(mask==23) return float2(0, 1);
                if(mask==213) return float2(1, 1);
                if(mask==85) return float2(2, 1);
                if(mask==95) return float2(3, 1);
                if(mask==253) return float2(5, 1);
                if(mask==113) return float2(6, 1);
                if(mask==21) return float2(0, 2);
                if(mask==93) return float2(1, 2);
                if(mask==125) return float2(2, 2);
                if(mask==119) return float2(3, 2);
                if(mask==215) return float2(4, 2);
                if(mask==199) return float2(5, 2);
                if(mask==209) return float2(6, 2);
                if(mask==29) return float2(0, 3);
                if(mask==127) return float2(1, 3);
                if(mask==247) return float2(2, 3);
                if(mask==221) return float2(3, 3);
                if(mask==117) return float2(4, 3);
                if(mask==68) return float2(5, 3);
                if(mask==81) return float2(6, 3);
                if(mask==31) return float2(0, 4);
                if(mask==255) return float2(1, 4);
                if(mask==245) return float2(2, 4);
                if(mask==87) return float2(3, 4);
                if(mask==193) return float2(4, 4);
                if(mask==1) return float2(6, 4);
                if(mask==7) return float2(0, 5);
                if(mask==223) return float2(1, 5);
                if(mask==241) return float2(2, 5);
                if(mask==17) return float2(3, 5);
                if(mask==0) return float2(4, 5);
                if(mask==20) return float2(5, 5);
                if(mask==80) return float2(6, 5);
                if(mask==4) return float2(0, 6);
                if(mask==71) return float2(1, 6);
                if(mask==197) return float2(2, 6);
                if(mask==69) return float2(3, 6);
                if(mask==64) return float2(4, 6);
                if(mask==5) return float2(5, 6);
                if(mask==65) return float2(6, 6);
                return float2(4, 5); // Default single tile
            }

            int getMapType(float2 uv, float dx, float dy) {
                // MainTex is now purely storing Type in R channel (0 to 5)
                float2 sampleUV = uv + float2(dx, dy) * _MainTex_TexelSize.xy;
                return round(tex2D(_MainTex, sampleUV).r * 255.0);
            }

            // Pseudo-random noise based on tile coordinate
            float random(float2 st) {
                return frac(sin(dot(st.xy, float2(12.9898, 78.233))) * 43758.5453123);
            }

            // Smooth procedural noise for organic blob transitions inside the tile
            float smoothNoise(float2 st) {
                float2 i = floor(st);
                float2 f = frac(st);
                float a = random(i);
                float b = random(i + float2(1.0, 0.0));
                float c = random(i + float2(0.0, 1.0));
                float d = random(i + float2(1.0, 1.0));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
            }

            float InverseLerp(float a, float b, float value) {
                if (b <= a) return 0.0;
                return saturate((value - a) / (b - a));
            }

            float GetBiomeLocalHeight(float h, float waterThresh, int c) {
                if (c == 0) return InverseLerp(0.0, waterThresh, h);
                float land = InverseLerp(waterThresh, 1.0, h);
                if (c == 1) return InverseLerp(0.0, 0.05, land);
                if (c == 2) return InverseLerp(0.05, 0.40, land);
                if (c == 3) return InverseLerp(0.40, 0.70, land);
                if (c == 4) return InverseLerp(0.70, 0.95, land);
                if (c == 5) return InverseLerp(0.95, 1.0, land);
                return 0.0;
            }

            float GetGlobalHeightSmooth(float2 uv) {
                float2 pixel = uv * _MapSize.xy - 0.5;
                float2 f = frac(pixel);
                float2 i = floor(pixel);
                
                float2 uv00 = (i + 0.5) * _MainTex_TexelSize.xy;
                float2 uv10 = uv00 + float2(_MainTex_TexelSize.x, 0);
                float2 uv01 = uv00 + float2(0, _MainTex_TexelSize.y);
                float2 uv11 = uv00 + _MainTex_TexelSize.xy;
                
                float g00 = tex2D(_MainTex, uv00).g;
                float g10 = tex2D(_MainTex, uv10).g;
                float g01 = tex2D(_MainTex, uv01).g;
                float g11 = tex2D(_MainTex, uv11).g;
                
                float g0 = lerp(g00, g10, f.x);
                float g1 = lerp(g01, g11, f.x);
                return lerp(g0, g1, f.y);
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Ancrage au centre du pixel : évite les désaccords UV à la frontière exacte de deux pixels
                float2 mapPixel   = floor(i.uv * _MapSize.xy);
                float2 snappedUV  = (mapPixel + 0.5) * _MainTex_TexelSize.xy;

                int c  = getMapType(snappedUV,  0,  0);
                int nw = getMapType(snappedUV, -1,  1);
                int n  = getMapType(snappedUV,  0,  1);
                int ne = getMapType(snappedUV,  1,  1);
                int w  = getMapType(snappedUV, -1,  0);
                int e  = getMapType(snappedUV,  1,  0);
                int sw = getMapType(snappedUV, -1, -1);
                int s  = getMapType(snappedUV,  0, -1);
                int se = getMapType(snappedUV,  1, -1);

                int maxLayer = max(max(max(c, nw), max(n, ne)), max(max(w, e), max(sw, max(s, se))));

                float2 offsets[6];
                offsets[0] = _WaterOffset.xy;
                offsets[1] = _SandOffset.xy;
                offsets[2] = _GrassOffset.xy;
                offsets[3] = _ForestOffset.xy;
                offsets[4] = _RockOffset.xy;
                offsets[5] = _SnowOffset.xy;

                float atlasCols = round(_TilesetTex_TexelSize.z / _TileSize);
                float atlasRows = round(_TilesetTex_TexelSize.w / _TileSize);
                // Position fractionnelle dans le pixel courant + inset demi-texel contre le bleeding
                float halfTexel = 0.5 / _TileSize;
                float2 subUV = clamp(i.uv * _MapSize.xy - mapPixel, halfTexel, 1.0 - halfTexel);

                // Layer 0 (Base layer) is drawn as a full solid tile.
                // In Godot 47-blob, solid block is at (1, 4)
                float2 solidCoord = float2(1, 4);
                float invRowBase = atlasRows - 1.0 - (offsets[0].y + solidCoord.y);
                float2 baseUV = float2((offsets[0].x + solidCoord.x + subUV.x) / atlasCols, (invRowBase + subUV.y) / atlasRows);
                fixed4 result = tex2Dlod(_TilesetTex, float4(baseUV, 0, 0)) * _Tint;
                
                int pixelBiome = c; // Track the actual visible biome at this pixel

                // Cliff pass (eau seulement, dessiné AVANT le débordement des terrains)
                if (c == 0) {
                    bool cn = (n >= 1);
                    bool ce = (e >= 1);
                    bool cs = (s >= 1);
                    bool cw = (w >= 1);
                    bool cne = cn && ce && (ne >= 1);
                    bool cse = cs && ce && (se >= 1);
                    bool csw = cs && cw && (sw >= 1);
                    bool cnw = cn && cw && (nw >= 1);

                    bool in_ = !cn;
                    bool ie  = !ce;
                    bool is_ = !cs;
                    bool iw  = !cw;
                    bool ine = in_ && ie && (ne < 1);
                    bool ise = is_ && ie && (se < 1);
                    bool isw = is_ && iw && (sw < 1);
                    bool inw = in_ && iw && (nw < 1);

                    int cliffMask = 0;
                    if (in_) cliffMask |= 1;
                    if (ine) cliffMask |= 2;
                    if (ie)  cliffMask |= 4;
                    if (ise) cliffMask |= 8;
                    if (is_) cliffMask |= 16;
                    if (isw) cliffMask |= 32;
                    if (iw)  cliffMask |= 64;
                    if (inw) cliffMask |= 128;

                    if (cn || ce || cs || cw || (ne >= 1) || (se >= 1) || (sw >= 1) || (nw >= 1)) {
                        float2 cliffCoord = GetGodotImagePosition(cliffMask);
                        float invRowCliff = atlasRows - 1.0 - (_CliffOffset.y + cliffCoord.y);
                        float2 cliffUV = float2((_CliffOffset.x + cliffCoord.x + subUV.x) / atlasCols, (invRowCliff + subUV.y) / atlasRows);
                        fixed4 cliffCol = tex2Dlod(_TilesetTex, float4(cliffUV, 0, 0));
                        result.rgb = lerp(result.rgb, cliffCol.rgb, cliffCol.a);
                    }
                }

                // Terrain layers : tile pleine si c >= L, débordement sur toutes cases inférieures (y compris eau)
                [unroll(5)]
                for (int L = 1; L <= 5; L++) {
                    if (L > maxLayer) break;

                    bool bn = (n >= L);
                    bool be = (e >= L);
                    bool bs = (s >= L);
                    bool bw = (w >= L);
                    bool bne = bn && be && (ne >= L);
                    bool bse = bs && be && (se >= L);
                    bool bsw = bs && bw && (sw >= L);
                    bool bnw = bn && bw && (nw >= L);

                    // Voisins inférieurs (là où ce terrain déborde)
                    bool in_ = (n < L);
                    bool ie  = (e < L);
                    bool is_ = (s < L);
                    bool iw  = (w < L);
                    bool ine = in_ && ie && (ne < L);
                    bool ise = is_ && ie && (se < L);
                    bool isw = is_ && iw && (sw < L);
                    bool inw = in_ && iw && (nw < L);

                    float2 localCoord;
                    if (c >= L) {
                        // Case appartient au layer → tile pleine (atlas inversé : pleine = mask 0 → pos (4,5))
                        localCoord = float2(5, 4);
                    } else if (bn || be || bs || bw || (ne >= L) || (se >= L) || (sw >= L) || (nw >= L)) {
                        // Case inférieure : masque inversé des voisins supérieurs
                        bool dn  = !bn;
                        bool de  = !be;
                        bool ds  = !bs;
                        bool dw  = !bw;
                        bool dne = dn && de && (ne < L);
                        bool dse = ds && de && (se < L);
                        bool dsw = ds && dw && (sw < L);
                        bool dnw = dn && dw && (nw < L);
                        int mask = 0;
                        if (dn)  mask |= 1;
                        if (dne) mask |= 2;
                        if (de)  mask |= 4;
                        if (dse) mask |= 8;
                        if (ds)  mask |= 16;
                        if (dsw) mask |= 32;
                        if (dw)  mask |= 64;
                        if (dnw) mask |= 128;
                        localCoord = GetGodotImagePosition(mask);
                    } else {
                        continue;
                    }


                    float invRow = atlasRows - 1.0 - (offsets[L].y + localCoord.y);
                    float2 layerUV = float2((offsets[L].x + localCoord.x + subUV.x) / atlasCols, (invRow + subUV.y) / atlasRows);
                    fixed4 layerCol = tex2Dlod(_TilesetTex, float4(layerUV, 0, 0));
                    result.rgb = lerp(result.rgb, layerCol.rgb, layerCol.a);
                    
                    if (layerCol.a > 0.5) {
                        pixelBiome = L;
                    }
                }

                // --- Shading ---
                // True global topographical contours!
                float globalHeight = GetGlobalHeightSmooth(i.uv);
                float localBiomeHeight = saturate(GetBiomeLocalHeight(globalHeight, _WaterThreshold, pixelBiome));
                
                // Calculate Slope for Sun Shading
                float2 uvX = float2(_MainTex_TexelSize.x * 2.0, 0);
                float2 uvY = float2(0, _MainTex_TexelSize.y * 2.0);

                float hL = GetGlobalHeightSmooth(i.uv - uvX);
                float hR = GetGlobalHeightSmooth(i.uv + uvX);
                float hD = GetGlobalHeightSmooth(i.uv - uvY);
                float hU = GetGlobalHeightSmooth(i.uv + uvY);

                float dX = (hR - hL);
                float dY = (hU - hD);

                float3 normal = normalize(float3(-dX * _SlopeScale, -dY * _SlopeScale, 1.0));
                
                // Light direction: Z is up.
                float3 lightDir = normalize(float3(_GlobalSunPosition, 0.0, 0.5));
                float NdotL = saturate(dot(normal, lightDir));
                float flatLight = saturate(lightDir.z);
                
                // Only darken slopes facing away from the sun
                float sunShadeFactor = min(1.0, NdotL / max(flatLight, 0.001));
                float sunShade = lerp(1.0 - _SunShadowStrength, 1.0, sunShadeFactor);

                // Wave distortion for extra organic boundary (controlled by Waviness)
                float wavy = smoothNoise(i.uv * _MapSize.xy * 1.5);
                
                // Crisp per-pixel dither to recreate pixel-art boundary
                float2 pixelCoord = floor(i.uv * _MapSize.xy * _TileSize);
                float dither = random(pixelCoord);
                
                float modifiedHeight = saturate(localBiomeHeight + (wavy - 0.5) * _Waviness + (dither - 0.5) * _NoiseStrength);

                // Safe step calculation ensure we don't exceed the top step index
                float stepIndex = min(floor(modifiedHeight * _ShadeSteps), _ShadeSteps - 1.0);
                float stepped = stepIndex / max(_ShadeSteps - 1.0, 1.0);
                float shade = lerp(1.0 - _ShadeDarkness, 1.0, stepped);

                bool applyShade = false;
                if (pixelBiome == 0 && _ShadeWater > 0.5) applyShade = true;
                if (pixelBiome == 1 && _ShadeSand > 0.5) applyShade = true;
                if (pixelBiome == 2 && _ShadeGrass > 0.5) applyShade = true;
                if (pixelBiome == 3 && _ShadeForest > 0.5) applyShade = true;
                if (pixelBiome == 4 && _ShadeRock > 0.5) applyShade = true;
                if (pixelBiome == 5 && _ShadeSnow > 0.5) applyShade = true;

                bool applySlope = false;
                if (pixelBiome == 0 && _SunShadeWater > 0.5) applySlope = true;
                if (pixelBiome == 1 && _SunShadeSand > 0.5) applySlope = true;
                if (pixelBiome == 2 && _SunShadeGrass > 0.5) applySlope = true;
                if (pixelBiome == 3 && _SunShadeForest > 0.5) applySlope = true;
                if (pixelBiome == 4 && _SunShadeRock > 0.5) applySlope = true;
                if (pixelBiome == 5 && _SunShadeSnow > 0.5) applySlope = true;

                if (applyShade) {
                    result.rgb *= shade;
                }
                if (applySlope) {
                    result.rgb *= sunShade;
                }

                result.a *= _Alpha;
                return result;
            }
            ENDCG
        }
    }
    FallBack "Transparent/Diffuse"
}
