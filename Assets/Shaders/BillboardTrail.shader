Shader "Custom/Billboard Trail" {

	Properties{
		_MainTex("Base (RGB)", 2D) = "white" {}
		_Width("Width", Float) = 1
		_StartLife("StartLife", Float) = 0
		_EndLife("End Life", Float) = 0
		_Life("Life", Float) = 1
		_StartColor("StartColor", Color) = (1,1,1,1)
		_EndColor("EndColor", Color) = (0,0,0,1)
		[Enum(UnityEngine.Rendering.BlendMode)]
		_SrcBlend("Src Blend", Float) = 5	// SrcAlpha
		[Enum(UnityEngine.Rendering.BlendMode)]
		_DstBlend("Dst Blend", Float) = 10	// OneMinusSrcAlpha
	}

	SubShader{
		Pass{
			Cull Off ZWrite Off ZTest LEqual
			Blend [_SrcBlend] [_DstBlend]

			CGPROGRAM
			#pragma target 5.0	// Geometry Shaderは4.0だがMetalが非対応の為5.0にする
			#pragma vertex vert
			#pragma geometry geom
			#pragma fragment frag

			#include "UnityCG.cginc"
			#include "ManuverTrail.cginc"

			StructuredBuffer<Trail> _TrailBuffer;
			StructuredBuffer<Node> _NodeBuffer;
			sampler2D _MainTex;
			float _Width;
			float _StartLife, _EndLife;
			float4x4 _ParentMatrix;
			float4 _StartColor, _EndColor;

			Node GetNode(int trailIdx, int nodeIdx) {
				return _NodeBuffer[ToNodeBufIdx(trailIdx, nodeIdx)];
			}

			struct vs_out {
				float4 pos : POSITION0;
				float4 posNext: POSITION1;
				float3 dir : TANGENT0;
				float3 dirNext : TANGENT1;
				float4 col : COLOR0;
				float4 colNext : COLOR1;
				float2 uv : TEXCOORD0;
			};

			struct gs_out {
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
				float4 col : COLOR;
			};

			vs_out vert(uint id : SV_VertexID, uint instanceId : SV_InstanceID) {
				vs_out Out;
				Trail trail = _TrailBuffer[instanceId];
				int currentNodeIdx = trail.currentNodeIdx;

				Node node1 = GetNode(instanceId, id); // current
				Node node2, node3, node4;

				bool isLastNode = (currentNodeIdx < 0 || currentNodeIdx == (int)id);

				if (isLastNode || !IsValid(node1)) {
					node1 = node2 = node3 = node4 = GetNode(instanceId, currentNodeIdx);
				} else {
					node2 = GetNode(instanceId, id + 1);
					node3 = GetNode(instanceId, id + 2);
					node4 = GetNode(instanceId, id + 3);
				}

				float4 pos1 = mul(_ParentMatrix, float4(node1.position, 1));
				float4 pos2 = mul(_ParentMatrix, float4(node2.position, 1));

				Out.pos = pos1;
				Out.posNext = pos2;
				Out.dir = normalize(pos2 - pos1);

				if (node3.time < node2.time) {
					Out.dirNext = Out.dir;
				} else {
					float4 pos3 = mul(_ParentMatrix, float4(node3.position, 1));
					Out.dirNext = normalize(pos3 - pos2);
				}

				float age = _CheckTime - node1.time;
				float ageNext = _CheckTime - node2.time;
				float ageRate = saturate(age / _Life);
				float ageRateNext = saturate(ageNext / _Life);

				Out.uv = float2(ageRate, ageRateNext);

				float4 color;
				float col = 1;

				if (age < _StartLife)
					col = age / _StartLife;
				else if (age > _EndLife)
					col = 1 - saturate((age - _EndLife) / (_Life - _EndLife));
				color = lerp(_StartColor, _EndColor, age / _Life);
				color.rgb *= col;
				Out.col = color;

				if (ageNext < _StartLife)
					col = ageNext / _StartLife;
				else if (ageNext > _EndLife)
					col = 1 - saturate((ageNext - _EndLife) / (_Life - _EndLife));
				color = lerp(_StartColor, _EndColor, ageNext / _Life);
				color.rgb *= col;
				Out.colNext = color;

				// 先端2節を馴染ませる
				if (node3.time < 0 || node3.time < node2.time) {
					Out.col *= 0.5;
					Out.colNext = float4(0, 0, 0, 1);
				}
				else if (node4.time < 0 || node4.time < node3.time) {
					Out.colNext *= 0.5;
				}

				//// テスト用カラー
				//Out.col = float4(1, 0, 0, 1);
				//Out.colNext = float4(0, 1, 0, 1);

				return Out;
			}

			[maxvertexcount(4)]
			void geom(point vs_out input[1], inout TriangleStream<gs_out> outStream) {
				gs_out output0, output1, output2, output3;
				float3 pos = input[0].pos;
				float3 dir = input[0].dir;
				float3 posNext = input[0].posNext;
				float3 dirNext = input[0].dirNext;

				float3 camPos = _WorldSpaceCameraPos;
				float3 toCamDir = normalize(camPos - pos);
				float3 sideDir = normalize(cross(toCamDir, dir));

				float3 toCamDirNext = normalize(camPos - posNext);
				float3 sideDirNext = normalize(cross(toCamDirNext, dirNext));
				float width = _Width * 0.5;

				output0.pos = UnityWorldToClipPos(pos + (sideDir * width));
				output1.pos = UnityWorldToClipPos(pos - (sideDir * width));
				output2.pos = UnityWorldToClipPos(posNext + (sideDirNext * width));
				output3.pos = UnityWorldToClipPos(posNext - (sideDirNext * width));

				output0.col = output1.col = input[0].col;
				output2.col = output3.col = input[0].colNext;

				output0.uv = float2(input[0].uv.x, 0);
				output1.uv = float2(input[0].uv.x, 1);
				output2.uv = float2(input[0].uv.y, 0);
				output3.uv = float2(input[0].uv.y, 1);

				outStream.Append(output0);
				outStream.Append(output1);
				outStream.Append(output2);
				outStream.Append(output3);

				outStream.RestartStrip();
			}

			fixed4 frag(gs_out In) : COLOR {
				return tex2D(_MainTex, In.uv) * In.col;
			}
			ENDCG
		}
	}
}


