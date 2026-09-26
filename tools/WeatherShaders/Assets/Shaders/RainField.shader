// Falling rain around the camera, as a toroidally wrapped field of streaks.
//
// The mesh is a static cloud of quads whose POSITION is a seed in [0,1)^3. The vertex stage
// wraps each seed into an L-sized cube centred on the object (the owner keeps the object on the
// camera), so the field never runs out at any airspeed and costs nothing on the CPU per drop:
//
//     local = (frac(seed - _RWOffset) - 0.5) * L
//
// _RWOffset = frac((cameraGlobal - rainVelocity * t) / L) is computed on the CPU in double
// precision, which is what keeps a drop still in the world while the camera moves and falling
// with the rain while time passes. Each drop is then drawn as a motion-blurred streak along the
// rain's velocity relative to the camera (the length a 1/60 s exposure would record), capped
// to a fraction of the screen and dimmed as it stretches so fast flight reads as a sheet, not
// white bars. Keyword-free on purpose: no variant can be stripped from the bundle.
Shader "Boscali/RainField"
{
    Properties
    {
        _RWOffset ("Wrap offset (xyz)", Vector) = (0, 0, 0, 0)
        _RWField ("L, near fade m, far fade start (fraction of L/2), density", Vector) = (24, 1.5, 0.7, 1)
        _RWRelVel ("Rain velocity relative to the camera (m/s)", Vector) = (0, -8, 0, 0)
        _RWShape ("Exposure s, width m, min width px, max screen fraction", Vector) = (0.0166, 0.006, 1.2, 0.12)
        _RWLook ("Alpha falloff per streak metre, soft depth m, stretch scale, head brightness", Vector) = (1.2, 0.6, 1, 0.35)
        _RWScreen ("Screen height px, tan half fov", Vector) = (1080, 0.577, 0, 0)
        _RWTint ("Tint (rgb) and alpha", Color) = (0.7, 0.75, 0.8, 0.25)
        _RWFlash ("Additive flash", Color) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+50"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "RainField"
            Tags { "LightMode" = "UniversalForward" }

            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _RWOffset;
                float4 _RWField;
                float4 _RWRelVel;
                float4 _RWShape;
                float4 _RWLook;
                float4 _RWScreen;
                float4 _RWTint;
                float4 _RWFlash;
            CBUFFER_END

            struct Attributes
            {
                float3 seed : POSITION;   // wrap seed in [0,1)^3
                float4 uv   : TEXCOORD0;  // x: side -1/+1, y: tail 0 / head 1, z: rank, w: size jitter
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 streak     : TEXCOORD0; // x across, y along, z alpha
                float4 screenPos  : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;

                // Density is a rank cut: the first `density` fraction of drops draw, the rest collapse.
                if (input.uv.z > _RWField.w)
                {
                    o.positionCS = float4(-2, -2, -2, 1);
                    return o;
                }

                float L = _RWField.x;
                float3 local = (frac(input.seed - _RWOffset.xyz) - 0.5) * L;
                float3 centreWS = TransformObjectToWorld(local);
                float3 toCam = GetCameraPositionWS() - centreWS;
                float dist = max(length(toCam), 1e-3);
                float3 viewDir = toCam / dist;

                float3 rel = _RWRelVel.xyz;
                float speed = length(rel);
                float3 axis = speed > 0.05 ? rel / speed : float3(0, -1, 0);

                float width = _RWShape.y * (0.7 + 0.6 * input.uv.w);

                // Thin streaks keep a minimum on-screen width and pay for it in alpha, so distant
                // drops shimmer instead of aliasing away.
                float metresPerPixel = 2.0 * dist * _RWScreen.y / max(_RWScreen.x, 1.0);
                float alphaScale = 1.0;
                float minWidth = _RWShape.z * metresPerPixel;
                if (width < minWidth)
                {
                    alphaScale = width / minWidth;
                    width = minWidth;
                }

                float len = speed * _RWShape.x * _RWLook.z;
                float maxLen = _RWShape.w * 2.0 * dist * _RWScreen.y;
                len = clamp(len, width * 2.0, max(maxLen, width * 2.0));

                float3 side = cross(axis, viewDir);
                float sideLen = length(side);
                side = sideLen > 1e-3 ? side / sideLen : normalize(cross(float3(0, 1, 0), viewDir) + float3(1e-4, 0, 0));

                // Head is where the drop is now; the tail is where it was one exposure ago.
                float3 tail = centreWS - axis * len;
                float3 positionWS = lerp(tail, centreWS, input.uv.y) + side * (input.uv.x * width * 0.5);

                float nearFade = saturate((dist - _RWField.y) / max(_RWField.y, 0.01));
                float farFade = 1.0 - smoothstep(_RWField.z, 1.0, dist / (0.5 * L));
                float alpha = _RWTint.a * alphaScale * nearFade * farFade / (1.0 + len * _RWLook.x);

                o.positionCS = TransformWorldToHClip(positionWS);
                o.streak = float3(input.uv.x, input.uv.y, alpha);
                o.screenPos = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float across = saturate(1.0 - i.streak.x * i.streak.x);
                float along = smoothstep(0.0, 0.25, i.streak.y) * (1.0 - smoothstep(0.92, 1.0, i.streak.y));
                along *= lerp(1.0, 1.0 + _RWLook.w, i.streak.y);

                float2 uv = i.screenPos.xy / max(i.screenPos.w, 1e-5);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float soft = saturate((sceneEye - i.screenPos.w) / max(_RWLook.y, 1e-3));

                float a = saturate(across * along * i.streak.z * soft);
                half3 rgb = (_RWTint.rgb + _RWFlash.rgb) * a;
                return half4(rgb, a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
