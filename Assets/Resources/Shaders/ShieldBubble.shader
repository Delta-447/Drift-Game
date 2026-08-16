// Energy bubble for the shield power-up: a glass shell that is nearly
// invisible facing the camera and glows brightest at the silhouette, so the
// car stays clearly readable inside it. Unlit and additive - no lighting to
// go wrong, and it never darkens the car.
Shader "Driftline/ShieldBubble"
{
    Properties
    {
        _Color      ("Color", Color) = (0.35, 0.72, 1.0, 1.0)
        _RimPower   ("Rim Power", Float) = 2.2
        _Intensity  ("Intensity", Float) = 1.0
        _BandScale  ("Band Scale", Float) = 26.0
        _BandAmount ("Band Amount", Float) = 0.05
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Blend SrcAlpha One      // additive: glows, never muddies the car
            ZWrite Off
            Cull Off                // both shell faces, so it reads as a volume

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos     : SV_POSITION;
                float3 wnormal : TEXCOORD0;
                float3 wview   : TEXCOORD1;
                float3 opos    : TEXCOORD2;
            };

            float4 _Color;
            float _RimPower;
            float _Intensity;
            float _BandScale;
            float _BandAmount;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.wnormal = UnityObjectToWorldNormal(v.normal);
                o.wview = _WorldSpaceCameraPos - wp;
                o.opos = v.vertex.xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.wnormal);
                float3 v = normalize(i.wview);

                // fresnel: 0 head-on, 1 at the edge of the sphere
                float rim = 1.0 - saturate(abs(dot(n, v)));
                float f = pow(rim, _RimPower);

                // slow travelling rings so the surface looks like live energy
                float band = sin(i.opos.y * _BandScale + _Time.y * 2.5);
                band = _BandAmount * (0.5 + 0.5 * band);

                float a = saturate((f + band) * _Intensity) * _Color.a;
                float3 col = _Color.rgb * (f * 1.7 + 0.2);
                return fixed4(col, a);
            }
            ENDCG
        }
    }

    FallBack Off
}
