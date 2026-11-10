Shader "Hidden/Xcy/CrackRepairProgress"
{
    Properties
    {
        _MainTex ("Crack Image", 2D) = "white" {}
        _RepairOrderMap ("Repair Order (R) and Crack Shape (G)", 2D) = "black" {}
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
            sampler2D _RepairOrderMap;
            float _Progress;

            fixed4 frag(v2f_img input) : SV_Target
            {
                fixed4 crack = tex2D(_MainTex, input.uv);
                float repairOrder = tex2D(_RepairOrderMap, input.uv).r;

                float visible = smoothstep(_Progress - 0.025, _Progress + 0.025, repairOrder);
                if (_Progress <= 0.001) visible = 1.0;
                if (_Progress >= 0.999) visible = 0.0;
                crack.rgb *= visible;
                crack.a *= visible;
                return crack;
            }
            ENDCG
        }
    }
}
