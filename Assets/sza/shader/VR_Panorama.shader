Shader "Custom/VR_Panorama"
{
    Properties
    {
        [MainTexture] _MainTex ("Panorama Texture (Equirectangular)", 2D) = "white" {}
    }
    SubShader
    {
        // 设置为不透明，并且在 URP 下渲染
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        // 🚨 核心魔法 1：剔除正面，渲染背面。这样相机在球体内部才能看到画面
        Cull Front

        Pass
        {
            Name "Unlit"
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
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                
                // 🚨 核心魔法 2：修复镜像问题
                // 因为我们在球体内部往外看，贴图默认是左右反的。这里将 U 坐标反转。
                output.uv = float2(1.0 - input.uv.x, input.uv.y);
                
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 采样贴图，全景图不需要受场景光照影响，直接输出原本的颜色即可
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                return color;
            }
            ENDHLSL
        }
    }
}