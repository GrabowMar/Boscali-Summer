// The vertex and pixel stages of NOA/TMP/VisorGlitchSDF; a pass sets BH_PASS (0 main, 1 red ghost, 2 cyan ghost) before it includes this.
// MODIFIED COPY of the vertex and pixel stages of "TextMeshPro/Distance Field" (TMP_SDF.shader, com.unity.textmeshpro 3.0.6, Unity Companion License,
// see PROVENANCE.txt). Changes: the bevel, glow and inner-underlay variants are dropped (the HUD's materials use face, outline and underlay only); the sideways
// tear, ghost split, dropout blocks, shimmer, bars, flicker and seam of NOA_VisorGlitch.cginc are added; with _BH_Fx.x = 0 the main pass draws what the
// stock shader draws.
#include "UnityCG.cginc"
#include "UnityUI.cginc"
#include "TMPro_Properties.cginc"
#include "TMPro.cginc"
#include "NOA_VisorGlitch.cginc"

struct vertex_t {
    UNITY_VERTEX_INPUT_INSTANCE_ID
    float4 position : POSITION;
    float3 normal : NORMAL;
    fixed4 color : COLOR;
    float2 texcoord0 : TEXCOORD0;
    float2 texcoord1 : TEXCOORD1;
};

struct pixel_t {
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
    float4 position : SV_POSITION;
    fixed4 color : COLOR;
    float2 atlas : TEXCOORD0;
    float4 param : TEXCOORD1;       // alphaClip, scale, bias, weight
    float4 mask : TEXCOORD2;        // position in object space (xy), pixel size (zw)
    float3 viewDir : TEXCOORD3;
#if UNDERLAY_ON
    float4 texcoord2 : TEXCOORD4;   // u, v, scale, bias
    fixed4 underlayColor : COLOR1;
#endif
    float4 textures : TEXCOORD5;
    float2 sp : TEXCOORD6;          // screen position in px, y from the top (before the glitch moved it)
};

float4 _FaceTex_ST;
float4 _OutlineTex_ST;

pixel_t VertShader(vertex_t input)
{
    pixel_t output;
    UNITY_INITIALIZE_OUTPUT(pixel_t, output);
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    float bold = step(input.texcoord1.y, 0);

    float4 vert = input.position;
    vert.x += _VertexOffsetX;
    vert.y += _VertexOffsetY;

    float4 vPosition = UnityObjectToClipPos(vert);
    float2 sp = bh_screen(vPosition);

    float2 pixelSize = vPosition.w;
    pixelSize /= float2(_ScaleX, _ScaleY) * abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
    float scale = rsqrt(dot(pixelSize, pixelSize));
    scale *= abs(input.texcoord1.y) * _GradientScale * (_Sharpness + 1);
    if (UNITY_MATRIX_P[3][3] == 0) scale = lerp(abs(scale) * (1 - _PerspectiveFilter), scale, abs(dot(UnityObjectToWorldNormal(input.normal.xyz), normalize(WorldSpaceViewDir(vert)))));

    // The glitch: whole-pixel shift of this vertex (the ghosts add their split); a ghost with nothing to show is moved off the screen.
    if (_BH_Fx.x > 0.0)
    {
        float2 d = bh_displace(sp);
        #if BH_PASS == 1
        d.x -= _BH_Chroma.x;
        #elif BH_PASS == 2
        d.x += _BH_Chroma.x;
        #endif
        #if BH_PASS != 0
        if (bh_ghost_off()) vPosition = float4(2.0, 2.0, 2.0, 1.0);
        #endif
        vPosition.xy += float2(d.x * 2.0 / _ScreenParams.x, -d.y * 2.0 / _ScreenParams.y * _ProjectionParams.x) * vPosition.w;
    }
    #if BH_PASS != 0
    else vPosition = float4(2.0, 2.0, 2.0, 1.0);
    #endif

    float weight = lerp(_WeightNormal, _WeightBold, bold) / 4.0;
    weight = (weight + _FaceDilate) * _ScaleRatioA * 0.5;

    float bias = (.5 - weight) + (.5 / scale);

    float alphaClip = (1.0 - _OutlineWidth * _ScaleRatioA - _OutlineSoftness * _ScaleRatioA);
    alphaClip = alphaClip / 2.0 - (.5 / scale) - weight;

#if UNDERLAY_ON
    float4 underlayColor = _UnderlayColor;
    underlayColor.rgb *= underlayColor.a;

    float bScale = scale;
    bScale /= 1 + ((_UnderlaySoftness * _ScaleRatioC) * bScale);
    float bBias = (0.5 - weight) * bScale - 0.5 - ((_UnderlayDilate * _ScaleRatioC) * 0.5 * bScale);

    float x = -(_UnderlayOffsetX * _ScaleRatioC) * _GradientScale / _TextureWidth;
    float y = -(_UnderlayOffsetY * _ScaleRatioC) * _GradientScale / _TextureHeight;
    float2 bOffset = float2(x, y);
#endif

    float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);

    float2 textureUV = UnpackUV(input.texcoord1.x);
    float2 faceUV = TRANSFORM_TEX(textureUV, _FaceTex);
    float2 outlineUV = TRANSFORM_TEX(textureUV, _OutlineTex);

    output.position = vPosition;
    output.color = input.color;
    output.atlas = input.texcoord0;
    output.param = float4(alphaClip, scale, bias, weight);
    output.mask = half4(vert.xy * 2 - clampedRect.xy - clampedRect.zw, 0.25 / (0.25 * half2(_MaskSoftnessX, _MaskSoftnessY) + pixelSize.xy));
    output.viewDir = mul((float3x3)_EnvMatrix, _WorldSpaceCameraPos.xyz - mul(unity_ObjectToWorld, vert).xyz);
#if UNDERLAY_ON
    output.texcoord2 = float4(input.texcoord0 + bOffset, bScale, bBias);
    output.underlayColor = underlayColor;
#endif
    output.textures = float4(faceUV, outlineUV);
    output.sp = sp;
    return output;
}

fixed4 PixShader(pixel_t input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);

    float c = tex2D(_MainTex, input.atlas).a;

#if !UNDERLAY_ON || BH_PASS != 0
    clip(c - input.param.x);
#endif

    float scale = input.param.y;
    float bias = input.param.z;
    float sd = (bias - c) * scale;

    float outline = (_OutlineWidth * _ScaleRatioA) * scale;
    float softness = (_OutlineSoftness * _ScaleRatioA) * scale;

    half4 faceColor = _FaceColor;
    half4 outlineColor = _OutlineColor;

    faceColor.rgb *= input.color.rgb;

    faceColor *= tex2D(_FaceTex, input.textures.xy + float2(_FaceUVSpeedX, _FaceUVSpeedY) * _Time.y);
    outlineColor *= tex2D(_OutlineTex, input.textures.zw + float2(_OutlineUVSpeedX, _OutlineUVSpeedY) * _Time.y);

    faceColor = GetColor(sd, faceColor, outlineColor, outline, softness);

#if UNDERLAY_ON && BH_PASS == 0
    float d = tex2D(_MainTex, input.texcoord2.xy).a * input.texcoord2.z;
    faceColor += input.underlayColor * saturate(d - input.texcoord2.w) * (1 - faceColor.a);
#endif

#if UNITY_UI_CLIP_RECT
    half2 m = saturate((_ClipRect.zw - _ClipRect.xy - abs(input.mask.xy)) * input.mask.zw);
    faceColor *= m.x * m.y;
#endif

#if UNITY_UI_ALPHACLIP
    clip(faceColor.a - 0.001);
#endif

    if (_BH_Fx.x > 0.0)
    {
        float seam;
        float lit = bh_light(input.sp, seam);
        #if BH_PASS == 0
        faceColor.rgb = saturate(faceColor.rgb + seam * 0.7 * faceColor.a);
        faceColor *= lit;
        #elif BH_PASS == 1
        faceColor = half4(float3(1.0, 0.12, 0.08) * faceColor.a, faceColor.a) * (lit * 0.75);
        #else
        faceColor = half4(float3(0.1, 0.9, 1.0) * faceColor.a, faceColor.a) * (lit * 0.75);
        #endif
    }

    return faceColor * input.color.a;
}
