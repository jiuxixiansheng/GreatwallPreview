Shader "Xcy/Outdoor Fog"
{
    Properties
    {
        _FogColor("Fog Color", Color) = (0.58, 0.65, 0.68, 1)
        _Density("Outdoor Density (per metre)", Range(0, 0.05)) = 0.006
        _StartDistance("Clear Near Distance (metres)", Float) = 15
        _MaxDistance("Maximum Fog Distance (metres)", Float) = 300
        _BaseHeight("Height Reference (world Y)", Float) = 0
        _HeightFalloff("Height Falloff (per metre, 0 = uniform)", Range(0, 0.2)) = 0.015
        _SkyStrength("Sky Horizon Strength", Range(0, 1)) = 0.65
        _HorizonFalloff("Sky Horizon Falloff", Range(0, 20)) = 5
        _MaxOpacity("Maximum Opacity", Range(0, 1)) = 0.85
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "OutdoorFog"
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _FogColor;
                float _Density, _StartDistance, _MaxDistance, _BaseHeight;
                float _HeightFalloff, _SkyStrength, _HorizonFalloff, _MaxOpacity;
            CBUFFER_END
            #define MAX_ZONES 16
            int _XcyIndoorCount;
            float4x4 _XcyIndoorWorldToLocal[MAX_ZONES];
            float4 _XcyIndoorExtents[MAX_ZONES];

            // Parametric ray distance stays in world metres even for rotated/scaled boxes.
            float2 BoxInterval(float3 origin, float3 direction, int index, float limit)
            {
                float3 o = mul(_XcyIndoorWorldToLocal[index], float4(origin, 1)).xyz;
                float3 d = mul((float3x3)_XcyIndoorWorldToLocal[index], direction);
                float3 h = _XcyIndoorExtents[index].xyz;
                bool3 parallel = abs(d) < 1e-6;
                if (any(parallel & (abs(o) > h))) return float2(limit, 0);
                float3 safeDirection = float3(parallel.x ? 1e-6 : d.x, parallel.y ? 1e-6 : d.y, parallel.z ? 1e-6 : d.z);
                float3 a = (-h - o) / safeDirection;
                float3 b = ( h - o) / safeDirection;
                float3 nearHit = min(a, b);
                float3 farHit = max(a, b);
                nearHit = float3(parallel.x ? -1e20 : nearHit.x, parallel.y ? -1e20 : nearHit.y, parallel.z ? -1e20 : nearHit.z);
                farHit = float3(parallel.x ? 1e20 : farHit.x, parallel.y ? 1e20 : farHit.y, parallel.z ? 1e20 : farHit.z);
                float enter = max(0, max(nearHit.x, max(nearHit.y, nearHit.z)));
                float leave = min(limit, min(farHit.x, min(farHit.y, farHit.z)));
                return float2(enter, leave);
            }
            float DensityPrimitive(float exponent)
            {
                if (exponent < -12) return exp(-12.0) * (exponent + 13);
                if (exponent > 6) return exp(6.0) * (exponent - 5);
                return exp(exponent);
            }
            float HeightIntegral(float3 origin, float3 direction, float a, float b)
            {
                if (b <= a) return 0;
                float k = _HeightFalloff * direction.y;
                float exponent = -_HeightFalloff * (origin.y + direction.y * a - _BaseHeight);
                if (abs(k) < 1e-5) return exp(clamp(exponent, -12, 6)) * (b - a);
                // Bound density to avoid unbounded fog below the reference height.
                float endExponent = -_HeightFalloff * (origin.y + direction.y * b - _BaseHeight);
                return max(0, (DensityPrimitive(exponent) - DensityPrimitive(endExponent)) / k);
            }
            float OutdoorIntegral(float3 origin, float3 direction, float limit)
            {
                float2 intervals[MAX_ZONES];
                [unroll] for (int init = 0; init < MAX_ZONES; init++) intervals[init] = float2(0, 0);
                int count = 0;
                [loop] for (int z = 0; z < min(_XcyIndoorCount, MAX_ZONES); z++)
                {
                    float2 range = BoxInterval(origin, direction, z, limit);
                    if (range.y > range.x) intervals[count++] = range;
                }
                // Sort and merge overlap: overlapping room boxes must not subtract air twice.
                [loop] for (int i = 1; i < count; i++)
                {
                    float2 value = intervals[i];
                    int j = i - 1;
                    [loop] while (j >= 0)
                    {
                        if (intervals[j].x <= value.x) break;
                        intervals[j + 1] = intervals[j]; j--;
                    }
                    intervals[j + 1] = value;
                }
                float cursor = min(max(0, _StartDistance), limit);
                float opticalLength = 0;
                [loop] for (int q = 0; q < count; q++)
                {
                    float start = max(cursor, intervals[q].x);
                    opticalLength += HeightIntegral(origin, direction, cursor, min(start, limit));
                    cursor = max(cursor, intervals[q].y);
                }
                opticalLength += HeightIntegral(origin, direction, min(cursor, limit), limit);
                return opticalLength;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                float depth = SampleSceneDepth(uv);
                #if UNITY_REVERSED_Z
                    bool sky = depth <= 1e-6;
                    float worldDepth = sky ? 1e-5 : depth;
                #else
                    bool sky = depth >= 0.999999;
                    float worldDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1, sky ? 0.99999 : depth);
                #endif
                float3 world = ComputeWorldSpacePosition(uv, worldDepth, UNITY_MATRIX_I_VP);
                float3 origin = GetCameraPositionWS();
                float3 delta = world - origin;
                float lengthToSurface = length(delta);
                float3 direction = delta / max(lengthToSurface, 1e-5);
                if (unity_OrthoParams.w > .5)
                {
                    direction = -UNITY_MATRIX_V[2].xyz;
                    #if UNITY_REVERSED_Z
                        float nearDepth = 1;
                    #else
                        float nearDepth = UNITY_NEAR_CLIP_VALUE;
                    #endif
                    origin = ComputeWorldSpacePosition(uv, nearDepth, UNITY_MATRIX_I_VP);
                    lengthToSurface = max(0, dot(world - origin, direction));
                }
                float limit = sky ? max(1, _MaxDistance) : min(lengthToSurface, max(1, _MaxDistance));
                float opacity = 1 - exp(-max(0, _Density) * OutdoorIntegral(origin, direction, limit));
                if (sky) opacity *= _SkyStrength * exp(-abs(direction.y) * _HorizonFalloff);
                opacity = min(saturate(opacity), _MaxOpacity);
                return half4(lerp(color.rgb, _FogColor.rgb, opacity), color.a);
            }
            ENDHLSL
        }
    }
}





