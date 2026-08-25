// A flat, unlit colour. No lighting, no specular, no ambient - whatever
// colour is set is exactly what draws.
//
// This exists because of shader stripping. A build only includes shaders that
// something references, so Shader.Find("Universal Render Pipeline/Unlit") at
// runtime works in the editor and returns null on device, and the code falls
// back to a lit shader. A lit black car is not a black car: it still catches a
// specular sheen on every edge, which is exactly what traces out the shape of
// a car that is supposed to be a silhouette.
//
// Living in Resources means it always ships.
Shader "Driftline/FlatBlack"
{
    Properties
    {
        _Color ("Colour", Color) = (0.03, 0.03, 0.04, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            ZWrite On
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target { return _Color; }
            ENDCG
        }
    }

    Fallback Off
}
