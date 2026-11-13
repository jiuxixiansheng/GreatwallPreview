Shader "SZA/Bow/MagicBow" {
    Properties {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1,1,1,1)
        [Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Scale", Float) = 1
        _MetallicGlossMap("Metallic (R) Smoothness (A)", 2D) = "black" {}
        [Toggle] _UseMetallicMap("Use Metallic/Smoothness Map", Float) = 1
        _Metallic("Metallic (Without Map)", Range(0,1)) = 0
        _Smoothness("Smoothness", Range(0,1)) = 1
        _OcclusionMap("Occlusion (G)", 2D) = "white" {}
        _OcclusionStrength("Occlusion Strength", Range(0,1)) = 1
        [Toggle] _AlphaClip("Base Alpha Clip", Float) = 0
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
        _Reveal("Reveal", Range(0,1)) = 1
        _DissolveMap("Dissolve Mask (R)", 2D) = "white" {}
        _NoiseScale("UV Noise Scale", Float) = 16
        _EdgeWidth("Dissolve Edge Width", Range(0.001,0.2)) = 0.04
        [HDR] _EdgeColor("Warm Edge", Color) = (4,0.8,0.05,1)
        _Highlight("Highlight", Range(0,1)) = 0
        [HDR] _HighlightColor("Highlight Color", Color) = (1,1,0,1)
        _HighlightIntensity("Highlight Intensity", Float) = 4
        _RimPower("Rim Power", Range(0.5,8)) = 3
        _BreathSpeed("Breathing Cycles Per Second", Float) = 0.8
    }
    SubShader {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull [_Cull]
        ZWrite On
        HLSLINCLUDE
        #pragma target 3.5
        #pragma multi_compile_instancing
        #define _NORMALMAP 1
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST, _DissolveMap_ST;
            half4 _BaseColor, _EdgeColor, _HighlightColor;
            float _NoiseScale, _BreathSpeed;
            half _BumpScale, _Metallic, _Smoothness, _UseMetallicMap, _OcclusionStrength;
            half _AlphaClip, _Cutoff, _Reveal, _EdgeWidth, _Highlight, _HighlightIntensity, _RimPower;
        CBUFFER_END
        TEXTURE2D(_MetallicGlossMap); SAMPLER(sampler_MetallicGlossMap);
        TEXTURE2D(_OcclusionMap); SAMPLER(sampler_OcclusionMap);
        TEXTURE2D(_DissolveMap); SAMPLER(sampler_DissolveMap);
        float3 _LightDirection, _LightPosition;
        struct Attributes {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 tangentOS : TANGENT;
            float2 uv : TEXCOORD0; float2 lightmapUV : TEXCOORD1;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            float3 positionWS : TEXCOORD1;
            half3 normalWS : TEXCOORD2;
            half4 tangentWS : TEXCOORD3;
            half4 fogAndVertexLight : TEXCOORD4;
            DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 5);
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };
        Varyings BowVertex(Attributes v) {
            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v, o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
            VertexNormalInputs n = GetVertexNormalInputs(v.normalOS, v.tangentOS);
            o.positionCS = p.positionCS; o.positionWS = p.positionWS;
            o.normalWS = n.normalWS; o.tangentWS = half4(n.tangentWS, v.tangentOS.w * GetOddNegativeScale());
            o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
            o.fogAndVertexLight = half4(ComputeFogFactor(p.positionCS.z), VertexLighting(p.positionWS, n.normalWS));
            OUTPUT_LIGHTMAP_UV(v.lightmapUV, unity_LightmapST, o.lightmapUV);
            OUTPUT_SH(o.normalWS, o.vertexSH);
            return o;
        }
        Varyings BowShadowVertex(Attributes v) {
            Varyings o = BowVertex(v);
            UNITY_SETUP_INSTANCE_ID(v);
            float3 lightDir = _LightDirection;
            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                lightDir = normalize(_LightPosition - o.positionWS);
            #endif
            o.positionCS = TransformWorldToHClip(ApplyShadowBias(o.positionWS, o.normalWS, lightDir));
            o.positionCS = ApplyShadowClamping(o.positionCS);
            return o;
        }
        float hash21(float2 p) { return frac(sin(dot(p, float2(127.1,311.7))) * 43758.5453); }
        float valueNoise(float2 uv) {
            float2 p = floor(uv), f = frac(uv); f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(hash21(p), hash21(p + float2(1,0)), f.x),
                        lerp(hash21(p + float2(0,1)), hash21(p + 1.0), f.x), f.y);
        }
        half BowClip(float2 uv) {
            half mask = SAMPLE_TEXTURE2D(_DissolveMap, sampler_DissolveMap, uv * _DissolveMap_ST.xy + _DissolveMap_ST.zw).r;
            float threshold = lerp(0.001, 0.999, saturate(mask * valueNoise(uv * _NoiseScale)));
            float distance = saturate(_Reveal) - threshold;
            clip(distance); // Strict endpoints: 0 clips everything; 1 retains everything.
            half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a * _BaseColor.a;
            clip(_AlphaClip > 0.5 ? alpha - _Cutoff : 1.0);
            return (1.0 - smoothstep(0.0, max(_EdgeWidth, 0.001), distance)) * (1.0 - step(1.0, _Reveal));
        }
        half3 BowNormal(Varyings i) {
            half3 normalTS = SampleNormal(i.uv, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), _BumpScale);
            half3 bitangent = i.tangentWS.w * cross(i.normalWS, i.tangentWS.xyz);
            return NormalizeNormalPerPixel(TransformTangentToWorld(normalTS, half3x3(i.tangentWS.xyz, bitangent, i.normalWS)));
        }
        half4 BowForward(Varyings i) : SV_Target {
            UNITY_SETUP_INSTANCE_ID(i); UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
            half edge = BowClip(i.uv);
            SurfaceData s = (SurfaceData)0;
            half4 ms = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, i.uv);
            s.albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
            s.metallic = lerp(_Metallic, ms.r, saturate(_UseMetallicMap));
            s.smoothness = _Smoothness * lerp(1.0, ms.a, saturate(_UseMetallicMap));
            s.occlusion = lerp(1.0, SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, i.uv).g, _OcclusionStrength);
            s.normalTS = half3(0,0,1); s.alpha = 1;
            InputData d = (InputData)0;
            d.positionWS = i.positionWS; d.normalWS = BowNormal(i);
            d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
            #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                d.shadowCoord = ComputeScreenPos(TransformWorldToHClip(i.positionWS));
            #else
                d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
            #endif
            d.bakedGI = SAMPLE_GI(i.lightmapUV, i.vertexSH, d.normalWS); d.shadowMask = SAMPLE_SHADOWMASK(i.lightmapUV);
            d.vertexLighting = i.fogAndVertexLight.yzw;
            d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
            half rim = pow(1.0 - saturate(dot(d.normalWS, d.viewDirectionWS)), _RimPower);
            half breath = 0.65 + 0.35 * sin(_Time.y * _BreathSpeed * 6.2831853);
            s.emission = _EdgeColor.rgb * edge + _HighlightColor.rgb * (saturate(_Highlight) * _HighlightIntensity * rim * breath);
            half4 color = UniversalFragmentPBR(d, s);
            color.rgb = MixFog(color.rgb, i.fogAndVertexLight.x);
            return color;
        }
        half4 BowDepth(Varyings i) : SV_Target {
            UNITY_SETUP_INSTANCE_ID(i); UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
            BowClip(i.uv); return i.positionCS.z;
        }
        half4 BowDepthNormals(Varyings i) : SV_Target {
            UNITY_SETUP_INSTANCE_ID(i); UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
            BowClip(i.uv);
            half3 n = BowNormal(i);
            #if defined(_GBUFFER_NORMALS_OCT)
                return half4(PackFloat2To888(saturate(PackNormalOctQuadEncode(n) * 0.5 + 0.5)), 0);
            #else
                return half4(n, 0);
            #endif
        }
        ENDHLSL
        Pass {
            Name "ForwardLit" Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma vertex BowVertex
            #pragma fragment BowForward
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fog
            ENDHLSL
        }
        Pass {
            Name "ShadowCaster" Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex BowShadowVertex
            #pragma fragment BowDepth
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            ENDHLSL
        }
        Pass {
            Name "DepthOnly" Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma vertex BowVertex
            #pragma fragment BowDepth
            ENDHLSL
        }
        Pass {
            Name "DepthNormals" Tags { "LightMode"="DepthNormalsOnly" }
            HLSLPROGRAM
            #pragma vertex BowVertex
            #pragma fragment BowDepthNormals
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
    }
    FallBack Off
}
