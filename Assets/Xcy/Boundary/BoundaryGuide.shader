Shader "Xcy/Boundary/Guide"
{
    Properties
    {
        _Tint ("Guide Color", Color) = (1, 0.9, 0.35, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.75
        _EdgeSoftness ("Edge Softness", Range(0.001, 0.5)) = 0.08
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Overlay+10" "RenderType"="Transparent" }
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _Tint;
            float _Opacity;
            float _EdgeSoftness;
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 edge = min(input.uv, 1.0 - input.uv);
                float feather = smoothstep(0.0, max(_EdgeSoftness, 0.001), min(edge.x, edge.y));
                return half4(_Tint.rgb, saturate(_Opacity * _Tint.a * feather));
            }
            ENDHLSL
        }
    }
}
