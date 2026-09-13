Shader "ElectricalSim/PipeLiquid"
{
    Properties
    {
        _Color ("Liquid color", Color) = (0.4, 0.5, 0.6, 0.85)
        _Front ("Front distance", Float) = 0
        _Valve ("Valve distance", Float) = 1
        _UpOpacity ("Upstream visibility", Float) = 0
        _DownOpacity ("Downstream visibility", Float) = 0
        _UpPhase ("Upstream movement", Float) = 0
        _DownPhase ("Downstream movement", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent-5" "RenderType"="Transparent" }
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
            float _Front, _Valve, _UpOpacity, _DownOpacity, _UpPhase, _DownPhase;
            struct Input { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct Output { float4 position : SV_POSITION; float2 uv : TEXCOORD0; float3 normal : TEXCOORD1; float3 world : TEXCOORD2; };
            Output vert(Input input)
            {
                Output output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.normal = UnityObjectToWorldNormal(input.normal);
                output.world = mul(unity_ObjectToWorld, input.vertex).xyz;
                return output;
            }
            fixed4 frag(Output input) : SV_Target
            {
                clip(_Front - input.uv.x);
                float downstream = step(_Valve, input.uv.x);
                float opacity = lerp(_UpOpacity, _DownOpacity, downstream);
                clip(opacity - 0.001);
                float phase = lerp(_UpPhase, _DownPhase, downstream);
                float streak = pow(0.5 + 0.5 * sin((input.uv.x - phase) * 85 + sin(input.uv.y * 18) * 0.8), 8);
                float3 v = normalize(_WorldSpaceCameraPos.xyz - input.world);
                float edge = pow(1 - saturate(dot(normalize(input.normal), v)), 3);
                float head = 1 - smoothstep(0, 0.015, _Front - input.uv.x);
                return fixed4(_Color.rgb * (0.86 + streak * 0.24) + edge * 0.13 + head * 0.08, _Color.a * opacity);
            }
            ENDCG
        }
    }
}
