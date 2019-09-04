/*
DISTRIBUTION STATEMENT A. Approved for public release. Distribution is unlimited.

This material is based upon work supported by the Under Secretary of Defense for Research
and Engineering under Air Force Contract No. FA8702-15-D-0001. Any opinions, findings,
conclusions or recommendations expressed in this material are those of the author(s) and
do not necessarily reflect the views of the Under Secretary of Defense for Research and
Engineering.

� 2019 Massachusetts Institute of Technology.

MIT Proprietary, Subject to FAR52.227-11 Patent Rights - Ownership by the contractor (May 2014)

The software/firmware is provided to you on an As-Is basis

Delivered to the U.S. Government with Unlimited Rights, as defined in DFARS Part 252.227-7013
or 7014 (Feb 2014). Notwithstanding any copyright notice, U.S. Government rights in this work
are defined by DFARS 252.227-7013 or DFARS 252.227-7014 as detailed above. Use of this work
other than as specifically authorized by the U.S. Government may violate any copyrights that
exist in this work.
*/

Shader "TESSE/TESSE_segmentation" 
{
	Properties{
		_MainTex("", 2D) = "white" {}
		_Cutoff("", Float) = 0.5
		_Color("", Color) = (1,1,1,1)

		_ObjectColor("Object Color", Color) = (1,1,1,1) 
		_CategoryColor("Catergory Color", Color) = (0,1,0,1)
	}

		SubShader{
		CGINCLUDE

		fixed4 _ObjectColor;
		fixed4 _CategoryColor;

		float4 Output(float depth01, float3 normal)
		{
			return _ObjectColor;
		}
		ENDCG


				Tags { "RenderType" = "Opaque" "SegClassID"="object" }
				Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"
			struct v2f {
				float4 pos : SV_POSITION;
				float4 nz : TEXCOORD0;
				UNITY_VERTEX_OUTPUT_STEREO
			};
			v2f vert(appdata_base v) {
				v2f o;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
				o.pos = UnityObjectToClipPos(v.vertex);
				o.nz.xyz = COMPUTE_VIEW_NORMAL;
				o.nz.w = COMPUTE_DEPTH_01;
				return o;
			}
			fixed4 frag(v2f i) : SV_Target {
				return Output(i.nz.w, i.nz.xyz);
			}
			ENDCG
				}
		}

			SubShader{
	Tags { "RenderType" = "TransparentCutout" "SegClassID" = "object"}
	Pass {
CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "UnityCG.cginc"
struct v2f {
	float4 pos : SV_POSITION;
	float2 uv : TEXCOORD0;
	float4 nz : TEXCOORD1;
	UNITY_VERTEX_OUTPUT_STEREO
};
uniform float4 _MainTex_ST;
v2f vert(appdata_base v) {
	v2f o;
	UNITY_SETUP_INSTANCE_ID(v);
	UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
	o.pos = UnityObjectToClipPos(v.vertex);
	o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
	o.nz.xyz = COMPUTE_VIEW_NORMAL;
	o.nz.w = COMPUTE_DEPTH_01;
	return o;
}
uniform sampler2D _MainTex;
uniform fixed _Cutoff;
uniform fixed4 _Color;
fixed4 frag(v2f i) : SV_Target {
	fixed4 texcol = tex2D(_MainTex, i.uv);
	clip(texcol.a*_Color.a - _Cutoff);
	return Output(i.nz.w, i.nz.xyz);
}
ENDCG
	}
			}

				SubShader{
					Tags { "RenderType" = "TreeBark" "SegClassID" = "object"}
					Pass {
				CGPROGRAM
				#pragma vertex vert
				#pragma fragment frag
				#include "UnityCG.cginc"
				#include "Lighting.cginc"
				#include "UnityBuiltin3xTreeLibrary.cginc"
				struct v2f {
					float4 pos : SV_POSITION;
					float2 uv : TEXCOORD0;
					float4 nz : TEXCOORD1;
					UNITY_VERTEX_OUTPUT_STEREO
				};
				v2f vert(appdata_full v) {
					v2f o;
					UNITY_SETUP_INSTANCE_ID(v);
					UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
					TreeVertBark(v);

					o.pos = UnityObjectToClipPos(v.vertex);
					o.uv = v.texcoord.xy;
					o.nz.xyz = COMPUTE_VIEW_NORMAL;
					o.nz.w = COMPUTE_DEPTH_01;
					return o;
				}
				fixed4 frag(v2f i) : SV_Target {
					return Output(i.nz.w, i.nz.xyz);
				}
				ENDCG
					}
}

SubShader{
	Tags { "RenderType" = "TreeLeaf" "SegClassID" = "object"}
	Pass {
CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "UnityCG.cginc"
#include "Lighting.cginc"
#include "UnityBuiltin3xTreeLibrary.cginc"
struct v2f {
	float4 pos : SV_POSITION;
	float2 uv : TEXCOORD0;
	float4 nz : TEXCOORD1;
	UNITY_VERTEX_OUTPUT_STEREO
};
v2f vert(appdata_full v) {
	v2f o;
	UNITY_SETUP_INSTANCE_ID(v);
	UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
	TreeVertLeaf(v);

	o.pos = UnityObjectToClipPos(v.vertex);
	o.uv = v.texcoord.xy;
	o.nz.xyz = COMPUTE_VIEW_NORMAL;
	o.nz.w = COMPUTE_DEPTH_01;
	return o;
}
uniform sampler2D _MainTex;
uniform fixed _Cutoff;
fixed4 frag(v2f i) : SV_Target {
	half alpha = tex2D(_MainTex, i.uv).a;

	clip(alpha - _Cutoff);
	return Output(i.nz.w, i.nz.xyz);
}
ENDCG
	}
				}

					SubShader{
						Tags { "RenderType" = "TreeOpaque" "DisableBatching" = "True" "SegClassID" = "object"}
						Pass {
					CGPROGRAM
					#pragma vertex vert
					#pragma fragment frag
					#include "UnityCG.cginc"
					#include "TerrainEngine.cginc"
					struct v2f {
						float4 pos : SV_POSITION;
						float4 nz : TEXCOORD0;
						UNITY_VERTEX_OUTPUT_STEREO
					};
					struct appdata {
						float4 vertex : POSITION;
						float3 normal : NORMAL;
						fixed4 color : COLOR;
						UNITY_VERTEX_INPUT_INSTANCE_ID
					};
					v2f vert(appdata v) {
						v2f o;
						UNITY_SETUP_INSTANCE_ID(v);
						UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
						TerrainAnimateTree(v.vertex, v.color.w);
						o.pos = UnityObjectToClipPos(v.vertex);
						o.nz.xyz = COMPUTE_VIEW_NORMAL;
						o.nz.w = COMPUTE_DEPTH_01;
						return o;
					}
					fixed4 frag(v2f i) : SV_Target {
						return Output(i.nz.w, i.nz.xyz);
					}
					ENDCG
						}
}

SubShader{
	Tags { "RenderType" = "TreeTransparentCutout" "DisableBatching" = "True" "SegClassID" = "object"}
	Pass {
		Cull Back
CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "UnityCG.cginc"
#include "TerrainEngine.cginc"

struct v2f {
	float4 pos : SV_POSITION;
	float2 uv : TEXCOORD0;
	float4 nz : TEXCOORD1;
	UNITY_VERTEX_OUTPUT_STEREO
};
struct appdata {
	float4 vertex : POSITION;
	float3 normal : NORMAL;
	fixed4 color : COLOR;
	float4 texcoord : TEXCOORD0;
	UNITY_VERTEX_INPUT_INSTANCE_ID
};
v2f vert(appdata v) {
	v2f o;
	UNITY_SETUP_INSTANCE_ID(v);
	UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
	TerrainAnimateTree(v.vertex, v.color.w);
	o.pos = UnityObjectToClipPos(v.vertex);
	o.uv = v.texcoord.xy;
	o.nz.xyz = COMPUTE_VIEW_NORMAL;
	o.nz.w = COMPUTE_DEPTH_01;
	return o;
}
uniform sampler2D _MainTex;
uniform fixed _Cutoff;
fixed4 frag(v2f i) : SV_Target {
	half alpha = tex2D(_MainTex, i.uv).a;

	clip(alpha - _Cutoff);
	return Output(i.nz.w, i.nz.xyz);
}
ENDCG
	}
	Pass {
		Cull Front
CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "UnityCG.cginc"
#include "TerrainEngine.cginc"

struct v2f {
	float4 pos : SV_POSITION;
	float2 uv : TEXCOORD0;
	float4 nz : TEXCOORD1;
	UNITY_VERTEX_OUTPUT_STEREO
};
struct appdata {
	float4 vertex : POSITION;
	float3 normal : NORMAL;
	fixed4 color : COLOR;
	float4 texcoord : TEXCOORD0;
	UNITY_VERTEX_INPUT_INSTANCE_ID
};
v2f vert(appdata v) {
	v2f o;
	UNITY_SETUP_INSTANCE_ID(v);
	UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
	TerrainAnimateTree(v.vertex, v.color.w);
	o.pos = UnityObjectToClipPos(v.vertex);
	o.uv = v.texcoord.xy;
	o.nz.xyz = -COMPUTE_VIEW_NORMAL;
	o.nz.w = COMPUTE_DEPTH_01;
	return o;
}
uniform sampler2D _MainTex;
uniform fixed _Cutoff;
fixed4 frag(v2f i) : SV_Target {
	fixed4 texcol = tex2D(_MainTex, i.uv);
	clip(texcol.a - _Cutoff);
	return Output(i.nz.w, i.nz.xyz);
}
ENDCG
	}

					}

						SubShader{
							Tags { "RenderType" = "TreeBillboard" "SegClassID" = "object"}
							Pass {
								Cull Off
						CGPROGRAM
						#pragma vertex vert
						#pragma fragment frag
						#include "UnityCG.cginc"
						#include "TerrainEngine.cginc"
						struct v2f {
							float4 pos : SV_POSITION;
							float2 uv : TEXCOORD0;
							float4 nz : TEXCOORD1;
							UNITY_VERTEX_OUTPUT_STEREO
						};
						v2f vert(appdata_tree_billboard v) {
							v2f o;
							UNITY_SETUP_INSTANCE_ID(v);
							UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
							TerrainBillboardTree(v.vertex, v.texcoord1.xy, v.texcoord.y);
							o.pos = UnityObjectToClipPos(v.vertex);
							o.uv.x = v.texcoord.x;
							o.uv.y = v.texcoord.y > 0;
							o.nz.xyz = float3(0,0,1);
							o.nz.w = COMPUTE_DEPTH_01;
							return o;
						}
						uniform sampler2D _MainTex;
						fixed4 frag(v2f i) : SV_Target {
							fixed4 texcol = tex2D(_MainTex, i.uv);
							clip(texcol.a - 0.001);
							return Output(i.nz.w, i.nz.xyz);
						}
						ENDCG
							}
}

SubShader{
	Tags { "RenderType" = "GrassBillboard" "SegClassID" = "object"}
	Pass {
		Cull Off
CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "UnityCG.cginc"
#include "TerrainEngine.cginc"

struct v2f {
	float4 pos : SV_POSITION;
	fixed4 color : COLOR;
	float2 uv : TEXCOORD0;
	float4 nz : TEXCOORD1;
	UNITY_VERTEX_OUTPUT_STEREO
};

v2f vert(appdata_full v) {
	v2f o;
	UNITY_SETUP_INSTANCE_ID(v);
	UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
	WavingGrassBillboardVert(v);
	o.color = v.color;
	o.pos = UnityObjectToClipPos(v.vertex);
	o.uv = v.texcoord.xy;
	o.nz.xyz = COMPUTE_VIEW_NORMAL;
	o.nz.w = COMPUTE_DEPTH_01;
	return o;
}
uniform sampler2D _MainTex;
uniform fixed _Cutoff;
fixed4 frag(v2f i) : SV_Target {
	fixed4 texcol = tex2D(_MainTex, i.uv);
	fixed alpha = texcol.a * i.color.a;
	clip(alpha - _Cutoff);
	return Output(i.nz.w, i.nz.xyz);
}
ENDCG
	}
						}

							SubShader{
								Tags { "RenderType" = "Grass" "SegClassID" = "object"}
								Pass {
									Cull Off
							CGPROGRAM
							#pragma vertex vert
							#pragma fragment frag
							#include "UnityCG.cginc"
							#include "TerrainEngine.cginc"
							struct v2f {
								float4 pos : SV_POSITION;
								fixed4 color : COLOR;
								float2 uv : TEXCOORD0;
								float4 nz : TEXCOORD1;
								UNITY_VERTEX_OUTPUT_STEREO
							};

							v2f vert(appdata_full v) {
								v2f o;
								UNITY_SETUP_INSTANCE_ID(v);
								UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
								WavingGrassVert(v);
								o.color = v.color;
								o.pos = UnityObjectToClipPos(v.vertex);
								o.uv = v.texcoord;
								o.nz.xyz = COMPUTE_VIEW_NORMAL;
								o.nz.w = COMPUTE_DEPTH_01;
								return o;
							}
							uniform sampler2D _MainTex;
							uniform fixed _Cutoff;
							fixed4 frag(v2f i) : SV_Target {
								fixed4 texcol = tex2D(_MainTex, i.uv);
								fixed alpha = texcol.a * i.color.a;
								clip(alpha - _Cutoff);
								return Output(i.nz.w, i.nz.xyz);
							}
							ENDCG
								}
}

			Fallback Off
}