Shader "Sza/Ending/Panorama"
{
    Properties { _VideoTex("360 Video", 2D)="black"{} _YawDegrees("Horizontal Rotation", Float)=0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Background" }
        Pass
        {
            // The sphere must occupy depth so a later skybox cannot overwrite it.
            Cull Front ZWrite On ZTest LEqual
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_VideoTex); SAMPLER(sampler_VideoTex);
            CBUFFER_START(UnityPerMaterial)
                float _YawDegrees;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; float3 direction:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings Vert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                Varyings o=(Varyings)0; UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS=TransformObjectToHClip(v.positionOS.xyz); o.direction=v.positionOS.xyz; return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 d=normalize(i.direction);
                float2 uv=float2(atan2(d.x,d.z)*(0.5/PI)+0.5+_YawDegrees/360.0,asin(clamp(d.y,-1.0,1.0))/PI+0.5);
                return half4(SAMPLE_TEXTURE2D(_VideoTex,sampler_VideoTex,uv).rgb,1);
            }
            ENDHLSL
        }
    }
}
