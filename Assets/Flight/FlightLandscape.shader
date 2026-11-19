Shader "Greatwall/Flight Landscape"
{
    Properties
    {
        [NoScaleOffset] _Control("Terrain Layer Weights", 2D) = "red" {}
        _Layer0("Layer 1 / Masonry", 2D) = "white" {}
        [NoScaleOffset] _Layer1("Layer 2", 2D) = "white" {}
        [NoScaleOffset] _Layer2("Layer 3", 2D) = "white" {}
        [NoScaleOffset] _Layer3("Layer 4", 2D) = "white" {}
        [NoScaleOffset] _DetailCoverage("Part1 Vegetation Layers", 2D) = "black" {}
        _TileFrequency("Layer Tile Frequency", Vector) = (64, 64, 64, 64)
        _StoneTint("Stone Tint", Color) = (1, 1, 1, 1)
        _FoliageCutoff("Foliage Cutoff", Range(0, 1)) = 0
        _FoliageDistance("Foliage Draw Distance", Float) = 235
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull Mode", Float) = 2
        _HazeColor("Horizon Haze", Color) = (0.36, 0.46, 0.50, 1)
        [NoScaleOffset] _Panorama("Horizon Panorama", 2D) = "gray" {}
        _PanoramaTint("Panorama Tint", Color) = (0.5, 0.5, 0.5, 1)
        _PanoramaRotation("Panorama Rotation", Vector) = (1, 0, 0, 0)
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "Flight Terrain"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_local_fragment _ _FLIGHT_MASONRY _FLIGHT_FOLIAGE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_Control); SAMPLER(sampler_Control);
            TEXTURE2D(_Layer0);
            TEXTURE2D(_Layer1);
            TEXTURE2D(_Layer2);
            TEXTURE2D(_Layer3);
            SAMPLER(sampler_Layer0);
            TEXTURE2D(_DetailCoverage); SAMPLER(sampler_DetailCoverage);
            TEXTURE2D(_Panorama); SAMPLER(sampler_Panorama);

            CBUFFER_START(UnityPerMaterial)
                float4 _TileFrequency;
                float4 _Layer0_ST;
                half4 _StoneTint;
                half _FoliageCutoff;
                float _FoliageDistance;
                half4 _HazeColor;
                half4 _PanoramaTint;
                float4 _PanoramaRotation;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                half3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output = (Varyings)0;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 normal = normalize(input.normalWS);
                float3 view = input.positionWS - GetCameraPositionWS();
                float range = length(view);
                #if defined(_FLIGHT_MASONRY)
                    float2 uv = TRANSFORM_TEX(input.uv, _Layer0);
                    half3 albedo = SAMPLE_TEXTURE2D(_Layer0, sampler_Layer0, uv).rgb * _StoneTint.rgb;
                    albedo = albedo * 1.34h + half3(0.052h, 0.035h, 0.021h);
                #elif defined(_FLIGHT_FOLIAGE)
                    half4 leaf = SAMPLE_TEXTURE2D(_Layer0, sampler_Layer0, input.uv);
                    half fade = 1.0h - smoothstep(_FoliageDistance - 65.0, _FoliageDistance + 18.0, range);
                    clip(fade - 0.012h);
                    if (_FoliageCutoff > 0)
                        clip(leaf.a * fade - _FoliageCutoff);
                    half3 albedo = leaf.rgb * _StoneTint.rgb;
                #else
                    half4 weights = SAMPLE_TEXTURE2D(_Control, sampler_Control, input.uv);
                    weights /= max(dot(weights, half4(1, 1, 1, 1)), 0.01h);
                    half3 albedo =
                        SAMPLE_TEXTURE2D(_Layer0, sampler_Layer0, input.uv * _TileFrequency.x).rgb * weights.r
                        + SAMPLE_TEXTURE2D(_Layer1, sampler_Layer0, input.uv * _TileFrequency.y).rgb * weights.g
                        + SAMPLE_TEXTURE2D(_Layer2, sampler_Layer0, input.uv * _TileFrequency.z).rgb * weights.b
                        + SAMPLE_TEXTURE2D(_Layer3, sampler_Layer0, input.uv * _TileFrequency.w).rgb * weights.a;
                    albedo *= lerp(half3(0.82h, 0.77h, 0.68h), 1.0h, smoothstep(0.60h, 0.90h, normal.y));
                    half3 cover = SAMPLE_TEXTURE2D(_DetailCoverage, sampler_DetailCoverage, input.uv).rgb;
                    half grain = SAMPLE_TEXTURE2D(_Layer0, sampler_Layer0, input.uv * (_TileFrequency.x * 0.35)).g;
                    half grass = saturate(max(cover.x, cover.y) * (0.76h + grain * 0.61h));
                    albedo *= lerp(1.0h, half3(0.92h, 0.77h, 0.48h), grass);
                #endif
                Light sun = GetMainLight();
                half light = saturate(dot(normal, sun.direction));
                half3 landscape = albedo * (0.51h + min(sun.color, 1.5h) * light * 0.58h);
                // The distant hills meet the original panorama sooner near the horizon.
                float horizon = smoothstep(-0.32, -0.08, view.y / max(range, 0.01));
                float hazeEnd = lerp(950.0, 790.0, horizon);
                half haze = smoothstep(220.0, hazeEnd, range);
                half3 distantColor = _HazeColor.rgb;
                if (_PanoramaRotation.z > 0.5 && range > 405.0)
                {
                    float3 direction = view / range;
                    float2 rotated = float2(
                        dot(direction.xz, float2(_PanoramaRotation.x, _PanoramaRotation.y)),
                        dot(direction.xz, float2(-_PanoramaRotation.y, _PanoramaRotation.x)));
                    float2 uv = float2(0.5, 1.0) - float2(atan2(rotated.y, rotated.x),
                        acos(clamp(direction.y, -1.0, 1.0))) * float2(0.5 / PI, 1.0 / PI);
                    half3 sky = SAMPLE_TEXTURE2D_LOD(_Panorama, sampler_Panorama, uv, 1).rgb
                        * _PanoramaTint.rgb * _PanoramaRotation.w;
                    distantColor = lerp(distantColor, sky, smoothstep(405.0, hazeEnd, range));
                }
                return half4(lerp(landscape, distantColor, haze), 1);
            }
            ENDHLSL
        }
    }
}
