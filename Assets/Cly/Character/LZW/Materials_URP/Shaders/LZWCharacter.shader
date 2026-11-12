Shader "Cly/LZW/Character URP"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Color", 2D) = "white" {}
        [HDR] [MainColor] _BaseColor("Tint", Color) = (1,1,1,1)
        [Normal] _BumpMap("Normal (Unity convention)", 2D) = "bump" {}
        _BumpScale("Normal Strength", Range(0,2)) = 1
        _RoughnessMap("Roughness (R)", 2D) = "white" {}
        _Roughness("Roughness Multiplier", Range(0.05,1)) = 0.5
        _SpecColor("Specular F0", Color) = (0.028,0.028,0.028,1)
        _SkinWrap("Skin Light Wrap", Range(0,0.5)) = 0.12
        _Transmission("Skin Backlight", Range(0,0.5)) = 0.06
        _TransmissionColor("Backlight Tint", Color) = (1,0.25,0.12,1)
        [Toggle(_LZW_EYE)] _EyeMode("Eye Mode", Float) = 0
        _IrisMap("Iris Color", 2D) = "white" {}
        [Normal] _IrisNormalMap("Iris Normal", 2D) = "bump" {}
        _IrisColor("Iris Tint", Color) = (0.55,0.45,0.32,1)
        _IrisRadius("Iris UV Radius", Range(0.06,0.4)) = 0.16
        _IrisCenter("Iris UV Center", Vector) = (0.5,0.5,0,0)
        [HideInInspector] _SzaPortalClipEnabled("Portal Clip", Float) = 0
        [HideInInspector] _SzaPortalPlaneWS("Portal Plane", Vector) = (0,0,1,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 300
        HLSLINCLUDE
        #define _SPECULAR_SETUP 1
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor, _SpecColor, _TransmissionColor, _IrisColor;
            float4 _IrisCenter;
            float4 _SzaPortalPlaneWS;
            float _SzaPortalClipEnabled;
            half _BumpScale, _Roughness, _SkinWrap, _Transmission, _IrisRadius, _EyeMode;
        CBUFFER_END
        #include "Assets/sza/Rendering/Portal/CharacterPortalClip.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
        TEXTURE2D(_RoughnessMap); SAMPLER(sampler_RoughnessMap);
        TEXTURE2D(_IrisMap); SAMPLER(sampler_IrisMap);
        TEXTURE2D(_IrisNormalMap); SAMPLER(sampler_IrisNormalMap);
        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 tangentOS : TANGENT;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            half3 normalWS : TEXCOORD1;
            half4 tangentWS : TEXCOORD2;
            float2 uv : TEXCOORD3;
            half fog : TEXCOORD4;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };
        Varyings Vert(Attributes v)
        {
            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_TRANSFER_INSTANCE_ID(v,o);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
            VertexNormalInputs n = GetVertexNormalInputs(v.normalOS,v.tangentOS);
            o.positionCS = p.positionCS;
            o.positionWS = p.positionWS;
            o.normalWS = n.normalWS;
            o.tangentWS = half4(n.tangentWS, v.tangentOS.w * GetOddNegativeScale());
            o.uv = TRANSFORM_TEX(v.uv,_BaseMap);
            o.fog = ComputeFogFactor(p.positionCS.z);
            return o;
        }
        half3 NormalTS(float2 uv)
        {
            half3 n = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,uv),_BumpScale);
            #if defined(_LZW_EYE)
                float2 irisUV = (uv-_IrisCenter.xy) / (2.0*_IrisRadius) + 0.5;
                half irisMask = 1-smoothstep(_IrisRadius-0.008,_IrisRadius,length(uv-_IrisCenter.xy));
                half3 irisN = UnpackNormalScale(SAMPLE_TEXTURE2D(_IrisNormalMap,sampler_IrisNormalMap,saturate(irisUV)),0.3);
                n = normalize(lerp(n,irisN,irisMask));
            #endif
            return n;
        }
        half3 NormalWS(Varyings i)
        {
            half3 n = normalize(i.normalWS);
            half3 t = normalize(i.tangentWS.xyz);
            half3 b = cross(n,t)*i.tangentWS.w;
            return NormalizeNormalPerPixel(TransformTangentToWorld(NormalTS(i.uv),half3x3(t,b,n)));
        }
        half3 SkinLight(Light light, half3 n, half3 albedo)
        {
            half nl = dot(n,light.direction);
            half wrap = max(0,(nl+_SkinWrap)/(1+_SkinWrap))-saturate(nl);
            half back = pow(saturate(-nl),3)*_Transmission;
            return albedo * light.color * light.distanceAttenuation * light.shadowAttenuation *
                   (wrap*0.32 + back*_TransmissionColor.rgb);
        }
        half4 Frag(Varyings i) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(i);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
            SzaClipAtPortal(i.positionWS);
            SurfaceData s = (SurfaceData)0;
            s.albedo = SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb*_BaseColor.rgb;
            #if defined(_LZW_EYE)
                float2 irisUV = (i.uv-_IrisCenter.xy)/(2.0*_IrisRadius)+0.5;
                half irisMask = 1-smoothstep(_IrisRadius-0.008,_IrisRadius,length(i.uv-_IrisCenter.xy));
                half3 iris = SAMPLE_TEXTURE2D(_IrisMap,sampler_IrisMap,saturate(irisUV)).rgb*_IrisColor.rgb;
                s.albedo = lerp(s.albedo,iris,irisMask);
            #endif
            s.specular = _SpecColor.rgb;
            s.smoothness = 1-clamp(SAMPLE_TEXTURE2D(_RoughnessMap,sampler_RoughnessMap,i.uv).r*_Roughness,0.04,1);
            s.normalTS = half3(0,0,1);
            s.occlusion = 1;
            s.alpha = 1;
            InputData inputData = (InputData)0;
            inputData.positionWS = i.positionWS;
            inputData.positionCS = i.positionCS;
            inputData.normalWS = NormalWS(i);
            inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
            inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
            inputData.bakedGI = SampleSH(inputData.normalWS);
            inputData.vertexLighting = VertexLighting(i.positionWS,inputData.normalWS);
            inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
            inputData.shadowMask = half4(1,1,1,1);
            Light mainLight = GetMainLight(inputData.shadowCoord);
            s.emission = SkinLight(mainLight,inputData.normalWS,s.albedo);
            #if defined(_ADDITIONAL_LIGHTS)
                uint count = GetAdditionalLightsCount();
                #if USE_FORWARD_PLUS
                UNITY_LOOP for (uint lightIndex=0;lightIndex<min(URP_FP_DIRECTIONAL_LIGHTS_COUNT,MAX_VISIBLE_LIGHTS);lightIndex++)
                {
                    FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK
                    Light light = GetAdditionalLight(lightIndex,i.positionWS,inputData.shadowMask);
                    s.emission += SkinLight(light,inputData.normalWS,s.albedo);
                }
                #endif
                LIGHT_LOOP_BEGIN(count)
                    Light light = GetAdditionalLight(lightIndex,i.positionWS,inputData.shadowMask);
                    s.emission += SkinLight(light,inputData.normalWS,s.albedo);
                LIGHT_LOOP_END
            #endif
            half4 result = UniversalFragmentPBR(inputData,s);
            result.rgb = MixFog(result.rgb,i.fog);
            return result;
        }
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Back ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local _LZW_EYE
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection;
            float3 _LightPosition;
            Varyings ShadowVert(Attributes v)
            {
                Varyings o = Vert(v);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirection = normalize(_LightPosition-o.positionWS);
                #else
                    float3 lightDirection = _LightDirection;
                #endif
                o.positionCS = TransformWorldToHClip(ApplyShadowBias(o.positionWS,o.normalWS,lightDirection));
                #if UNITY_REVERSED_Z
                    o.positionCS.z = min(o.positionCS.z,UNITY_NEAR_CLIP_VALUE*o.positionCS.w);
                #else
                    o.positionCS.z = max(o.positionCS.z,UNITY_NEAR_CLIP_VALUE*o.positionCS.w);
                #endif
                return o;
            }
            half4 DepthFrag(Varyings i) : SV_Target { SzaClipAtPortal(i.positionWS); return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            half4 DepthFrag(Varyings i) : SV_Target { SzaClipAtPortal(i.positionWS); return i.positionCS.z; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _LZW_EYE
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 DepthNormalsFrag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                SzaClipAtPortal(i.positionWS);
                half3 n = NormalWS(i);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 oct = PackNormalOctQuadEncode(n);
                    return half4(PackFloat2To888(saturate(oct*0.5+0.5)),0);
                #else
                    return half4(n,0);
                #endif
            }
            ENDHLSL
        }
    }
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
