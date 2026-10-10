// Paints a squadron insignia into a livery atlas, in texture space: every fin vertex is placed
// at its own UV, so the fragments are the fin's texels, and each one samples the insignia by
// where it sits in the fin's own 3D space. Run once per livery, never per frame.
//
// A fin's two faces often share one mirrored UV island. Pass 0 paints the outboard face and
// marks its texels in stencil; pass 1 paints the inboard face (the caller hands it a mirrored
// frame) only where pass 0 left no mark, so a shared island is painted once and a fin with
// two islands reads correctly from both sides.
Shader "Hidden/BoscaliSummer/EmblemBake"
{
    Properties
    {
        _MainTex ("Insignia", 2D) = "white" {}
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex;
    // Mesh-local frame of the marking. xyz is an axis, w its offset, so dot(float4(p, 1), axis) is a
    // coordinate: right and up run -1..1 across the marking, normal is metres off the face.
    float4 _EmblemRight, _EmblemUp, _EmblemNormal;
    float4 _EmblemParams; // x opacity, y half-depth in metres, zw UV tile shift

    struct v2f
    {
        float4 pos : SV_POSITION;
        float3 box : TEXCOORD0;
        float facing : TEXCOORD1;
    };

    v2f vert(appdata_base v)
    {
        v2f o;
        float2 uv = v.texcoord.xy + _EmblemParams.zw;
        o.pos = float4(uv * 2.0 - 1.0, 0.0, 1.0);
        #if UNITY_UV_STARTS_AT_TOP
        o.pos.y = -o.pos.y;
        #endif
        float4 p = float4(v.vertex.xyz, 1.0);
        o.box = float3(dot(p, _EmblemRight), dot(p, _EmblemUp), dot(p, _EmblemNormal));
        o.facing = dot(normalize(v.normal), _EmblemNormal.xyz);
        return o;
    }

    fixed4 frag(v2f i) : SV_Target
    {
        clip(i.facing - 0.5);
        clip(1.0 - max(abs(i.box.x), abs(i.box.y)));
        clip(_EmblemParams.y - abs(i.box.z));
        fixed4 c = tex2D(_MainTex, i.box.xy * 0.5 + 0.5);
        c.a *= _EmblemParams.x;
        return c;
    }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

        Pass
        {
            Name "OUTBOARD"
            Stencil { Ref 1 Comp Always Pass Replace }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }

        Pass
        {
            Name "INBOARD"
            Stencil { Ref 1 Comp NotEqual }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }
    }
}
