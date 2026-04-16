Shader "Evolution/SpriteGlobalShadow"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Shadow Color", Color) = (0, 0, 0, 0.5)
        _ShadowScaleY ("Shadow Length (Scale Y)", Float) = 0.5
    }
    SubShader
    {
        Tags
        { 
            // On met la même queue (Transparent) que le terrain et les sprites.
            // L'ordre d'affichage sera géré naturellement par leur position en Z !
            "Queue"="Transparent"
            "IgnoreProjector"="True" 
            "RenderType"="Transparent" 
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;

            // _Color est passé via MaterialPropertyBlock (_shadowMpb) dans UnitSpriteRenderer
            fixed4 _Color;
            
            // Variables globales injectées par GlobalSunManager chaque frame
            float _GlobalShadowSkew;
            float _ShadowScaleY;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                // Le quad va de -0.5 à +0.5 en Y
                // relativeHeight : 0 en bas, 1 en haut
                float4 pos = IN.vertex;
                float relativeHeight = pos.y + 0.5;

                // 1. Écrase verticalement (0 = sol, _ShadowScaleY = hauteur finale de l'ombre)
                pos.y = -0.5 + (relativeHeight * _ShadowScaleY);

                // 2. Cisaille sur X selon la direction du soleil
                pos.x += relativeHeight * _GlobalShadowSkew;

                OUT.vertex  = UnityObjectToClipPos(pos);
                OUT.texcoord = IN.texcoord;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // Alpha de la texture → forme de l'ombre
                float alpha = tex2D(_MainTex, IN.texcoord).a;
                // Couleur noire + alpha de l'ombre, modulé par l'alpha du MPB
                return fixed4(_Color.rgb, alpha * _Color.a);
            }
            ENDCG
        }
    }
}
