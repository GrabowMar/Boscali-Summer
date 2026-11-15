Shader "Boscali/AtmosphereSky"
{
    Properties
    {
        // LevelInfo and RainAtmosphere keep writing these native inputs.
        _SunDirection ("Sun direction", Vector) = (0,-1,0,0)
        _SunColor ("Sun colour", Color) = (1,1,1,1)
        _FogColor ("Fog colour", Color) = (0.5,0.6,0.7,1)
        _AmbientIntensity ("Ambient intensity", Float) = 1
        _ScatterIntensity ("Scattering", Float) = 1
        _Conditions ("Native cloud cover", Float) = 0
        _CloudOcclusion ("Native cloud shade", Float) = 0
        _Altitude ("Native altitude", Float) = 0
        [HideInInspector] _SkyView ("Height, moon, night light, haze", Vector) = (0,0,1,0)
        [HideInInspector] _SkySun ("Toward sun", Vector) = (0,1,0,0)
        [HideInInspector] _SkyMoon ("Toward moon", Vector) = (0,-1,0,0)
        [HideInInspector] _SkyFog ("Linear horizon fog", Vector) = (0.2,0.3,0.4,0)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            float4 _SkyView, _SkySun, _SkyMoon, _SkyFog;
            float4x4 _SkyStarRotation;
            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 direction : TEXCOORD0; };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.direction = v.vertex.xyz;
                return o;
            }

            float AirMass(float mu)
            {
                mu = saturate(mu);
                return rcp(mu + 0.50572 * pow(max(96.07995 - degrees(acos(mu)), 0.01), -1.6364));
            }

            // The native star mesh supplies point stars. Only the diffuse band belongs here:
            // fixed celestial coordinates, no second star lattice or per-frame noise.
            float3 GalacticBand(float3 ray)
            {
                // A faint, broken galactic band with a dark dust lane. No bright nebula wallpaper.
                float latitude = dot(ray, normalize(float3(0.45,0.83,-0.33)));
                float longitude = atan2(ray.z, ray.x);
                float dust = 0.6 + 0.4 * sin(longitude * 9.0 + sin(longitude * 4.0));
                float band = exp(-latitude * latitude * 90.0) * (0.5 + 0.5 * dust);
                band *= 1.0 - 0.65 * exp(-pow(latitude + 0.014 * sin(longitude * 13.0), 2.0) * 1800.0);
                return float3(0.64,0.72,0.86) * band * 0.003;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 ray = normalize(i.direction), sun = normalize(_SkySun.xyz);
                float height = max(_SkyView.x, 0.0);
                float air = exp(-height / 8000.0), aerosol = exp(-height / 1200.0);
                float mu = dot(ray, sun), viewMass = AirMass(max(ray.y, 0.0));
                // RGB molecular optical depths and a shallow aerosol layer. Analytic single scattering
                // with a small diffuse fill; no extra march, texture, render target or sun disc.
                float3 betaR = float3(0.0464,0.1080,0.2648);
                float betaM = lerp(0.025, 0.085, _SkyView.w);
                float3 optical = betaR * air * viewMass;
                float3 sunT = exp(-(betaR * air + betaM * aerosol) * AirMass(max(sun.y, 0.0)));
                float day = smoothstep(-0.09, 0.025, sun.y);
                float phaseR = 0.75 * (1.0 + mu * mu);
                float phaseM = 0.08 / pow(max(0.05, 1.0 + 0.76 * 0.76 - 1.52 * mu), 1.5);
                float3 sky = (1.0 - exp(-optical)) * sunT * phaseR * 2.0 * day;
                sky += (1.0 - exp(-betaM * aerosol * viewMass)) * sunT * phaseM * day;
                // Blue multiple-scattered fill survives after direct sunlight has reddened.
                sky += float3(0.018,0.040,0.085) * air * smoothstep(-0.16,0.08,sun.y) * (0.4 + 0.6 * saturate(ray.y));

                float horizon = exp(-max(ray.y, 0.0) * 14.0);
                float dusk = (1.0 - smoothstep(0.0,0.20,abs(sun.y))) * smoothstep(-0.20,-0.025,sun.y);
                float toward = pow(saturate(mu * 0.5 + 0.5), 4.0);
                sky += float3(0.28,0.055,0.012) * dusk * toward * exp(-abs(ray.y - 0.035) * 10.0) * air;
                // Opposite the sunset, a subdued rose belt over the rising blue Earth shadow.
                float belt = exp(-pow((ray.y - 0.10) * 13.0, 2.0)) * pow(saturate(-mu), 2.0);
                sky += float3(0.035,0.013,0.022) * belt * dusk * air;

                float night = 1.0 - smoothstep(-0.24,-0.08,sun.y);
                float moon = _SkyMoon.w;
                float moonMu = dot(ray, _SkyMoon.xyz);
                sky += float3(0.0012,0.0020,0.0036) * air * night * _SkyView.z;
                sky += float3(0.008,0.012,0.020) * moon * air * night *
                    (0.4 + 0.6 * pow(saturate(moonMu), 8.0)) * (1.0 - exp(-viewMass * 0.4));

                float3 starRay = normalize(mul((float3x3)_SkyStarRotation, ray));
                // Day exposure continues to suppress stars from space while looking into daylight.
                float space = smoothstep(25000.0,80000.0,height);
                float visibility = max(night, space * 0.16 * (1.0 - saturate(mu) * 0.9));
                sky += GalacticBand(starRay) * visibility * exp(-optical) * (1.0 - moon * 0.72);
                // Match the scene fog at the sea horizon; fade this flat-world seam treatment out
                // before the existing curved-Earth composite takes over at high altitude.
                float earthFade = smoothstep(12000.0,30000.0,height);
                float seam = horizon * (1.0 - earthFade);
                sky = lerp(sky, _SkyFog.rgb, seam * 0.88);
                // Keep daylight haze behind the distant cloud edge below eye level; black here
                // exposes a dark horizon strip at cruise altitude. Only night needs the sea-darkening fade.
                // Higher up, the existing Earth composite owns the surface and atmospheric limb.
                sky *= lerp(1.0,smoothstep(-0.025,0.0,ray.y),night * (1.0 - earthFade));
                return float4(max(sky,0.0),1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
