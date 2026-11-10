Shader "sza/VolumetricFog"
{
    Properties
    {
        _Color("Color", Color) = (1, 1, 1, 1)
        _MaxDistance("Max distance", float) = 100
        _StepSize("Step size", Range(0.1, 20)) = 1
        _DensityMultiplier("Density multiplier", Range(0, 10)) = 1
        _NoiseOffset("Noise offset", float) = 0

        _FogNoise("Fog noise", 3D) = "white" {}
        _NoiseTiling("Noise tiling", float) = 1
        _DensityThreshold("Density threshold", Range(0, 1)) = 0.1

        [HDR]_LightContribution("Light contribution", Color) = (1, 1, 1, 1)
        _LightScattering("Light scattering", Range(0, 1)) = 0.2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "VolumetricFog"

            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float4 _Color;
            float _MaxDistance;
            float _DensityMultiplier;
            float _StepSize;
            float _NoiseOffset;

            TEXTURE3D(_FogNoise);

            float _DensityThreshold;
            float _NoiseTiling;
            float4 _LightContribution;
            float _LightScattering;

            float henyey_greenstein(float angle, float scattering)
            {
                return (1.0 - angle * angle) /
                    (4.0 * PI * pow(
                        1.0 + scattering * scattering - 2.0 * scattering * angle,
                        1.5));
            }

            float get_density(float3 worldPos)
            {
                float4 noise = _FogNoise.SampleLevel(
                    sampler_TrilinearRepeat,
                    worldPos * 0.01 * _NoiseTiling,
                    0);

                float density = dot(noise, noise);
                density = saturate(density - _DensityThreshold) * _DensityMultiplier;

                return density;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float4 col = SAMPLE_TEXTURE2D(
                    _BlitTexture,
                    sampler_LinearClamp,
                    IN.texcoord);

                float rawDepth = SampleSceneDepth(IN.texcoord);
                float sceneDepth01 = Linear01Depth(rawDepth, _ZBufferParams);

                // 天空盒/无深度区域在线性深度里接近 1。
                // 只跳过这些像素，避免把正常场景物体误判成天空。
                if (sceneDepth01 >= 0.9999)
                    return col;

                float3 worldPos = ComputeWorldSpacePosition(
                    IN.texcoord,
                    rawDepth,
                    UNITY_MATRIX_I_VP);

                float3 entryPoint = _WorldSpaceCameraPos;
                float3 viewDir = worldPos - _WorldSpaceCameraPos;
                float viewLength = length(viewDir);
                float3 rayDir = normalize(viewDir);

                float2 pixelCoords = IN.texcoord * _BlitTexture_TexelSize.zw;
                float distLimit = min(viewLength, _MaxDistance);

                float frameNoise = InterleavedGradientNoise(
                    pixelCoords,
                    (int)(_Time.y / max(HALF_EPS, unity_DeltaTime.x)));

                float distTravelled = frameNoise * _NoiseOffset;
                float transmittance = 1.0;
                float4 fogCol = _Color;

                while (distTravelled < distLimit)
                {
                    float3 rayPos = entryPoint + rayDir * distTravelled;
                    float density = get_density(rayPos);

                    if (density > 0.0)
                    {
                        Light mainLight = GetMainLight(
                            TransformWorldToShadowCoord(rayPos));

                        fogCol.rgb +=
                            mainLight.color.rgb *
                            _LightContribution.rgb *
                            henyey_greenstein(
                                dot(rayDir, mainLight.direction),
                                _LightScattering) *
                            density *
                            mainLight.shadowAttenuation *
                            _StepSize;

                        transmittance *= exp(-density * _StepSize);
                    }

                    distTravelled += _StepSize;
                }

                return lerp(col, fogCol, 1.0 - saturate(transmittance));
            }
            ENDHLSL
        }
    }
}