Shader "Xcy/Hand Toon"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (0.88, 0.94, 0.97, 1)
        _ShadowColor("Shadow Tint", Color) = (0.3, 0.43, 0.55, 1)
        _MidtoneColor("Midtone Tint", Color) = (0.68, 0.79, 0.85, 1)
        _ShadowThreshold("Shadow Threshold", Range(0, 0.9)) = 0.25
        _LightThreshold("Light Threshold", Range(0.1, 1)) = 0.65
        _BandSoftness("Band Softness", Range(0.001, 0.2)) = 0.025
        _AmbientStrength("Ambient Strength", Range(0, 2)) = 0.45
        _BrightnessFloor("Minimum Brightness", Range(0, 1)) = 0.25
        _LightIntensityLimit("Light Intensity Limit", Range(0, 1)) = 0.8
        _MaxBrightness("Maximum Brightness", Range(0.1, 1)) = 0.85
        _RimColor("Rim Color", Color) = (0.65, 0.88, 0.96, 1)
        _RimStrength("Rim Strength", Range(0, 1)) = 0.15
        _RimPower("Rim Power", Range(1, 10)) = 4
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor, _ShadowColor, _MidtoneColor, _RimColor;
            float _ShadowThreshold, _LightThreshold, _BandSoftness;
            float _AmbientStrength, _BrightnessFloor, _RimStrength, _RimPower, _Cull;
            float _LightIntensityLimit, _MaxBrightness;
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
            float2 uv : TEXCOORD2;
            half fog : TEXCOORD3;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };
        Varyings Vert(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
            output.positionCS = position.positionCS;
            output.positionWS = position.positionWS;
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            output.fog = ComputeFogFactor(position.positionCS.z);
            return output;
        }
        half3 LimitBrightness(half3 color, half limit)
        {
            half peak = max(max(color.r, color.g), color.b);
            return color * min(1.0h, limit / max(peak, 0.0001h));
        }
        half WhiteLightBrightness(half3 color, half limit)
        {
            return min(dot(max(color, half3(0, 0, 0)), half3(0.2126h, 0.7152h, 0.0722h)), limit);
        }
        half3 ToonLight(Light light, half3 normal, float softness)
        {
            half ndl = saturate(dot(normal, light.direction));
            half shade = ndl * light.shadowAttenuation;
            float low = min(_ShadowThreshold, _LightThreshold - 0.01);
            float high = max(_LightThreshold, low + 0.01);
            half mid = smoothstep(low - softness, low + softness, shade);
            half bright = smoothstep(high - softness, high + softness, shade);
            half3 tint = lerp(_ShadowColor.rgb, _MidtoneColor.rgb, mid);
            tint = lerp(tint, half3(1, 1, 1), bright);
            // Keep luminance and attenuation, but discard the light's hue.
            half brightness = WhiteLightBrightness(light.color * light.distanceAttenuation, _LightIntensityLimit);
            return tint * brightness;
        }
        half4 Frag(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            half3 normal = normalize(input.normalWS);
            float softness = max(_BandSoftness, length(fwidth(normal)) * 0.5);
            half3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
            half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;
            #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                float4 shadowCoord = float4(GetNormalizedScreenSpaceUV(input.positionCS), 0, 1);
            #else
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
            #endif
            Light mainLight = GetMainLight(shadowCoord);
            half3 lighting = ToonLight(mainLight, normal, softness);
            #if defined(_ADDITIONAL_LIGHTS) || USE_FORWARD_PLUS
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                #if USE_FORWARD_PLUS
                    UNITY_LOOP for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); ++lightIndex)
                    {
                        FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK
                        lighting += ToonLight(GetAdditionalLight(lightIndex, input.positionWS), normal, softness);
                    }
                #endif
                uint lightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lightCount)
                    lighting += ToonLight(GetAdditionalLight(lightIndex, input.positionWS), normal, softness);
                LIGHT_LOOP_END
            #elif defined(_ADDITIONAL_LIGHTS_VERTEX)
                lighting += WhiteLightBrightness(VertexLighting(input.positionWS, normal), _LightIntensityLimit);
            #endif
            lighting += WhiteLightBrightness(SampleSH(normal) * _AmbientStrength, _LightIntensityLimit * 0.25h);
            lighting = max(lighting, _ShadowColor.rgb * _BrightnessFloor);
            half rim = pow(saturate(1 - dot(normal, view)), _RimPower);
            half3 color = albedo * lighting + _RimColor.rgb * rim * _RimStrength;
            color = LimitBrightness(color, _MaxBrightness);
            return half4(MixFog(color, input.fog), 1);
        }
        half4 DepthFrag(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            return 0;
        }
        ENDHLSL
        Pass
        {
            Name "HandToonForward"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull [_Cull] ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            Cull [_Cull] ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection;
            float3 _LightPosition;
            Varyings ShadowVert(Attributes input)
            {
                Varyings output = Vert(input);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 direction = normalize(_LightPosition - output.positionWS);
                #else
                    float3 direction = _LightDirection;
                #endif
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(output.positionWS, normalize(output.normalWS), direction));
                #if UNITY_REVERSED_Z
                    output.positionCS.z = min(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    output.positionCS.z = max(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif
                return output;
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            Cull [_Cull] ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            #pragma multi_compile _ STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            Cull [_Cull] ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment NormalFrag
            #pragma multi_compile_instancing
            #pragma multi_compile _ STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 NormalFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 normal = normalize(input.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 oct = PackNormalOctQuadEncode(normal);
                    return half4(PackFloat2To888(saturate(oct * 0.5 + 0.5)), 0);
                #else
                    return half4(normal, 0);
                #endif
            }
            ENDHLSL
        }
    }
    FallBack Off
}
