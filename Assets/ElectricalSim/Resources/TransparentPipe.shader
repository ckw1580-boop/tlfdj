Shader "ElectricalSim/TransparentPipe"
{
    SubShader
    {
        Tags { "Queue"="Transparent+5" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct Input { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct Output { float4 position : SV_POSITION; float3 normal : TEXCOORD0; float3 world : TEXCOORD1; };
            Output vert(Input input)
            {
                Output output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.normal = UnityObjectToWorldNormal(input.normal);
                output.world = mul(unity_ObjectToWorld, input.vertex).xyz;
                return output;
            }
            fixed4 frag(Output input) : SV_Target
            {
                float3 n = normalize(input.normal);
                float3 v = normalize(_WorldSpaceCameraPos.xyz - input.world);
                float edge = pow(1 - abs(dot(n, v)), 2.5);
                return fixed4(0.35, 0.42, 0.48, 0.075 + edge * 0.32);
            }
            ENDCG
        }
    }
}
