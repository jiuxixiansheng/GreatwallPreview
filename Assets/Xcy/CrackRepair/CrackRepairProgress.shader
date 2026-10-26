Shader "Hidden/Xcy/CrackRepairProgress"
{
    Properties
    {
        _MainTex ("Crack Image", 2D) = "white" {}
        _ShapeMask ("Crack Shape", 2D) = "black" {}
        _Progress ("Repair Progress", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            ZTest Always ZWrite Off Cull Off

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _ShapeMask;
            float _Progress;

            fixed4 frag(v2f_img input) : SV_Target
            {
                fixed4 crack = tex2D(_MainTex, input.uv);
                float shape = tex2D(_ShapeMask, input.uv).r;

                // The white core of 1_mask repairs first. Distance spreads the
                // front from the middle toward all outer branches and fragments.
                float2 fromCenter = (input.uv - float2(0.49, 0.53)) * float2(0.72, 1.0);
                float distanceOrder = saturate(length(fromCenter) / 0.60);
                float repairOrder = saturate(distanceOrder + (1.0 - shape) * 0.12);

                float visible = smoothstep(_Progress - 0.025, _Progress + 0.025, repairOrder);
                if (_Progress <= 0.001) visible = 1.0;
                if (_Progress >= 0.999) visible = 0.0;
                crack.a *= visible;
                return crack;
            }
            ENDCG
        }
    }
}
