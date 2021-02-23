
Shader "Custom/Tubular Trail Mesh" {
	Properties {
		[HDR]
		_Color("Color", Color) = (1, 1, 1, 1) // 発光色
		_Thickness("Thickness", Range(0.01, 1)) = 0.1 // 太さ
		[Enum(UnityEngine.Rendering.BlendMode)]
		_SrcBlend("Src Blend", Float) = 5	// SrcAlpha
		[Enum(UnityEngine.Rendering.BlendMode)]
		_DstBlend("Dst Blend", Float) = 10	// OneMinusSrcAlpha
	}

	SubShader {
		Tags{ "RenderType" = "Opaque" "Queue" = "Geometry" }
		Offset 1, 1

		CGINCLUDE
		#include "UnityCG.cginc"
		#include "ManeuverTrail.cginc"

		float4 _Color;
		float _Thickness;

		struct appdata_t {
			float4 vertex : POSITION;
		};

		struct v2f {
			float4 pos : POSITION0;
		};

		v2f vert(appdata_t v)
		{
			v2f o;
			// ワールド変換不要
			//o.pos = UnityObjectToClipPos(v.vertex);
			o.pos = mul(UNITY_MATRIX_VP, float4(v.vertex.xyz, 1.0));
			return o;
		}

		fixed4 frag(v2f IN) : SV_Target {
			return _Color;
		}
		ENDCG

		Cull Back
		ZWrite On
		ZTest LEqual
		Blend[_SrcBlend][_DstBlend]

		Pass {
			CGPROGRAM
			#pragma target 5.0
			#pragma vertex vert
			#pragma fragment frag
			ENDCG
		}
	}
}