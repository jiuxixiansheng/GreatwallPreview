Shader "Sza/Telescope Video Mask"
{
    Properties
    {
        [MainTexture] _MainTex ("Video", 2D) = "black" {}
        _Aspect ("Mask Aspect", Float) = 1.6
        _VideoAspect ("Video Aspect", Float) = 1
        _Radius ("Lens Radius", Range(0.2, 0.5)) = 0.46
        _Softness ("Lens Edge", Range(0.001, 0.05)) = 0.012
        _MaskEnabled ("Use Lens Mask", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Overlay" "RenderType"="Transparent" }
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; UNITY_VERTEX_OUTPUT_STEREO };
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
            float _Aspect, _VideoAspect, _Radius, _Softness, _MaskEnabled;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                if (_MaskEnabled < 0.5) return input.color;
                float2 p=(input.uv-0.5)*float2(_Aspect,1);
                // Union of two overlapping circles: one continuous binocular opening.
                float2 eyeOffset=float2(_Radius*0.6,0);
                float d=min(length(p-eyeOffset),length(p+eyeOffset));
                float lens=1-smoothstep(_Radius-_Softness,_Radius,d);
                float vignette=1-0.32*smoothstep(_Radius*0.5,_Radius,d);
                // Fill the wider mask without stretching or duplicating the video.
                float2 videoUV=input.uv-0.5;
                if (_Aspect > _VideoAspect) videoUV.y *= _VideoAspect / _Aspect;
                else videoUV.x *= _Aspect / _VideoAspect;
                half3 video=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,videoUV+0.5).rgb;
                float rim=smoothstep(_Radius-0.022,_Radius-0.014,d)*(1-smoothstep(_Radius-0.008,_Radius,d));
                return half4(video*lens*vignette+half3(0.16,0.12,0.055)*rim,1)*input.color;
            }
            ENDHLSL
        }
    }
}
