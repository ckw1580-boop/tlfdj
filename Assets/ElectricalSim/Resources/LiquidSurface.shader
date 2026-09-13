Shader "ElectricalSim/LiquidSurface"
{
    Properties { _Color ("Liquid color", Color) = (0.04, 0.48, 0.7, 0.78) }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            struct Input { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct Output { float4 position : SV_POSITION; float3 normal : TEXCOORD0; };
            Output vert(Input input)
            {
                Output output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.normal = UnityObjectToWorldNormal(input.normal);
                return output;
            }
            fixed4 frag(Output input) : SV_Target
            {
                float top = saturate(normalize(input.normal).y);
                return fixed4(lerp(_Color.rgb * 0.8, _Color.rgb + 0.18, top), _Color.a);
            }
            ENDCG
        }
    }
}
