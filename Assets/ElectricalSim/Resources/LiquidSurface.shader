Shader "ElectricalSim/LiquidSurface"
{
    Properties
    {
        _Color ("Liquid color", Color) = (0.46, 0.36, 0.49, 0.92)
        _BumpMap ("Ripple normals", 2D) = "bump" {}
        _WaveScale ("Wave scale", Float) = 2.5
        _WaveStrength ("Wave strength", Range(0, 1)) = 0.48
        _FlowTime ("Animation time", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent-10" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            fixed4 _Color;
            sampler2D _BumpMap;
            float _WaveScale, _WaveStrength, _FlowTime;
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
                float2 uv = input.world.xz * _WaveScale;
                float3 wave1 = UnpackNormal(tex2D(_BumpMap, uv + _FlowTime * float2(0.019, 0.012)));
                float3 wave2 = UnpackNormal(tex2D(_BumpMap, uv * 1.37 + _FlowTime * float2(-0.013, 0.021)));
                float top = saturate(normalize(input.normal).y);
                float2 swell = float2(sin(uv.x * 9 + uv.y * 6 + _FlowTime * 0.65), cos(uv.y * 11 - uv.x * 4 - _FlowTime * 0.48)) * 0.09;
                float3 n = normalize(input.normal + float3(wave1.x + wave2.x + swell.x, 0, wave1.y + wave2.y + swell.y) * _WaveStrength * top);
                float3 v = normalize(_WorldSpaceCameraPos.xyz - input.world);
                float3 l = normalize(UnityWorldSpaceLightDir(input.world));
                float fresnel = pow(1 - saturate(dot(n, v)), 4);
                float specular = pow(saturate(dot(n, normalize(l + v))), 75) * 0.7 * top;
                half3 ambient = max(ShadeSH9(half4(n, 1)), half3(0.38, 0.38, 0.38));
                half3 color = _Color.rgb * (ambient * 0.65 + 0.48 + 0.13 * saturate(dot(n, l)));
                color += half3(0.64, 0.71, 0.79) * (fresnel * 0.3 + top * 0.04) + _LightColor0.rgb * specular;
                // Broad room reflections make the small ripples legible even when
                // the main light's sharp specular highlight is outside the view.
                float3 reflected = reflect(-v, n);
                half3 environment = DecodeHDR(UNITY_SAMPLE_TEXCUBE(unity_SpecCube0, reflected), unity_SpecCube0_HDR);
                float reflectionBand = smoothstep(0.32, 0.56, reflected.y) * (1 - smoothstep(0.75, 0.96, reflected.y));
                color = lerp(color, max(environment, half3(0.70, 0.75, 0.81)), top * (0.06 + 0.25 * reflectionBand));
                color += top * (wave1.x + wave2.y) * 0.035;
                return fixed4(color, saturate(_Color.a + fresnel * 0.06));
            }
            ENDCG
        }
    }
}
