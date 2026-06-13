Shader "Custom/VR_HolographicBorder"
{
    Properties
    {
        [HDR] _MainColor ("全息颜色 (HDR)", Color) = (0, 0.8, 1, 1)
        _FadePower ("向上衰减强度", Range(0.1, 10)) = 2.0
        _ScanSpeed ("扫描线速度", Range(0, 10)) = 2.0
        _ScanDensity ("扫描线密集度", Range(1, 50)) = 15.0
    }
    SubShader
    {
        // 透明渲染队列，关闭深度写入，开启 Additive 叠加发光混合
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        LOD 100
        ZWrite Off
        
        // 【关键】双面渲染：让玩家走进框内依然能看到光效边界
        Cull Off 
        
        Blend SrcAlpha One

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 objPos : TEXCOORD0; // 传递局部坐标到片元着色器
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _MainColor;
                float _FadePower;
                float _ScanSpeed;
                float _ScanDensity;
            CBUFFER_END

            Varyings vert (Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                // 记录顶点的模型空间坐标
                output.objPos = input.positionOS.xyz; 
                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                // ==========================================
                // 1. 高度衰减计算 (Bottom to Top)
                // ==========================================
                // Unity 默认 Cube 的 Y 轴局部坐标范围是 -0.5 到 0.5
                // 将其映射到 0 到 1 的范围 (0=底, 1=顶)
                float height01 = input.objPos.y + 0.5;
                
                // 翻转数值：我们要让底部是 1 (最亮)，顶部是 0 (最暗)
                float verticalMask = 1.0 - saturate(height01);
                
                // 使用指数 Power 控制光芒的“高度”。数值越大，光芒压得越低
                verticalMask = pow(verticalMask, _FadePower);

                // ==========================================
                // 2. 动态科幻扫描线
                // ==========================================
                // 利用高度和时间生成向上的波纹
                float scanline = sin(input.objPos.y * _ScanDensity - _Time.y * _ScanSpeed);
                
                // 映射到 0.6 ~ 1.0 之间，让波纹不至于黑死，保持底色发光
                scanline = scanline * 0.2 + 0.8; 

                // ==========================================
                // 3. 最终混合与输出
                // ==========================================
                // 将基础衰减、扫描线波动和 HDR 透明度相乘
                float finalAlpha = verticalMask * scanline * _MainColor.a;

                // 因为是 Additive 混合，颜色乘以 alpha 后直接返回即可
                return half4(_MainColor.rgb * finalAlpha, finalAlpha);
            }
            ENDHLSL
        }
    }
}