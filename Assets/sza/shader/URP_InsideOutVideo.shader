Shader "Custom/URP_InsideOutVideo"
{
    Properties
    {
        _MainTex ("Video Texture", 2D) = "white" {}
        [HDR] _ColorTint ("Color Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        // 【核心】剔除正面，渲染背面。这样摄像机在球体内部才能看到画面
        Cull Front 
        ZWrite On

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _ColorTint;
            CBUFFER_END

            Varyings vert (Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                
                // 【核心】水平翻转 UV，修正内部观看时的镜像问题
                output.uv = input.uv;
                output.uv.x = 1.0 - output.uv.x; 
                
                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                // 采样视频贴图并乘以颜色修正
                half4 videoColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * _ColorTint;
                return videoColor;
            }
            ENDHLSL
        }
    }
}