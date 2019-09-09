
// TubularEdge.shader
// 
// MIT License
// 
// Copyright(c) 2019 mattatz
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files(the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and / or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions :
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

Shader "Custom/Tubular Trail" {
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
		#include "ManuverTrail.cginc"

		StructuredBuffer<Trail> _TrailBuffer;
		StructuredBuffer<Node> _NodeBuffer;
		float4 _Color;
		float _Thickness;

		Node GetNode(int trailIdx, int nodeIdx) {
			return _NodeBuffer[ToNodeBufIdx(trailIdx, nodeIdx)];
		}

		struct v2g {
			float4 pos : POSITION0;
			float4 posNext: POSITION1;
			//float3 viewDir : TANGENT;
		};

		// NOTE: float1024個分の頂点数しかGeometryShaderで扱えないので極力減らす
		struct g2f {
			float4 position : SV_POSITION;
			//float3 normal : NORMAL;
		};

		// 参考 : @fuqunaga Unity Graphics Programming vol.2 GPU Trail
		// https://indievisuallab.stores.jp/items/5ae077b850bbc30f3a000a6d
		v2g vert(uint id : SV_VertexID, uint iid : SV_InstanceID) {
			v2g Out;
			Trail trail = _TrailBuffer[iid];
			int currentNodeIdx = trail.currentNodeIdx;

			Node node1, node2; // current, next
			node1 = GetNode(iid, id);
			bool isLastNode = (currentNodeIdx < 0 || currentNodeIdx == (int)id);
			if (isLastNode || !IsValid(node1))
				node2 = node1;
			else
				node2 = GetNode(iid, id + 1);
			Out.pos = float4(node1.position, 1);
			Out.posNext = float4(node2.position, 1);
			//Out.viewDir = WorldSpaceViewDir(Out.pos);

			return Out;
		}

		// 参考 : @mattaz TubularShader.shader
		// https://github.com/mattatz/Dendrite
		[maxvertexcount(180)] // 9*2 + 9*9*2
		void geom(point v2g IN[1], inout TriangleStream<g2f> Out) {
			float3 p0 = mul(unity_ObjectToWorld, IN[0].pos);
			float3 p1 = mul(unity_ObjectToWorld, IN[0].posNext);

			float3 t = normalize(p1 - p0);       // 進行方向
			float3 bn = cross(t, float3(0, 1, 0));
			//float3 bn = cross(t, IN[0].viewDir); // 上ベクトル
			float3 n = cross(t, bn);             // 横ベクトル

			static const uint rows = 9, cols = 9;
			static const float rows_inv = 1.0 / rows, cols_inv = 1.0 / (cols - 1);

			g2f o0, o1;

			// side
			for (uint i = 0; i < cols; i++) {
				float r = i * cols_inv * UNITY_TWO_PI;

				float s, c;
				sincos(r, s, c);

				float3 normal = normalize(n * c + bn * s);
				float4 w0 = UnityWorldToClipPos(p0 + normal * _Thickness);
				float4 w1 = UnityWorldToClipPos(p1 + normal * _Thickness);
				//o0.normal = o1.normal = normal;

				o0.position = w0;
				Out.Append(o0);

				o1.position = w1;
				Out.Append(o1);
			}
			Out.RestartStrip();

			// back
			for (uint row = 0; row < rows; row++) {
				float s0 = sin(row * rows_inv * UNITY_HALF_PI);
				float s1 = sin((row + 1) * rows_inv * UNITY_HALF_PI);
				for (uint col = 0; col < cols; col++)
				{
					float r = col * cols_inv * UNITY_TWO_PI;

					float s, c;
					sincos(r, s, c);

					float3 n0 = normalize(n * c * (1.0 - s0) + bn * s * (1.0 - s0) - t * s0);
					float3 n1 = normalize(n * c * (1.0 - s1) + bn * s * (1.0 - s1) - t * s1);

					o1.position = UnityWorldToClipPos(float4(p0 + n1 * _Thickness, 1));
					//o1.normal = n1;
					Out.Append(o1);

					o0.position = UnityWorldToClipPos(float4(p0 + n0 * _Thickness, 1));
					//o0.normal = n0;
					Out.Append(o0);
				}
				Out.RestartStrip();
			}
		}

		fixed4 frag(g2f IN) : SV_Target {
			fixed4 color = _Color;
			//// ポリゴン確認用
			//float3 normal = IN.normal;
			//fixed3 normal01 = saturate((normal + 1.0) * 0.6);
			//color.rgb *= normal01.xyz;
			return color;
		}
		ENDCG

		Cull Back
		ZWrite On
		ZTest LEqual
		Blend[_SrcBlend][_DstBlend]

		Pass {
			CGPROGRAM
			#pragma target 5.0	// Geometry Shaderは4.0だがMetalが非対応らしく5.0
			#pragma vertex vert
			#pragma geometry geom
			#pragma fragment frag
			#pragma multi_compile_instancing
			ENDCG
		}
	}
}