Shader "Evolution/TerrainOverlay"
{
    Properties
    {
        _MainTex ("Map Data (R=Type, G=Mask)", 2D) = "white" {}
        _TilesetTex ("Tileset Atlas", 2D) = "white" {}
        _Alpha ("Alpha", Range(0, 1)) = 1
        _Tint ("Tint Color", Color) = (1, 1, 1, 1)

        _MapSize ("Map Size (Width/Height)", Float) = 512
        _AtlasCols ("Atlas Columns", Float) = 48
        _AtlasRows ("Atlas Rows", Float) = 17

        // Starting column/row for the 4x4 block of each terrain
        _WaterOffset ("Water Offset (Col, Row)", Vector) = (0, 0, 0, 0)
        _SandOffset ("Sand Offset (Col, Row)", Vector) = (4, 0, 0, 0)
        _GrassOffset ("Grass Offset (Col, Row)", Vector) = (8, 0, 0, 0)
        _ForestOffset ("Forest Offset", Vector) = (12, 0, 0, 0)
        _RockOffset ("Rock Offset", Vector) = (16, 0, 0, 0)
        _SnowOffset ("Snow Offset", Vector) = (20, 0, 0, 0)
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
            sampler2D _TilesetTex;
            float4 _MainTex_ST;
            float _Alpha;
            float4 _Tint;

            float _MapSize;
            float _AtlasCols;
            float _AtlasRows;

            float4 _WaterOffset;
            float4 _SandOffset;
            float4 _GrassOffset;
            float4 _ForestOffset;
            float4 _RockOffset;
            float4 _SnowOffset;

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

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float4 data = tex2D(_MainTex, i.uv);
                
                int terrainType = round(data.r * 255.0);

                float2 baseOffset = float2(0,0);
                if (terrainType == 0) baseOffset = _WaterOffset.xy;
                else if (terrainType == 1) baseOffset = _SandOffset.xy;
                else if (terrainType == 2) baseOffset = _GrassOffset.xy;
                else if (terrainType == 3) baseOffset = _ForestOffset.xy;
                else if (terrainType == 4) baseOffset = _RockOffset.xy;
                else if (terrainType == 5) baseOffset = _SnowOffset.xy;

                // NOUVEAU: Le script C# sauvegarde désormais les coordonnées LOCALES (X, Y) du bloc 12x5 directement dans les canaux Vert (G) et Bleu (B) !
                float2 localOffset = float2(round(data.g * 255.0), round(data.b * 255.0));
                float2 tileCoord = baseOffset + localOffset;

                // Position within the single tile (0.0 to 1.0)
                float2 subUV = frac(i.uv * _MapSize);

                // Unity lit l'image de Bas en Haut (V part de 0 au bas).
                // Pour que notre Ligne 0 soit tout en "HAUT" de l'image, on inverse Y :
                float invertedRow = _AtlasRows - 1.0 - tileCoord.y;
                
                // Calcul final en prenant compte de l'inversion
                float2 finalUV = float2(
                    (tileCoord.x + subUV.x) / _AtlasCols,
                    (invertedRow + subUV.y) / _AtlasRows
                );
                
                fixed4 col = tex2D(_TilesetTex, finalUV) * _Tint;
                col.a *= _Alpha;

                return col;
            }
            ENDCG
        }
    }
    FallBack "Transparent/Diffuse"
}
