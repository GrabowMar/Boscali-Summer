using BoscaliSummer.Modules.Weather.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Visuals
{
    /// <summary>What the cloud material needs from the game for one frame. Local (floating
    /// origin) camera values; WorldOffset turns them into the global frame the maps use.</summary>
    internal struct CloudFrame
    {
        public Vector3 WorldOffset;
        public Vector3 CameraPosition, CameraForward;
        public float FieldOfView;
        public int PixelHeight;
        public float CloudShift;
        /// <summary>Altitude range of the weather cloud (before set-pieces), shift not applied.</summary>
        public float Bottom, Top;
        public float HorizonCover;
        public Vector3 SunDirection;
        public Color SunColor, Ambient, Ground, Fog;
        public float Extinction;
        /// <summary>Lightning flash envelope, 0..1: lights the cloud cores the sun cannot reach.</summary>
        public float Flash;
        public Vector4 FlashA, FlashB;
        public bool LowDetail;
        public bool RainVisualsEnabled;
        public float DeltaTime;
    }

    /// <summary>Writes one frame of cloud uniforms. Owns the smoothing of set-pieces and the fog
    /// bank, so a console change builds up on screen instead of popping. Unity-only (no game
    /// types), so the offline bench drives exactly the game's uniforms.</summary>
    internal sealed class CloudVolumeUniforms
    {
        private static readonly int NoiseId = Shader.PropertyToID("_CloudNoiseTex");
        private static readonly int MapSpanId = Shader.PropertyToID("_WeatherMapSpan");
        private static readonly int FarSpanId = Shader.PropertyToID("_WeatherFarSpan");
        private static readonly int BaseId = Shader.PropertyToID("_CloudBase");
        private static readonly int ShiftId = Shader.PropertyToID("_CloudHeightShift");
        private static readonly int OffsetId = Shader.PropertyToID("_CloudWorldOffset");
        private static readonly int ForwardId = Shader.PropertyToID("_CloudCameraForward");
        private static readonly int CameraPosId = Shader.PropertyToID("_CloudCameraPos");
        private static readonly int FogBankId = Shader.PropertyToID("_FogBank");
        private static readonly int EyeId = Shader.PropertyToID("_CloudEye");
        private static readonly int HeroAId = Shader.PropertyToID("_HeroA");
        private static readonly int HeroBId = Shader.PropertyToID("_HeroB");
        private static readonly int HeroCountId = Shader.PropertyToID("_HeroCount");
        private static readonly int BoundsId = Shader.PropertyToID("_CloudAltitudeBounds");
        private static readonly int HeroBoundsId = Shader.PropertyToID("_CloudHeroBounds");
        private static readonly int WindOffsetId = Shader.PropertyToID("_CloudWindOffset");
        private static readonly int SunDirId = Shader.PropertyToID("_CloudSunDirection");
        private static readonly int SunColorId = Shader.PropertyToID("_CloudSunColor");
        private static readonly int AmbientId = Shader.PropertyToID("_CloudAmbientColor");
        private static readonly int GroundId = Shader.PropertyToID("_CloudGroundColor");
        private static readonly int FogId = Shader.PropertyToID("_CloudFogColor");
        private static readonly int ExtinctionId = Shader.PropertyToID("_CloudAirExtinction");
        private static readonly int FlashId = Shader.PropertyToID("_CloudFlash");
        private static readonly int FlashAId = Shader.PropertyToID("_CloudFlashA");
        private static readonly int FlashBId = Shader.PropertyToID("_CloudFlashB");
        private static readonly int FlashChangeId = Shader.PropertyToID("_CloudFlashChange");
        private static readonly int StormId = Shader.PropertyToID("_CloudStorm");
        private static readonly int RainVisualsId = Shader.PropertyToID("_CloudRainVisuals");
        private static readonly int LayerDepthId = Shader.PropertyToID("_LayerDepth");
        private static readonly int LayerSmoothId = Shader.PropertyToID("_LayerSmooth");
        private static readonly int MidCoverId = Shader.PropertyToID("_MidCover");
        private static readonly int MidSheetId = Shader.PropertyToID("_MidSheet");
        private static readonly int HighCoverId = Shader.PropertyToID("_HighCover");
        private static readonly int HighVeilId = Shader.PropertyToID("_HighVeil");
        private static readonly int PuffScaleId = Shader.PropertyToID("_PuffScale");
        private static readonly int PuffDepthId = Shader.PropertyToID("_PuffDepth");
        private static readonly int BaseSharpId = Shader.PropertyToID("_BaseSharp");
        private static readonly int BaseWobbleId = Shader.PropertyToID("_BaseWobble");
        private static readonly int DomeId = Shader.PropertyToID("_Dome");
        private static readonly int BillowId = Shader.PropertyToID("_Billow");
        private static readonly int AnvilId = Shader.PropertyToID("_Anvil");
        private static readonly int WindDirId = Shader.PropertyToID("_CloudWindDir");
        private static readonly int HorizonCoverId = Shader.PropertyToID("_HorizonCover");
        private static readonly int HorizonDeckId = Shader.PropertyToID("_HorizonDeck");
        private static readonly int HorizonDepthId = Shader.PropertyToID("_HorizonDepth");
        private static readonly int SplitAId = Shader.PropertyToID("_SplitA");
        private static readonly int SplitBId = Shader.PropertyToID("_SplitB");
        private static readonly int PixelAngleId = Shader.PropertyToID("_CloudPixelAngle");
        private static readonly int StepsId = Shader.PropertyToID("_CloudSteps");
        private static readonly int FarStepsId = Shader.PropertyToID("_CloudFarSteps");
        private static readonly int FrustumId = Shader.PropertyToID("_CloudFrustum");
        private static readonly int PrevMatrixId = Shader.PropertyToID("_CloudPrevMatrix");
        private static readonly int CamDeltaId = Shader.PropertyToID("_CloudCamDelta");
        private static readonly int CheckerId = Shader.PropertyToID("_CloudChecker");
        private static readonly Vector2[] CheckerOrder = { new Vector2(0, 0), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 1) };

        private readonly Vector4[] heroA = new Vector4[Superstructures.MaxCount];
        private readonly Vector4[] heroB = new Vector4[Superstructures.MaxCount];
        private readonly float[] heroShown = new float[Superstructures.MaxCount];
        private readonly Vector2[] heroSite = new Vector2[Superstructures.MaxCount];
        private float fogShown;
        private Matrix4x4 previousRotation = Matrix4x4.identity;
        private Matrix4x4 previousProjection;
        private Camera previousCamera;
        private RenderTexture previousTarget;
        private Rect previousRect;
        private Vector3 previousPosition;
        private Quaternion previousQuat = Quaternion.identity;
        private float previousFov;
        private bool hasPrevious;
        private int checkerFrame;
        private float jitterPhase;
        private float previousFlash;
        internal float[] HeroStrengths => heroShown;

        /// <summary>0..1 how far the console fog bank has built up.</summary>
        internal float FogShown => fogShown;

        internal void Reset()
        {
            for (int i = 0; i < heroShown.Length; i++) { heroShown[i] = 0f; heroSite[i] = Vector2.zero; }
            fogShown = 0f;
            previousFlash = 0f;
            hasPrevious = false;
            previousCamera = null;
            previousTarget = null;
            checkerFrame = 0;
            jitterPhase = 0f;
        }

        /// <summary>Jump set-pieces and fog to their targets (the bench, or a preload).</summary>
        internal void Settle(WeatherField field)
        {
            for (int i = 0; i < heroShown.Length; i++)
            {
                if (i >= field.SuperstructureCount) { heroShown[i] = 0f; continue; }
                Superstructure s = field.SuperstructureAt(i);
                heroSite[i] = new Vector2(s.X, s.Z);
                heroShown[i] = s.Strength;
            }
            fogShown = (field.Key.Sets & Superstructures.FogBankSet) != 0 ? 1f : 0f;
        }

        internal void Apply(Material material, WeatherField field, in CloudFrame frame, Texture noise)
        {
            StateParams sky = field.Params;
            material.SetTexture(NoiseId, noise);
            material.SetFloat(BaseId, sky.CloudBase + frame.CloudShift);
            material.SetFloat(ShiftId, frame.CloudShift);
            material.SetVector(OffsetId, frame.WorldOffset);
            material.SetVector(ForwardId, frame.CameraForward);

            // Scenery storms: static set-pieces the state builds up. They keep their own altitude
            // range, so rays that miss them keep the weather's tight march bounds.
            int heroes = field.SuperstructureCount;
            Vector4 eye = Vector4.zero;
            float heroBottom = 1e6f, heroTop = -1e6f;
            for (int i = 0; i < heroA.Length; i++)
            {
                if (i >= heroes) { heroA[i] = Vector4.zero; heroB[i] = Vector4.zero; heroShown[i] = 0f; continue; }
                Superstructure s = field.SuperstructureAt(i);
                var site = new Vector2(s.X, s.Z);
                if (site != heroSite[i]) { heroSite[i] = site; heroShown[i] = 0f; }
                heroShown[i] = Mathf.MoveTowards(heroShown[i], s.Strength, frame.DeltaTime / 20f);
                heroA[i] = new Vector4(s.X, s.Z, s.Heading, (float)s.Kind);
                heroB[i] = new Vector4(s.Size, s.Top, heroShown[i], s.Extent);
                if (s.Kind == SuperstructureKind.StormEye) eye = new Vector4(s.X, s.Z, s.Size, heroShown[i]);
                // Set-pieces use absolute seeded heights in both CPU and shader bodies.
                // Native cloud-height changes shift the ordinary weather volume only.
                heroBottom = Mathf.Min(heroBottom, 300f);
                heroTop = Mathf.Max(heroTop, s.Top + 1500f);
            }
            // The console fog bank eases in and out like the set-pieces.
            fogShown = Mathf.MoveTowards(fogShown, (field.Key.Sets & Superstructures.FogBankSet) != 0 ? 1f : 0f,
                frame.DeltaTime / 20f);
            material.SetFloat(FogBankId, fogShown);
            material.SetVectorArray(HeroAId, heroA);
            material.SetVectorArray(HeroBId, heroB);
            material.SetFloat(HeroCountId, heroes);
            material.SetVector(EyeId, eye);
            material.SetVector(BoundsId, new Vector2(frame.Bottom + frame.CloudShift, frame.Top + frame.CloudShift));
            material.SetVector(HeroBoundsId, heroes > 0 ? new Vector2(heroBottom, heroTop) : Vector2.zero);
            // Static weather: the detail texture does not crawl either.
            material.SetVector(WindOffsetId, Vector2.zero);

            material.SetVector(SunDirId, frame.SunDirection.normalized);
            material.SetColor(SunColorId, frame.SunColor * 0.75f);
            material.SetColor(AmbientId, frame.Ambient);
            material.SetColor(GroundId, frame.Ground);
            material.SetColor(FogId, frame.Fog);
            material.SetFloat(ExtinctionId, frame.Extinction);
            material.SetFloat(FlashId, frame.Flash);
            material.SetVector(FlashAId, frame.FlashA);
            material.SetVector(FlashBId, frame.FlashB);
            Shader.SetGlobalFloat(FlashChangeId, Mathf.Abs(frame.Flash - previousFlash));
            previousFlash = frame.Flash;
            material.SetFloat(StormId, sky.Severity);
            material.SetFloat(RainVisualsId, frame.RainVisualsEnabled ? 1f : 0f);
            // Cloud genera for the current state (they fade with it).
            material.SetFloat(LayerDepthId, sky.LayerDepth);
            material.SetFloat(LayerSmoothId, sky.LayerSmooth);
            material.SetFloat(MidCoverId, sky.MidCover);
            material.SetFloat(MidSheetId, sky.MidSheet);
            material.SetFloat(HighCoverId, sky.HighCover);
            material.SetFloat(HighVeilId, sky.HighVeil);
            CloudGenus genus = CloudShape.Resolve(sky);
            material.SetFloat(PuffScaleId, genus.PuffScale);
            material.SetFloat(PuffDepthId, genus.PuffDepth);
            material.SetFloat(BaseSharpId, genus.BaseSharp);
            material.SetFloat(BaseWobbleId, genus.BaseWobble);
            material.SetFloat(DomeId, genus.Dome);
            material.SetFloat(BillowId, genus.Billow);
            material.SetFloat(AnvilId, genus.Anvil);
            WeatherMath.HeadingToVector(field.PrevailingHeading, out float windX, out float windZ);
            material.SetVector(WindDirId, new Vector2(windX, windZ));
            // Horizon deck: the far ring's cover, plus a distant band of cumulus in fair skies.
            // An overcast deck continues past the march: rays that reach its base beyond 220 km
            // (just above the horizon under or inside it) otherwise showed a bright sky sliver.
            // The shader still opens it ahead of a frontal boundary (SplitShare).
            material.SetFloat(HorizonCoverId, Mathf.Clamp01(Mathf.Max(frame.HorizonCover,
                Mathf.Max(sky.Cumulus * 0.3f + sky.Convective * 0.2f, sky.Overcast * 0.9f))));
            material.SetFloat(HorizonDeckId, sky.CloudBase + frame.CloudShift + Mathf.Max(300f, sky.LayerDepth) * 0.4f);
            material.SetFloat(HorizonDepthId, Mathf.Max(300f, sky.LayerDepth));
            // The frontal boundary: one side of the map under the deck, the other opening up.
            SkySplit split = field.Split;
            material.SetVector(SplitAId, new Vector4(split.NormalX, split.NormalZ, split.Offset, SkySplit.Width));
            material.SetVector(SplitBId, new Vector4(split.Amount, split.MeanderAmplitude,
                Mathf.Max(1000f, split.MeanderWavelength), split.MeanderPhase));
            material.SetFloat(PixelAngleId,
                2f * Mathf.Tan(frame.FieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, frame.PixelHeight));
            // 64 near steps: temporal accumulation averages the per-frame dither, which
            // also supersamples the step quantization over frames, so 64 accumulated steps
            // resolve as well as 80 static ones did (see the bench PNGs).
            material.SetFloat(StepsId, frame.LowDetail ? 56f : 64f);
            material.SetFloat(FarStepsId, frame.LowDetail ? 14f : 20f);
        }

        internal static void ApplySpans(Material material, float mapHalf, float farHalf)
        {
            material.SetFloat(MapSpanId, mapHalf * 2f);
            material.SetFloat(FarSpanId, farHalf * 2f);
        }

        /// <summary>At render time, with the camera's final pose: the reduced-resolution march's
        /// view (rays through the four screen corners, as texture uv: bottom-left, bottom-right,
        /// top-left, top-right), the checkerboard texel this frame marches (xy) with the
        /// per-frame dither offset (z), and last frame's view for reprojection.
        /// <paramref name="globalPosition"/> is the camera in the map frame (unaffected by
        /// floating-origin shifts). Returns false when last frame's view cannot be reused
        /// (first frame, a cut, a zoom).</summary>
        internal bool ApplyView(Camera camera, Vector3 globalPosition, Material material = null)
            => ApplyView(camera, globalPosition, camera.worldToCameraMatrix, camera.projectionMatrix, material);

        internal bool ApplyView(Camera camera, Vector3 globalPosition, Matrix4x4 view,
            Matrix4x4 projection, Material material = null)
        {
            // Reconstruct the view URP actually rendered. TransformVector includes parent
            // scale, which Unity's camera matrix ignores, and misses overridden views.
            Matrix4x4 inverseView = view.inverse, inverseProjection = projection.inverse;
            Transform t = camera.transform;
            var m = new Matrix4x4();
            for (int i = 0; i < 4; i++)
            {
                Vector3 corner = inverseProjection.MultiplyPoint(new Vector3((i & 1) == 0 ? -1f : 1f,
                    i < 2 ? -1f : 1f, -1f));
                m.SetRow(i, inverseView.MultiplyVector(corner / -corner.z));
            }
            Vector3 origin = inverseView.GetColumn(3);
            Vector3 forward = inverseView.MultiplyVector(Vector3.back).normalized;
            Quaternion rotation = Quaternion.LookRotation(forward, inverseView.GetColumn(1));
            Vector3 worldOffset = globalPosition - t.position;
            Vector3 renderedGlobalPosition = origin + worldOffset;
            Shader.SetGlobalMatrix(FrustumId, m);
            Shader.SetGlobalVector(CameraPosId, origin);
            if (material != null)
            {
                material.SetVector(OffsetId, worldOffset);
                material.SetVector(ForwardId, forward);
            }

            // Last frame's rotation and projection (OpenGL convention: uv v up, like the
            // targets), applied to points relative to last frame's camera.
            Vector3 delta = renderedGlobalPosition - previousPosition;
            // Rays reproject exactly under rotation. Preserve accumulation on ordinary pans;
            // the previous 0.5-degree threshold discarded it and exposed a quarter-size grid.
            float rotAngle = hasPrevious ? Quaternion.Angle(rotation, previousQuat) : 180f;
            float rotMotion = hasPrevious ? Mathf.Clamp01(rotAngle * 0.15f) : 1f;
            bool sameProjection = true;
            for (int i = 0; i < 16; i++)
                if (Mathf.Abs(projection[i] - previousProjection[i]) > 0.0001f) { sameProjection = false; break; }
            bool reusable = hasPrevious && previousCamera == camera && previousTarget == camera.targetTexture &&
                previousRect == camera.pixelRect && sameProjection && delta.sqrMagnitude < 2000f * 2000f &&
                Mathf.Abs(camera.fieldOfView - previousFov) < 0.5f && rotAngle < 45f;
            Shader.SetGlobalMatrix(PrevMatrixId, previousProjection * previousRotation);
            Shader.SetGlobalVector(CamDeltaId, new Vector4(delta.x, delta.y, delta.z, rotMotion));
            Vector2 offset = CheckerOrder[checkerFrame & 3];
            // The march dither cycles each frame (golden-ratio steps never line up with the
            // 4-frame checkerboard period), so temporal accumulation converges to smooth cloud.
            jitterPhase = (jitterPhase + 0.6180339887f) % 1f;
            Shader.SetGlobalVector(CheckerId, new Vector4(offset.x, offset.y, jitterPhase, reusable ? 1f : 0f));
            checkerFrame++;
            previousRotation = view;
            previousRotation.SetColumn(3, new Vector4(0f, 0f, 0f, 1f));
            previousPosition = renderedGlobalPosition;
            previousQuat = rotation;
            previousFov = camera.fieldOfView;
            previousProjection = projection;
            previousCamera = camera;
            previousTarget = camera.targetTexture;
            previousRect = camera.pixelRect;
            hasPrevious = true;
            return reusable;
        }
    }
}
