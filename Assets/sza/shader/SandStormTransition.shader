Shader "Custom/SandStormTransition"
{
    Properties
    {
        _DustTex1 ("Large Dust", 2D) = "gray" {}
        _DustTex2 ("Fine Dust", 2D) = "gray" {}
        _DustTex3 ("Streak Dust", 2D) = "gray" {}

        _BaseSandColor ("Base Sand Color", Color) = (0.72, 0.43, 0.17, 1)
        _DarkSandColor ("Dark Sand Color", Color) = (0.42, 0.24, 0.10, 1)
        _LightSandColor ("Light Sand Color", Color) = (1.0, 0.72, 0.36, 1)

        _Fade ("Fade", Range(0, 1)) = 0
        _DetailStrength ("Detail Strength", Range(0, 1)) = 0.75
        _Contrast ("Contrast", Range(0.1, 4)) = 1.6
        _Distortion ("Distortion", Range(0, 0.05)) = 0.012

        _Layer1 ("Layer1 xy Speed, z Tiling, w Weight", Vector) = (0.04, 0.01, 1.2, 0.55)
        _Layer2 ("Layer2 xy Speed, z Tiling, w Weight", Vector) = (-0.08, 0.02, 3.2, 0.30)
        _Layer3 ("Layer3 xy Speed, z Tiling, w Weight", Vector) = (0.12, -0.01, 6.0, 0.20)
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }

        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "SandStorm"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D(_DustTex1); SAMPLER(sampler_DustTex1);
            TEXTURE2D(_DustTex2); SAMPLER(sampler_DustTex2);
            TEXTURE2D(_DustTex3); SAMPLER(sampler_DustTex3);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseSandColor;
                float4 _DarkSandColor;
                float4 _LightSandColor;

                float _Fade;
                float _DetailStrength;
                float _Contrast;
                float _Distortion;

                float4 _Layer1;
                float4 _Layer2;
                float4 _Layer3;
            CBUFFER_END

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float t = _Time.y;

                float n1 = SAMPLE_TEXTURE2D(_DustTex1, sampler_DustTex1, uv * _Layer1.z + t * _Layer1.xy).r;
                float n2 = SAMPLE_TEXTURE2D(_DustTex2, sampler_DustTex2, uv * _Layer2.z + t * _Layer2.xy).r;
                float n3 = SAMPLE_TEXTURE2D(_DustTex3, sampler_DustTex3, uv * _Layer3.z + t * _Layer3.xy).r;

                float dustNoise = n1 * _Layer1.w + n2 * _Layer2.w + n3 * _Layer3.w;
                dustNoise = saturate((dustNoise - 0.5) * _Contrast + 0.5);

                float2 distort = float2(n2 - 0.5, n3 - 0.5) * _Distortion * _Fade;
                float3 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + distort).rgb;

                float3 detailColor = lerp(_DarkSandColor.rgb, _LightSandColor.rgb, dustNoise);
                float3 stormColor = lerp(_BaseSandColor.rgb, detailColor, _DetailStrength);

                float3 result = lerp(scene, stormColor, saturate(_Fade));

                return half4(result, 1);
            }
            ENDHLSL
        }
    }
}