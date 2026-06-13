Shader "Custom/URP_WorldSpaceGridCage"
{
    Properties
    {
        [HDR] _GridColor ("网格发光颜色 (HDR)", Color) = (0, 1, 0.8, 1)
        _LineWidth ("线宽 (米)", Range(0.005, 0.1)) = 0.02
        _GridSmoothness ("线条边缘平滑度", Range(0.001, 0.05)) = 0.005
    }
    SubShader
    {
        // 渲染队列设置为透明，关闭深度写入
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        LOD 100
        ZWrite Off
        Cull Off // 双面渲染，保证玩家在笼子内部和外部都能看到网格
        Blend SrcAlpha OneMinusSrcAlpha // 标准透明混合

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _GridColor;
                float _LineWidth;
                float _GridSmoothness;
            CBUFFER_END

            Varyings vert (Attributes input)
            {
                Varyings output;
                // 转换坐标到世界空间和裁剪空间
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                // 转换法线到世界空间
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                float3 worldPos = input.positionWS;
                // 取世界空间绝对法线并归一化
                float3 absNormal = normalize(abs(input.normalWS));

                // ========================================================
                // 1. 世界空间网格核心数学
                // ========================================================
                // frac(worldPos) 让坐标每隔1米在 0~1 之间循环
                // 0.5 - abs(frac(...) - 0.5) 计算出当前像素点距离最近的整数米(网格线)的绝对物理距离(米)
                float3 distToGrid = 0.5 - abs(frac(worldPos) - 0.5);

                // 使用 smoothstep 做抗锯齿线条剪裁
                // 当距离小于线宽时亮起，大于线宽时暗下
                float3 axisLines = smoothstep(_LineWidth + _GridSmoothness, _LineWidth, distToGrid);

                // ========================================================
                // 2. 避免“全屏变色”的法线遮罩（三平面混合思想）
                // ========================================================
                // 如果一个面垂直于X轴，那么这个面上只有 Y 和 Z 的朝向线构成正方形网格，必须剔除X轴自身的线条线
                float gridX = max(axisLines.y, axisLines.z); // X面的网格由Y、Z线构成
                float gridY = max(axisLines.x, axisLines.z); // Y面的网格由X、Z线构成
                float gridZ = max(axisLines.x, axisLines.y); // Z面的网格由X、Y线构成

                // 使用法线的平方作为权重进行混合，保证各面平滑过渡（支持倒角盒子）
                float3 weights = absNormal * absNormal;
                weights /= (weights.x + weights.y + weights.z + 0.0001); // 权重归一化

                float finalGridMask = gridX * weights.x + gridY * weights.y + gridZ * weights.z;

                // ========================================================
                // 3. 输出与透明度剪裁
                // ========================================================
                // 镂空部分完全透明：直接将网格遮罩作为 Alpha 赋给材质
                half4 finalColor = _GridColor;
                finalColor.a *= finalGridMask;

                // 如果完全透明则直接丢弃片元，优化显卡 Overdraw 压力
                if (finalColor.a < 0.01)
                    discard;

                return finalColor;
            }
            ENDHLSL
        }
    }
}