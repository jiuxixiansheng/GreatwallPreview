Shader "Greatwall/Lamp Flame"
{
    Properties
    {
        _FlameBrightness ("Flame Brightness", Range(0, 1)) = 0.65
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _FlameBrightness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half height : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float height = input.uv.y;
                float t = _Time.y;
                float3 p = input.positionOS.xyz;
                float lean = height * height;
                p.x += lean * (0.008 + 0.011 * sin(t * 11.0 - height * 2.8));
                p.z += lean * 0.009 * sin(t * 8.0 - height * 3.5);
                p.y *= 1.0 + 0.055 * sin(t * 14.0) + 0.03 * sin(t * 23.0);

                VertexPositionInputs pos = GetVertexPositionInputs(p);
                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.height = height;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half facing = saturate(dot(normalize(input.normalWS), GetWorldSpaceNormalizeViewDir(input.positionWS)));
                half core = smoothstep(0.15h, 0.90h, facing) * (1.0h - smoothstep(0.32h, 0.9h, input.height));
                half3 orange = lerp(half3(0.65h, 0.055h, 0.005h), half3(1.55h, 0.32h, 0.017h), sin(input.height * 3.14159h));
                half3 color = lerp(orange, half3(3.2h, 1.65h, 0.38h), core);
                return half4(color * _FlameBrightness * (0.94h + 0.06h * sin(_Time.y * 17.0)), 1.0h);
            }
            ENDHLSL
        }
    }
}
