// The vertex and fragment stages of NOA/UI/VisorGlitch; a pass sets BH_PASS (0 main, 1 red ghost, 2 cyan ghost) before it includes this.
// The main pass is the stock UI/Default (sprite * vertex colour, rect clip, alpha clip) plus the glitch terms of NOA_VisorGlitch.cginc,
// which are all zero while _BH_Fx.x is 0. Original code, written for this project.
#include "UnityCG.cginc"
#include "UnityUI.cginc"
#include "NOA_VisorGlitch.cginc"

struct appdata_t { float4 vertex : POSITION; float4 color : COLOR; float2 texcoord : TEXCOORD0; };
struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float4 worldPosition : TEXCOORD1; float2 sp : TEXCOORD2; };

fixed4 _Color, _TextureSampleAdd;
float4 _ClipRect;
sampler2D _MainTex;

v2f vert(appdata_t v)
{
    v2f o;
    o.worldPosition = v.vertex;
    o.vertex = UnityObjectToClipPos(v.vertex);
    o.sp = bh_screen(o.vertex);
    if (_BH_Fx.x > 0.0)
    {
        float2 d = bh_displace(o.sp);
        #if BH_PASS == 1
        d.x -= _BH_Chroma.x;
        #elif BH_PASS == 2
        d.x += _BH_Chroma.x;
        #endif
        #if BH_PASS != 0
        if (bh_ghost_off()) o.vertex = float4(2.0, 2.0, 2.0, 1.0);
        #endif
        o.vertex.xy += float2(d.x * 2.0 / _ScreenParams.x, -d.y * 2.0 / _ScreenParams.y * _ProjectionParams.x) * o.vertex.w;
    }
    #if BH_PASS != 0
    else o.vertex = float4(2.0, 2.0, 2.0, 1.0);
    #endif
    o.uv = v.texcoord;
    o.color = v.color * _Color;
    return o;
}

fixed4 frag(v2f i) : SV_Target
{
    half4 color = (tex2D(_MainTex, i.uv) + _TextureSampleAdd) * i.color;
    #ifdef UNITY_UI_CLIP_RECT
    color.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
    #endif
    #ifdef UNITY_UI_ALPHACLIP
    clip(color.a - 0.001);
    #endif
    if (_BH_Fx.x > 0.0)
    {
        float seam;
        float m = bh_light(i.sp, seam);
        #if BH_PASS == 0
        color.rgb = saturate(color.rgb + seam * 0.7);
        color.a *= m;
        #elif BH_PASS == 1
        color = half4(1.0, 0.12, 0.08, color.a * m * 0.75);
        #else
        color = half4(0.1, 0.9, 1.0, color.a * m * 0.75);
        #endif
    }
    return color;
}
