Shader "EasyWater14_C2TBDOR" {
	Properties {
		_Color ("_Color", Vector) = (1,1,1,1)
		_Texture1 ("_Texture1", 2D) = "black" {}
		_BumpMap1 ("_BumpMap1", 2D) = "black" {}
		_Texture2 ("_Texture2", 2D) = "black" {}
		_BumpMap2 ("_BumpMap2", 2D) = "black" {}
		_MainTexSpeed ("_MainTexSpeed", Float) = 0
		_Bump1Speed ("_Bump1Speed", Float) = 0
		_Texture2Speed ("_Texture2Speed", Float) = 0
		_Bump2Speed ("_Bump2Speed", Float) = 0
		_DistortionMap ("_DistortionMap", 2D) = "black" {}
		_DistortionSpeed ("_DistortionSpeed", Float) = 0
		_DistortionPower ("_DistortionPower", Range(0, 0.02)) = 0
		_Specular ("_Specular", Range(0, 7)) = 1
		_Gloss ("_Gloss", Range(0.3, 2)) = 0.3
		_Opacity ("_Opacity", Range(-0.2, 1)) = 0
		_Reflection ("_Reflection", 2D) = "black" {}
		_ReflectPower ("_ReflectPower", Range(0, 1)) = 0
	}
	//DummyShaderTextExporter
	SubShader{
		Tags { "RenderType"="Opaque" }
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

			float4 _Color;

			float4 frag(Vertex_Stage_Output input) : SV_TARGET
			{
				return _Color; // RGBA
			}

			ENDHLSL
		}
	}
	Fallback "Diffuse"
}