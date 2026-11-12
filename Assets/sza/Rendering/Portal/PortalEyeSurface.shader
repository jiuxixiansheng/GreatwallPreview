Shader "Sza/Portal/Eye Surface URP"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1,1,1,0.015)
        _Smoothness("Smoothness", Range(0,1)) = 0.95
        _Metallic("Metallic", Range(0,1)) = 0
        [HideInInspector] _SzaPortalClipEnabled("Portal Clip", Float) = 0
        [HideInInspector] _SzaPortalPlaneWS("Portal Plane", Vector) = (0,0,1,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend One OneMinusSrcAlpha
            ZWrite Off Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #define _SURFACE_TYPE_TRANSPARENT 1
            #define _ALPHAPREMULTIPLY_ON 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST, _SzaPortalPlaneWS;
                half4 _BaseColor;
                half _Smoothness, _Metallic;
                float _SzaPortalClipEnabled;
            CBUFFER_END
            #include "Assets/sza/Rendering/Portal/CharacterPortalClip.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; float2 uv:TEXCOORD2; half fog:TEXCOORD3; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings Vert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                Varyings o=(Varyings)0; UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p=GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS=p.positionCS; o.positionWS=p.positionWS; o.normalWS=TransformObjectToWorldNormal(v.normalOS);
                o.uv=TRANSFORM_TEX(v.uv,_BaseMap); o.fog=ComputeFogFactor(p.positionCS.z); return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                SzaClipAtPortal(i.positionWS);
                half4 color=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv)*_BaseColor;
                SurfaceData s=(SurfaceData)0;
                s.albedo=color.rgb; s.alpha=color.a; s.smoothness=_Smoothness; s.metallic=_Metallic; s.occlusion=1;
                InputData d=(InputData)0;
                d.positionWS=i.positionWS; d.positionCS=i.positionCS; d.normalWS=NormalizeNormalPerPixel(i.normalWS);
                d.viewDirectionWS=GetWorldSpaceNormalizeViewDir(i.positionWS); d.shadowCoord=TransformWorldToShadowCoord(i.positionWS);
                d.bakedGI=SampleSH(d.normalWS); d.shadowMask=1; d.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS);
                half4 result=UniversalFragmentPBR(d,s); result.rgb=MixFog(result.rgb,i.fog); return result;
            }
            ENDHLSL
        }
    }
}
