Shader "Shader Forge/Examples/FixRefraction" {
	Properties {
		_RefractionIntensity ("Refraction Intensity", Range(0, 1)) = 0.5066193
		_Refraction ("Refraction", 2D) = "bump" {}
		_node_5044 ("node_5044", Vector) = (1,0,0,1)
		_myAddColor ("myAddColor", Vector) = (1,0,0,1)
		_node_2810 ("node_2810", Range(0, 10)) = 0.3846154
		_R ("R", Range(0, 0.2)) = 0.1
		[HideInInspector] _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.5
	}
	//DummyShaderTextExporter
	SubShader{
		Tags { "RenderType" = "Opaque" }
		LOD 200

		Pass
		{
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			float4x4 unity_ObjectToWorld;
			float4x4 unity_MatrixVP;

			struct Vertex_Stage_Input
			{
				float4 pos : POSITION;
			};

			struct Vertex_Stage_Output
			{
				float4 pos : SV_POSITION;
			};

			Vertex_Stage_Output vert(Vertex_Stage_Input input)
			{
				Vertex_Stage_Output output;
				output.pos = mul(unity_MatrixVP, mul(unity_ObjectToWorld, input.pos));
				return output;
			}

			float4 frag(Vertex_Stage_Output input) : SV_TARGET
			{
				return float4(1.0, 1.0, 1.0, 1.0); // RGBA
			}

			ENDHLSL
		}
	}
	Fallback "Diffuse"
	//CustomEditor "ShaderForgeMaterialInspector"
}