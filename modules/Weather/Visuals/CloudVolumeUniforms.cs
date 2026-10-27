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
        private BakedTape boundsTape;
        private int boundsCell;
        private float boundsExpiry, boundsBottomCache, boundsTopCache;
        private static readonly int NoiseId = Shader.PropertyToID("_CloudNoiseTex");
        private static readonly int MapSpanId = Shader.PropertyToID("_WeatherMapSpan");
        private static readonly int FarSpanId = Shader.PropertyToID("_WeatherFarSpan");
        private static readonly int BaseId = Shader.PropertyToID("_CloudBase");
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
        private static readonly int RainVisualsId = Shader.PropertyToID("_CloudRainVisuals");
        // The weather tape (TapeTexture, BakedTape): the shader reads every per-sample genus and thin-layer column from it.
        private static readonly int TapeTexId = Shader.PropertyToID("_TapeTex");
        private static readonly int TapeInfoId = Shader.PropertyToID("_TapeInfo");
        private static readonly int TapeFrameId = Shader.PropertyToID("_TapeFrame");
        private static readonly int TapeFrame2Id = Shader.PropertyToID("_TapeFrame2");
        private static readonly int TapeChordId = Shader.PropertyToID("_TapeChord");
        private static readonly int TapeFlagsId = Shader.PropertyToID("_TapeFlags");
        // The low genus of a one-texel tape rides in uniforms (live play in slices 2-3), so a sample costs no texture fetch for it.
        private static readonly int[] TapeConstLowIds =
        {
            Shader.PropertyToID("_TapeConstLow0"), Shader.PropertyToID("_TapeConstLow1"), Shader.PropertyToID("_TapeConstLow2"),
        };
        // The noise decorrelation of the tape (NoiseXform): one (cos, sin, offset x, offset z) per noise layer.
        private static readonly int[] NoiseXfIds =
        {
            Shader.PropertyToID("_NoiseXf0"), Shader.PropertyToID("_NoiseXf1"), Shader.PropertyToID("_NoiseXf2"),
            Shader.PropertyToID("_NoiseXf3"), Shader.PropertyToID("_NoiseXf4"), Shader.PropertyToID("_NoiseXf5"),
        };
        private static readonly int WindDirId = Shader.PropertyToID("_CloudWindDir");
        private static readonly int HorizonDeckId = Shader.PropertyToID("_HorizonDeck");
        private static readonly int PixelAngleId = Shader.PropertyToID("_CloudPixelAngle");
        private static readonly int StepsId = Shader.PropertyToID("_CloudSteps");
        private static readonly int FarStepsId = Shader.PropertyToID("_CloudFarSteps");
        private static readonly int FrustumId = Shader.PropertyToID("_CloudFrustum");
        private static readonly int PrevMatrixId = Shader.PropertyToID("_CloudPrevMatrix");
        private static readonly int CamDeltaId = Shader.PropertyToID("_CloudCamDelta");
        private static readonly int CheckerId = Shader.PropertyToID("_CloudChecker");
        private static readonly Vector2[] CheckerOrder = { new Vector2(0, 0), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 1) };

        private readonly TapeTexture tapeTexture = new TapeTexture();
        private readonly Vector4[] tapeConstLow = new Vector4[3];
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
        private float previousFlash;
        internal float[] HeroStrengths => heroShown;

        /// <summary>0..1 how far the console fog bank has built up.</summary>
        internal float FogShown => fogShown;

        internal void Reset()
        {
            tapeTexture.Dispose();
            for (int i = 0; i < heroShown.Length; i++) { heroShown[i] = 0f; heroSite[i] = Vector2.zero; }
            fogShown = 0f;
            previousFlash = 0f;
            hasPrevious = false;
            previousCamera = null;
            previousTarget = null;
            checkerFrame = 0;
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

        /// <summary>
        /// Binds the field's tape: the texture, its frame, the slab chords and which thin layers exist (spec 7.1: bound with the
        /// other per-frame uniforms, never from a pass Execute). Returns false when the float texture cannot be made.
        /// </summary>
        internal bool BindTape(Material material, WeatherField field)
        {
            BakedTape tape = field.Tape.Tape;
            if (tape == null || !tapeTexture.Update(tape)) return false;
            TapeFrame f = field.Frame;
            material.SetTexture(TapeTexId, tapeTexture.Texture);
            material.SetVector(TapeInfoId, new Vector4(tape.Texels, 1f / tape.Texels, 1f / tape.TexelMetres, 0f));
            material.SetVector(TapeFrameId, new Vector4(f.UX, f.UZ, f.Amplitude, Mathf.Max(1f, f.Wavelength)));
            material.SetVector(TapeFrame2Id, new Vector4(f.Phase, f.Offset, f.Noise.StreetSpacing, f.Noise.StreetPhase));
            NoiseXform noise = f.Noise;
            for (int layer = 0; layer < NoiseXform.Layers; layer++)
                material.SetVector(NoiseXfIds[layer], new Vector4(noise.Cos(layer), noise.Sin(layer), noise.OffsetX(layer), noise.OffsetZ(layer)));
            material.SetVector(TapeChordId, new Vector4(tape.MidChord0, tape.MidChord1, tape.HighChord0, tape.HighChord1));
            bool constant = tape.IsConstant;
            if (constant)
            {
                for (int i = 0; i < 3; i++) tapeConstLow[i] = Vector4.zero;
                for (int c = 0; c < SkyColumns.Count; c++)
                {
                    int row = SkyColumns.GpuRow[c];
                    if (row < 0 || row > 2) continue;
                    Vector4 v = tapeConstLow[row];
                    float value = tape.Data[c];
                    switch (SkyColumns.GpuChannel[c])
                    {
                        case 0: v.x = value; break;
                        case 1: v.y = value; break;
                        case 2: v.z = value; break;
                        default: v.w = value; break;
                    }
                    tapeConstLow[row] = v;
                }
                for (int i = 0; i < 3; i++) material.SetVector(TapeConstLowIds[i], tapeConstLow[i]);
            }
            material.SetVector(TapeFlagsId, new Vector4(tape.MidPresent ? 1f : 0f, tape.HighPresent ? 1f : 0f, constant ? 1f : 0f, 0f));
            return true;
        }

        /// <summary>
        /// The march's altitude bounds from the baked tape (spec 7.3): the far bounds around the camera's s, a superset of the near ones. A held tape's bounds change
        /// only with s; the rise-instantly / fall-at-200 m/s smoothing is slice 5's. Clamped to the volume and never thinner than 1 km (an empty sky still marches).
        /// </summary>
        internal static void FarBounds(WeatherField field, Vector3 cameraGlobal, out float bottom, out float top)
        {
            BakedTape tape = field.Tape.Tape;
            if (tape == null) { bottom = 0f; top = CloudMaps.VolumeTop; return; }
            TapeBounds.Compute(tape, field.Frame.S(cameraGlobal.x, cameraGlobal.z), TapeBounds.FarHalfWidth, out bottom, out top);
            bottom = Mathf.Max(-30f, bottom);
            top = Mathf.Min(CloudMaps.VolumeTop, Mathf.Max(top, bottom + 1000f));
        }

        internal void Apply(Material material, WeatherField field, in CloudFrame frame, Texture noise)
        {
            SkyParams sky = field.RowAt(0f, 0f);
            material.SetTexture(NoiseId, noise);
            material.SetFloat(BaseId, sky.CloudBase);
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
            // The tape walk is per-frame cost for an answer that only changes when the camera crosses a tape texel (or the tape is replaced): reuse it between, refresh at least every 2 s.
            Vector3 boundsCamera = frame.CameraPosition + frame.WorldOffset;
            BakedTape boundsSource = field.Tape.Tape;
            int boundsTexel = boundsSource == null ? int.MinValue : Mathf.FloorToInt(field.Frame.S(boundsCamera.x, boundsCamera.z) / boundsSource.TexelMetres);
            if (!ReferenceEquals(boundsSource, boundsTape) || boundsTexel != boundsCell || Time.unscaledTime >= boundsExpiry)
            {
                FarBounds(field, boundsCamera, out boundsBottomCache, out boundsTopCache);
                boundsTape = boundsSource; boundsCell = boundsTexel; boundsExpiry = Time.unscaledTime + 2f;
            }
            float boundsBottom = boundsBottomCache, boundsTop = boundsTopCache;
            material.SetVector(BoundsId, new Vector2(boundsBottom, boundsTop));
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
            material.SetFloat(RainVisualsId, frame.RainVisualsEnabled ? 1f : 0f);
            // The low genus of every sample is a row of the field's tape (it fades with the state).
            BindTape(material, field);
            // The thin layers are rows of the same tape (BindTape above).

            WeatherMath.HeadingToVector(field.PrevailingHeading, out float windX, out float windZ);
            material.SetVector(WindDirId, new Vector2(windX, windZ));
            // Horizon deck (spec 7.3): one plane at the tape's deck altitude, fixed per tape; cover and depth per hit come from the tape rows in the shader.
            material.SetFloat(HorizonDeckId, field.Tape.Tape.DeckAltitude);

            material.SetFloat(PixelAngleId,
                2f * Mathf.Tan(frame.FieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, frame.PixelHeight));
            // 2026-10-26 (user: put more resources into clouds): 96 near steps (the shader's cap) and 32 far. The dither is frozen per
            // texel since 2026-10-14, so accumulation no longer supersamples the steps, and the noise footprint follows the step length:
            // shorter steps are directly sharper cloud.
            material.SetFloat(StepsId, frame.LowDetail ? 72f : 96f);
            material.SetFloat(FarStepsId, frame.LowDetail ? 20f : 32f);
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

        /// <summary>World rays through the four corners of a view, one per row (bottom-left, bottom-right, top-left, top-right), each of unit length along the camera axis.</summary>
        internal static Matrix4x4 FrustumRays(Matrix4x4 inverseView, Matrix4x4 inverseProjection)
        {
            var m = new Matrix4x4();
            for (int i = 0; i < 4; i++)
            {
                Vector3 corner = inverseProjection.MultiplyPoint(new Vector3((i & 1) == 0 ? -1f : 1f,
                    i < 2 ? -1f : 1f, -1f));
                m.SetRow(i, inverseView.MultiplyVector(corner / -corner.z));
            }
            return m;
        }

        internal bool ApplyView(Camera camera, Vector3 globalPosition, Matrix4x4 view,
            Matrix4x4 projection, Material material = null)
        {
            // Reconstruct the view URP actually rendered. TransformVector includes parent
            // scale, which Unity's camera matrix ignores, and misses overridden views.
            Matrix4x4 inverseView = view.inverse, inverseProjection = projection.inverse;
            Transform t = camera.transform;
            Matrix4x4 m = FrustumRays(inverseView, inverseProjection);
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
            // z (the old per-frame dither phase) is unused: the march dither is frozen per texel.
            Shader.SetGlobalVector(CheckerId, new Vector4(offset.x, offset.y, 0f, reusable ? 1f : 0f));
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
