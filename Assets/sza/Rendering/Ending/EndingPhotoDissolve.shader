Shader "Sza/Ending/Photo Dissolve"
{
    Properties
    {
        [MainTexture] _MainTex("照片",2D)="white"{}
        [MainColor] _DiffuseColor("照片颜色",Color)=(1,1,1,1)
        [HDR] _EdgeColor("溶解发光边缘",Color)=(1.5,0.6,0.12,1)
        _Progress("出现进度",Range(0,1))=0
        _NoiseScale("噪声频率",Float)=12
        _NoiseStrength("噪声扰动",Range(0,1))=0.3
        _EdgeWidth("边缘宽度",Range(0.001,0.2))=0.035
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Pass
        {
            Name "Photo"
            Cull Off ZWrite On ZTest LEqual
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _DiffuseColor,_EdgeColor;
                float _Progress,_NoiseScale,_NoiseStrength,_EdgeWidth;
            CBUFFER_END
            struct Attributes {float4 positionOS:POSITION;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct Varyings {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;UNITY_VERTEX_OUTPUT_STEREO};
            // Same value-noise pattern as URP_DissolveBuildUp, in photo UV space.
            float Hash(float3 p){p=frac(p*0.3183099+float3(0.1,0.1,0.1));p*=17.0;return frac(p.x*p.y*p.z*(p.x+p.y+p.z));}
            float Noise(float3 x)
            {
                float3 i=floor(x),f=frac(x);f=f*f*(3.0-2.0*f);
                return lerp(lerp(lerp(Hash(i),Hash(i+float3(1,0,0)),f.x),lerp(Hash(i+float3(0,1,0)),Hash(i+float3(1,1,0)),f.x),f.y),
                    lerp(lerp(Hash(i+float3(0,0,1)),Hash(i+float3(1,0,1)),f.x),lerp(Hash(i+float3(0,1,1)),Hash(i+float3(1,1,1)),f.x),f.y),f.z);
            }
            Varyings Vert(Attributes v){UNITY_SETUP_INSTANCE_ID(v);Varyings o=(Varyings)0;UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.positionCS=TransformObjectToHClip(v.positionOS.xyz);o.uv=TRANSFORM_TEX(v.uv,_MainTex);return o;}
            half4 Frag(Varyings i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                clip(_Progress-0.00001);
                half4 photo=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv)*_DiffuseColor;
                if(_Progress<0.99999)
                {
                    float threshold=i.uv.y+Noise(float3(i.uv*_NoiseScale,0.35))*_NoiseStrength;
                    float edge=_Progress*(1.0+_NoiseStrength+_EdgeWidth)-threshold;
                    clip(edge);
                    photo.rgb+=_EdgeColor.rgb*pow(saturate(1.0-edge/max(_EdgeWidth,0.0001)),2.0);
                }
                return half4(photo.rgb,1);
            }
            ENDHLSL
        }
    }
}
