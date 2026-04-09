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

                    float2 localCoord;
                    if (c >= L) {
                        localCoord = float2(1, 4); // tile pleine
                    } else if (bn || be || bs || bw || (ne >= L) || (se >= L) || (sw >= L) || (nw >= L)) {
                        // Débordement sur cette case (eau ou terrain inférieur)
                        int mask = 0;
                        if (bn) mask |= 1;
                        if (bne) mask |= 2;
                        if (be) mask |= 4;
                        if (bse) mask |= 8;
                        if (bs) mask |= 16;
                        if (bsw) mask |= 32;
                        if (bw) mask |= 64;
                        if (bnw) mask |= 128;
                        localCoord = GetGodotImagePosition(mask);
                    } else {
                        continue;
                    }

                    float invRow = atlasRows - 1.0 - (offsets[L].y + localCoord.y);
                    float2 layerUV = float2((offsets[L].x + localCoord.x + subUV.x) / atlasCols, (invRow + subUV.y) / atlasRows);
                    fixed4 layerCol = tex2Dlod(_TilesetTex, float4(layerUV, 0, 0));
                    result.rgb = lerp(result.rgb, layerCol.rgb, layerCol.a);
                }

                result.a *= _Alpha;
                return result;
            }
            ENDCG
        }
    }
    FallBack "Transparent/Diffuse"
}
