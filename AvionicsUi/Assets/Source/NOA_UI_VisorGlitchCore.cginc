// The vertex and fragment stages of NOA/UI/VisorGlitch; a pass sets BH_PASS (0 main, 1 red ghost, 2 cyan ghost, 3 static) before it includes this.
// The main pass is the stock UI/Default (sprite * vertex colour, rect clip, alpha clip) plus the glitch terms of NOA_VisorGlitch.cginc,
// which are all zero while _BH_Fx.x is 0. The static pass belongs to the one material that has _BH_Static = 1 (a full-screen quad the HUD adds while a jammer is
// on the pilot): it draws the rolling bars as light, thin interference lines with grain, only inside a bar, additive; every other material draws no static, and the
// static material draws none of the other passes. Original code, written for this project.
#include "UnityCG.cginc"
#include "UnityUI.cginc"
#include "NOA_VisorGlitch.cginc"

struct appdata_t { float4 vertex : POSITION; float4 color : COLOR; float2 texcoord : TEXCOORD0; };
struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float4 worldPosition : TEXCOORD1; float2 sp : TEXCOORD2; };

fixed4 _Color, _TextureSampleAdd;
float4 _ClipRect;
float _BH_Static;
sampler2D _MainTex;

v2f vert(appdata_t v)
{
    v2f o;
    o.worldPosition = v.vertex;
    o.vertex = UnityObjectToClipPos(v.vertex);
    o.sp = bh_screen(o.vertex);
    #if BH_PASS == 3
    if (_BH_Static < 0.5 || _BH_Fx.x <= 0.0 || _BH_Bars.x <= 0.01) o.vertex = float4(2.0, 2.0, 2.0, 1.0);
    #else
    if (_BH_Static > 0.5) o.vertex = float4(2.0, 2.0, 2.0, 1.0);
    else if (_BH_Fx.x > 0.0)
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
    #endif
    o.uv = v.texcoord;
    o.color = v.color * _Color;
    return o;
}

fixed4 frag(v2f i) : SV_Target
{
    #if BH_PASS == 3
    float v = i.sp.y / _ScreenParams.y;
    float bar = bh_bar(v);
    float rn = bh_hash(float2(floor(i.sp.y * 0.5), _BH_Fx.z));
    float cn = bh_hash(float2(floor(i.sp.x * 0.2), floor(i.sp.y * 0.5) + _BH_Fx.z * 1.7));
    float lead = smoothstep(0.004, 0.0, abs(abs(frac(v - _BH_Bars.y + 0.5) - 0.5) - 0.03));
    float a = bar * (0.05 + 0.22 * step(0.62, rn) + 0.20 * step(0.82, cn)) + lead * 0.22 * _BH_Bars.x;
    return fixed4(lerp(float3(0.45, 1.0, 0.65), float3(1.0, 1.0, 1.0), cn * cn), saturate(a));
    #else
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
    #endif
}
