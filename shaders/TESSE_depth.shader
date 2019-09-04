/*
DISTRIBUTION STATEMENT A. Approved for public release. Distribution is unlimited.

This material is based upon work supported by the Under Secretary of Defense for Research
and Engineering under Air Force Contract No. FA8702-15-D-0001. Any opinions, findings,
conclusions or recommendations expressed in this material are those of the author(s) and
do not necessarily reflect the views of the Under Secretary of Defense for Research and
Engineering.

© 2019 Massachusetts Institute of Technology.

MIT Proprietary, Subject to FAR52.227-11 Patent Rights - Ownership by the contractor (May 2014)

The software/firmware is provided to you on an As-Is basis

Delivered to the U.S. Government with Unlimited Rights, as defined in DFARS Part 252.227-7013
or 7014 (Feb 2014). Notwithstanding any copyright notice, U.S. Government rights in this work
are defined by DFARS 252.227-7013 or DFARS 252.227-7014 as detailed above. Use of this work
other than as specifically authorized by the U.S. Government may violate any copyrights that
exist in this work.
*/

Shader "TESSE/TESSE_depth" {
	Properties{

	}

		SubShader{
		
			Tags{ "RenderType" = "Opaque" }
			Pass{
			Fog {Mode Off}
			ZWrite On
			Cull Off
		CGPROGRAM
		#pragma vertex vert
		#pragma fragment frag
		#include "UnityCG.cginc"
		sampler2D _CameraDepthTexture;

		struct appdata_t {
			float4 vertex : POSITION;
			float2 uv : TEXCOORD0;
		};
			struct v2f {
				float2 uv : TEXCOORD0;
				float4 position : SV_POSITION;
				float4 screen_pos : TEXCOORD1;

			};
			v2f vert(appdata_t v) {
				v2f o;
				UNITY_SETUP_INSTANCE_ID(v);
				o.position = UnityObjectToClipPos(v.vertex);
				o.uv = v.uv;
				o.screen_pos = ComputeScreenPos(o.position);
				return o;
			}
			
			inline float4 myEncodeFloatRGBA(float v) {
				float4 enc = float4(1.0, 255.0, 65025.0, 16581375.0) * v;
				enc = frac(enc);
				enc -= enc.yzww * float4(1.0 / 255.0, 1.0 / 255.0, 1.0 / 255.0, 0.0);
				return enc;
			}

			float4 frag( v2f i ) : SV_Target
			{
				float depth = UNITY_SAMPLE_DEPTH(tex2D(_CameraDepthTexture, i.screen_pos.xy).r);
				depth = Linear01Depth(depth);
				return EncodeFloatRGBA(depth);
			}
			
			ENDCG
			}
		}

		Fallback Off
}