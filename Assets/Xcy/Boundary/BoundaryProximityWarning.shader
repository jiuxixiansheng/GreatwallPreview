Shader "Xcy/Boundary/ProximityWarning"
{
    Properties
    {
        _Tint ("Line Color", Color) = (1, 0.12, 0.1, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0
        _PatchCenterWS ("Patch Center", Vector) = (0, 0, 0, 0)
        _PatchRadius ("Patch Radius", Float) = 1.75
        _PatchFeather ("Patch Feather", Float) = 0.65
        _GridSpacing ("Grid Spacing", Float) = 0.45
        _LineWidth ("Line Width", Float) = 0.025
        _BoxSize ("Box Size", Vector) = (1, 1, 1, 0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+100" "RenderType"="Transparent" }
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
            float3 _PatchCenterWS;
            float _PatchRadius;
            float _PatchFeather;
            float _GridSpacing;
            float _LineWidth;
            float3 _BoxSize;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                float3 normalOS : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                output.normalOS = input.normalOS;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                if (abs(input.normalOS.y) > 0.5) discard;

                float2 coords = abs(input.normalOS.x) > 0.5
                    ? input.positionOS.yz * _BoxSize.yz
                    : input.positionOS.xy * _BoxSize.xy;
                float spacing = max(_GridSpacing, 0.05);
                float2 cell = abs(frac(coords / spacing + 0.5) - 0.5) * spacing;
                float lineDistance = min(cell.x, cell.y);
                float antialias = max(fwidth(lineDistance), 0.001);
                float lineMask = 1.0 - smoothstep(_LineWidth * 0.5, _LineWidth * 0.5 + antialias, lineDistance);

                float radius = max(_PatchRadius, 0.1);
                float feather = clamp(_PatchFeather, 0.01, radius);
                float patch = 1.0 - smoothstep(radius - feather, radius, distance(input.positionWS, _PatchCenterWS));
                return half4(_Tint.rgb, saturate(_Opacity * _Tint.a * patch * lineMask));
            }
            ENDHLSL
        }
    }
}
