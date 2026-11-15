// ぶいちゃライブのスクリーン用。ライトの影響を受けない (Unlit)。
// VRChat の AVPro は映像をガンマ空間 (sRGB) の値のままテクスチャに書くので、
// リニアカラースペースのプロジェクトでそのまま貼ると白っぽく (浅く) 見える。
// _IsAVProInput が 1 のときはガンマ → リニアに直してから出力する。
Shader "BuichaLive/VideoScreen"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "black" {}
        [Toggle] _IsAVProInput ("AVPro input (gamma correction)", Float) = 1
        _Brightness ("Brightness", Range(0, 2)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _IsAVProInput;
            float _Brightness;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                UNITY_TRANSFER_FOG(o, o.vertex);
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float3 col = tex2D(_MainTex, i.uv).rgb;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                if (_IsAVProInput > 0.5) col = GammaToLinearSpace(col);
                #endif
                col *= _Brightness;
                float4 result = float4(col, 1);
                UNITY_APPLY_FOG(i.fogCoord, result);
                return result;
            }
            ENDCG
        }
    }
}
