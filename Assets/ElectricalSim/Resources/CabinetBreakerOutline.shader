Shader "ElectricalSim/Cabinet Breaker Outline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (1, 0.82, 0.04, 1)
        _OutlineWidth ("Outline Width", Float) = 0.0009
        _GlowColor ("Glow Color", Color) = (1, 0.68, 0.02, 0.28)
        _GlowWidth ("Glow Width", Float) = 0.0022
    }

    SubShader
    {
        Tags { "Queue"="Transparent+20" "RenderType"="Transparent" "IgnoreProjector"="True" }

        Pass
        {
            Name "Outer Glow"
            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _GlowWidth;
            fixed4 _GlowColor;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
            };

            v2f vert(appdata input)
            {
                input.vertex.xyz += normalize(input.normal) * _GlowWidth;
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                return _GlowColor;
            }
            ENDCG
        }

        Pass
        {
            Name "Yellow Outline"
            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _OutlineWidth;
            fixed4 _OutlineColor;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
            };

            v2f vert(appdata input)
            {
                input.vertex.xyz += normalize(input.normal) * _OutlineWidth;
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                return _OutlineColor;
            }
            ENDCG
        }
    }

    Fallback Off
}
