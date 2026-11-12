Shader "Greatwall/Window Daylight Video"
{
    Properties
    {
        [MainTexture] _BaseMap ("Video Texture", 2D) = "white" {}
        _Saturation ("Saturation", Range(0, 1)) = 0.76
        _HighlightSoftness ("Highlight Softness", Range(0, 0.5)) = 0.12
        _DaylightWash ("Daylight Wash", Range(0, 0.2)) = 0.045
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }

        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float _Saturation;
                float _HighlightSoftness;
                float _DaylightWash;
            CBUFFER_END

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
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                half luma = dot(color, half3(0.2126h, 0.7152h, 0.0722h));
                color = lerp(luma.xxx, color, _Saturation);
                color *= 1.0h - _HighlightSoftness * smoothstep(0.35h, 1.0h, luma);
                // Lift darker detail gently, without adding a light to the scene.
                color = lerp(color, half3(0.74h, 0.82h, 0.92h), _DaylightWash * (1.0h - saturate(luma)));
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }
}
