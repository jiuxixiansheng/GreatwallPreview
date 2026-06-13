Shader "Custom/URP_LitDissolveBuildUp"
{
    Properties
    {
        _MainTex ("基础纹理", 2D) = "white" {}
        _DiffuseColor ("整体主颜色", Color) = (1, 1, 1, 1)
        [HDR] _EdgeColor ("发光边缘颜色 (HDR)", Color) = (5, 1.5, 0, 1)
        _Progress ("出现进度", Range(0, 1)) = 0.0
        _MinHeight ("建筑底部世界高度 Y", Float) = 0.0
        _MaxHeight ("建筑顶部世界高度 Y", Float) = 10.0
        _EdgeWidth ("燃烧边缘宽度", Range(0.01, 1.0)) = 0.2
        _NoiseScale ("噪声频率", Float) = 5.0
        _NoiseStrength ("噪声扰动强度", Float) = 0.5
    }
    SubShader
    {
        // 标记为不透明物体，使用通用渲染管线
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 100
        Cull Off // 双面渲染

        // ========================================================
        // PASS 1: 基础光照与接收阴影 (ForwardLit)
        // ========================================================
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            // 关键：激活 URP 的主光源阴影和变体开关
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS    : TEXTCOORD2;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _DiffuseColor;
                half4 _EdgeColor;
                float _Progress;
                float _MinHeight;
                float _MaxHeight;
                float _EdgeWidth;
                float _NoiseScale;
                float _NoiseStrength;
            CBUFFER_END

            // 噪点函数
            float hash(float3 p)
            {
                p = frac(p * 0.3183099 + float3(0.1, 0.1, 0.1));
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }
            float noise(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(hash(i + float3(0,0,0)), hash(i + float3(1,0,0)), f.x),
                                 lerp(hash(i + float3(0,1,0)), hash(i + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(hash(i + float3(0,0,1)), hash(i + float3(1,0,1)), f.x),
                                 lerp(hash(i + float3(0,1,1)), hash(i + float3(1,1,1)), f.x), f.y), f.z);
            }

            Varyings vert (Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                // ---- 1. 核心高度裁剪逻辑 ----
                float currentCutHeight = lerp(_MinHeight, _MaxHeight, _Progress);
                float n = noise(input.positionWS * _NoiseScale) * _NoiseStrength;
                float noisyWorldY = input.positionWS.y + n;

                if (noisyWorldY > currentCutHeight)
                {
                    discard; // 未生成的像素直接丢弃
                }

                // ---- 2. 采样基础颜色与计算法线 ----
                half4 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * _DiffuseColor;
                float3 N = normalize(input.normalWS);

                // ---- 3. URP 核心物理光照计算 ----
                // 获取主光源信息（含阴影衰减因子 shadowAttenuation）
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                
                float3 L = normalize(mainLight.direction);
                float diffuseTerm = saturate(dot(N, L)); // 经典漫反射 N dot L
                
                // 结合光照颜色、主档阴影、环境光
                float3 lightColor = mainLight.color * (diffuseTerm * mainLight.shadowAttenuation);
                float3 ambientColor = SampleSH(N); // 采样场景环境球光照(SH)
                
                half4 finalColor = half4(albedo.rgb * (lightColor + ambientColor), albedo.a);

                // ---- 4. 边缘燃烧发光计算 ----
                float edgeDist = currentCutHeight - noisyWorldY;
                if (edgeDist < _EdgeWidth)
                {
                    float edgeGlowMask = 1.0 - (edgeDist / _EdgeWidth);
                    edgeGlowMask = pow(edgeGlowMask, 2.0);
                    finalColor.rgb += _EdgeColor.rgb * edgeGlowMask;
                }

                return finalColor;
            }
            ENDHLSL
        }
    }
}