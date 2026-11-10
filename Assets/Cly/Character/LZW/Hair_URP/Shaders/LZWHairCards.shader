Shader "Cly/LZW/Hair Cards URP"
{
    Properties
    {
        [MainTexture] _BaseMap("Hair Color + Opacity",2D)="white"{}
        [MainColor] _BaseColor("Tint",Color)=(1,1,1,1)
        _FlowMap("Strand Direction + Root (linear)",2D)="gray"{}
        [Toggle] _UseSeedColor("Use Strand Seed Colors",Float)=0
        _PigmentMap("Strand Selection (R) + Shading (G)",2D)="gray"{}
        _WhiteAmount("White Hair Amount",Range(0,1))=.85
        _BlackLevel("Black Hair Brightness",Range(.001,.08))=.014
        [HideInInspector] _WhiteBoost("Legacy White Boost",Range(0,1))=0
        _WhiteLevel("White Hair Brightness",Range(.01,.8))=.48
        _WhiteTint("White Hair Tint",Color)=(1,1,1,1)
        _Cutoff("Alpha Cutoff",Range(.05,.9))=.28
        _Roughness("Roughness",Range(.1,.9))=.4
        _Anisotropy("Strand Highlight Strength",Range(0,1))=.25
        _Transmission("Back Light",Range(0,.5))=.1
        _SurfaceOffset("Surface Offset (metres)",Range(-.01,.01))=0
        _DetailNormalMap("Hair Surface Detail (linear RGB)",2D)="bump"{}
        _DetailNormalStrength("Hair Surface Detail Strength",Range(0,2))=0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Off
        HLSLINCLUDE
        #define _SPECULAR_SETUP 1
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Cutoff,_Roughness,_Anisotropy,_Transmission;
            float _SurfaceOffset;
            half _WhiteBoost,_WhiteLevel;
            half4 _WhiteTint;
            half _UseSeedColor,_WhiteAmount,_BlackLevel;
            half _DetailNormalStrength;
        CBUFFER_END
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_FlowMap); SAMPLER(sampler_FlowMap);
        TEXTURE2D(_PigmentMap); SAMPLER(sampler_PigmentMap);
        TEXTURE2D(_DetailNormalMap); SAMPLER(sampler_DetailNormalMap);
        struct Attributes
        {
            float4 positionOS:POSITION; float3 normalOS:NORMAL; float4 tangentOS:TANGENT; float2 uv:TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0;
            half3 normalWS:TEXCOORD1; half4 tangentWS:TEXCOORD2; float2 uv:TEXCOORD3; half fog:TEXCOORD4;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };
        Varyings Vert(Attributes v)
        {
            Varyings o=(Varyings)0;
            UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v,o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            VertexPositionInputs p=GetVertexPositionInputs(v.positionOS.xyz);
            VertexNormalInputs n=GetVertexNormalInputs(v.normalOS,v.tangentOS);
            p.positionWS+=n.normalWS*_SurfaceOffset;
            p.positionCS=TransformWorldToHClip(p.positionWS);
            o.positionCS=p.positionCS; o.positionWS=p.positionWS; o.normalWS=n.normalWS;
            o.tangentWS=half4(n.tangentWS,v.tangentOS.w*GetOddNegativeScale());
            o.uv=TRANSFORM_TEX(v.uv,_BaseMap); o.fog=ComputeFogFactor(p.positionCS.z); return o;
        }
        half4 HairSample(float2 uv)
        {
            half4 sample=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,uv)*_BaseColor;
            clip(sample.a-_Cutoff); return sample;
        }
        half3 HairLight(Light light,half3 n,half3 strand,half3 view,half3 albedo)
        {
            half3 h=SafeNormalize(light.direction+view);
            half3 t0=normalize(strand+n*.12);
            half3 t1=normalize(strand-n*.22);
            half th0=dot(t0,h), th1=dot(t1,h);
            half spec0=pow(sqrt(saturate(1-th0*th0)),lerp(100,12,_Roughness));
            half spec1=pow(sqrt(saturate(1-th1*th1)),lerp(40,6,_Roughness));
            half facing=saturate(dot(n,light.direction)*.5+.5);
            half3 glint=(spec0*.10+spec1*.04)*_Anisotropy*facing;
            half back=pow(saturate(-dot(n,light.direction)),2)*_Transmission;
            return light.color*light.distanceAttenuation*light.shadowAttenuation*(glint+albedo*back);
        }
        half4 Frag(Varyings i, FRONT_FACE_TYPE face:FRONT_FACE_SEMANTIC):SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(i); UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
            half4 sample=HairSample(i.uv);
            half3 n=normalize(i.normalWS)*IS_FRONT_VFACE(face,1,-1);
            half3 t=normalize(i.tangentWS.xyz);
            half3 b=cross(normalize(i.normalWS),t)*i.tangentWS.w;
            if(_DetailNormalStrength>.001h)
            {
                half3 detail=SAMPLE_TEXTURE2D(_DetailNormalMap,sampler_DetailNormalMap,i.uv).xyz*2-1;
                detail.xy*=_DetailNormalStrength;
                detail=SafeNormalize(detail);
                n=SafeNormalize(t*detail.x+b*detail.y+normalize(i.normalWS)*detail.z)*IS_FRONT_VFACE(face,1,-1);
            }
            half4 flowSample=SAMPLE_TEXTURE2D(_FlowMap,sampler_FlowMap,i.uv);
            half3 flow=flowSample.xyz*2-1;
            // Lift dark fibres toward silver while retaining root shading and the
            // original bright fibres. Zero preserves the baked atlas exactly.
            half rootShade=.8h+.2h*sqrt(saturate(flowSample.a));
            half3 silver=_WhiteTint.rgb*_WhiteLevel*rootShade*_BaseColor.rgb;
            sample.rgb=lerp(sample.rgb,max(sample.rgb,silver),_WhiteBoost);
            if(_UseSeedColor>.5h)
            {
                half2 pigment=SAMPLE_TEXTURE2D(_PigmentMap,sampler_PigmentMap,i.uv).rg;
                half edge=max(fwidth(pigment.r)*.5h,.002h);
                half white=smoothstep(pigment.r-edge,pigment.r+edge,_WhiteAmount);
                if(_WhiteAmount<=0) white=0;
                if(_WhiteAmount>=1) white=1;
                half3 black=half3(1,.86h,.72h)*_BlackLevel;
                sample.rgb=lerp(black,_WhiteTint.rgb*_WhiteLevel,white)*pigment.g*_BaseColor.rgb;
            }
            half3 strand=SafeNormalize(t*flow.x+b*flow.y+normalize(i.normalWS)*flow.z);
            SurfaceData s=(SurfaceData)0;
            s.albedo=sample.rgb; s.specular=half3(.025,.025,.025); s.smoothness=1-_Roughness;
            s.normalTS=half3(0,0,1); s.occlusion=1; s.alpha=sample.a;
            InputData inputData=(InputData)0;
            inputData.positionWS=i.positionWS; inputData.positionCS=i.positionCS;
            inputData.normalWS=n; inputData.viewDirectionWS=GetWorldSpaceNormalizeViewDir(i.positionWS);
            inputData.shadowCoord=TransformWorldToShadowCoord(i.positionWS);
            inputData.bakedGI=SampleSH(n); inputData.vertexLighting=VertexLighting(i.positionWS,n);
            inputData.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS);
            inputData.shadowMask=half4(1,1,1,1);
            s.emission=HairLight(GetMainLight(inputData.shadowCoord),n,strand,inputData.viewDirectionWS,s.albedo);
            #if defined(_ADDITIONAL_LIGHTS)
                uint count=GetAdditionalLightsCount();
                #if USE_FORWARD_PLUS
                UNITY_LOOP for(uint lightIndex=0;lightIndex<min(URP_FP_DIRECTIONAL_LIGHTS_COUNT,MAX_VISIBLE_LIGHTS);lightIndex++)
                {
                    FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK
                    Light light=GetAdditionalLight(lightIndex,i.positionWS,inputData.shadowMask);
                    s.emission+=HairLight(light,n,strand,inputData.viewDirectionWS,s.albedo);
                }
                #endif
                LIGHT_LOOP_BEGIN(count)
                    Light light=GetAdditionalLight(lightIndex,i.positionWS,inputData.shadowMask);
                    s.emission+=HairLight(light,n,strand,inputData.viewDirectionWS,s.albedo);
                LIGHT_LOOP_END
            #endif
            half4 result=UniversalFragmentPBR(inputData,s);
            result.rgb=MixFog(result.rgb,i.fog); result.a=sample.a; return result;
        }
        ENDHLSL
        Pass
        {
            Name "ForwardLit" Tags {"LightMode"="UniversalForwardOnly"}
            ZWrite On AlphaToMask On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
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
            Name "ShadowCaster" Tags {"LightMode"="ShadowCaster"}
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightPosition; float3 _LightDirection;
            Varyings ShadowVert(Attributes v)
            {
                Varyings o=Vert(v);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirection=normalize(_LightPosition-o.positionWS);
                #else
                    float3 lightDirection=_LightDirection;
                #endif
                o.positionCS=TransformWorldToHClip(ApplyShadowBias(o.positionWS,o.normalWS,lightDirection));
                #if UNITY_REVERSED_Z
                    o.positionCS.z=min(o.positionCS.z,UNITY_NEAR_CLIP_VALUE*o.positionCS.w);
                #else
                    o.positionCS.z=max(o.positionCS.z,UNITY_NEAR_CLIP_VALUE*o.positionCS.w);
                #endif
                return o;
            }
            half4 DepthFrag(Varyings i):SV_Target { HairSample(i.uv); return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly" Tags {"LightMode"="DepthOnly"}
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            half4 DepthFrag(Varyings i):SV_Target { HairSample(i.uv); return i.positionCS.z; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals" Tags {"LightMode"="DepthNormalsOnly"}
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment NormalsFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 NormalsFrag(Varyings i,FRONT_FACE_TYPE face:FRONT_FACE_SEMANTIC):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i); UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i); HairSample(i.uv);
                half3 n=normalize(i.normalWS)*IS_FRONT_VFACE(face,1,-1);
                #if defined(_GBUFFER_NORMALS_OCT)
                    return half4(PackFloat2To888(saturate(PackNormalOctQuadEncode(n)*.5+.5)),0);
                #else
                    return half4(n,0);
                #endif
            }
            ENDHLSL
        }
    }
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
