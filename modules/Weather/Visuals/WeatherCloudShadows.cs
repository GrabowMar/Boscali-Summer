using System;
using System.Threading;
using BoscaliSummer.Features.Weather.Domain;
using BoscaliSummer.Runtime;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Features.Weather.Visuals
{
    /// <summary>Cached cloud transmission through the native directional-light cookie.
    /// A cookie cannot distinguish receiver altitude: fade it as the observer climbs
    /// through the local deck so aircraft above clouds do not retain ground shadows.</summary>
    internal sealed class WeatherCloudShadows
    {
        private const int Size = 128;
        private const float RefreshSeconds = 30f;
        private const float UploadSeconds = 0.25f;
        private readonly Color32[] blended = new Color32[Size * Size];
        private readonly Color32[] previous = new Color32[Size * Size];
        private Texture2D texture;
        private Color32[] target;
        private LevelInfo owner;
        private LightState sun, moon;
        private WeatherKey key;
        private Vector3 projectedDirection;
        private float span, nextBuild, nextUpload, blendStart, requestedTime, heightShift;
        private float blendSeconds = RefreshSeconds;
        private int revision, generating, requestedStep = -1;
        private ShadowResult ready;
        private string failureReason;
        private bool attached;

        internal bool Active => attached && texture != null;
        internal int UpdateCount { get; private set; }
        internal string FailureReason => Volatile.Read(ref failureReason);

        internal void Update(LevelInfo level, WeatherField field, float currentCloudHeight)
        {
            if (Application.isBatchMode || level == null || field == null || !field.IsBuilt ||
                level.sun == null || level.moon == null || level.SunURPLightData == null ||
                level.MoonURPLightData == null)
            {
                Hide();
                return;
            }
            if (owner != level)
            {
                Hide();
                owner = level;
                sun = new LightState(level.sun, level.SunURPLightData);
                moon = new LightState(level.moon, level.MoonURPLightData);
                ResetPixels();
            }
            Light light = Visible(level.sun) ? level.sun : Visible(level.moon) ? level.moon : null;
            if (light == null)
            {
                // No light is up (vanilla kills the sun under overcast while the moon stays
                // dark by day). Hold the last projection instead of hiding: restoring the
                // stale native cookie would flash the ground bright until the next rebuild.
                return;
            }

            float now = Time.unscaledTime;
            float shift = currentCloudHeight - field.Regional().CloudBase;
            // A returning sun resumes on its old projection without a white flash: the
            // direction check below already catches genuine sun/moon basis changes.
            bool changed = key == null || !key.Equals(field.Key) ||
                Vector3.Dot(projectedDirection, light.transform.forward) < 0.9986f ||
                Math.Abs(shift - heightShift) > 250f || field.Time < requestedTime - 1f;
            if (changed)
            {
                Interlocked.Increment(ref revision);
                Interlocked.Exchange(ref ready, null);
                key = field.Key;
                projectedDirection = light.transform.forward;
                heightShift = shift;
                nextBuild = 0f;
                // A different mission/key/light must never inherit the old projection.
                ResetPixels();
            }

            ShadowResult result = Interlocked.Exchange(ref ready, null);
            if (result != null && result.Revision == Volatile.Read(ref revision))
            {
                // Previous is the un-faded transmission, independent of observer altitude.
                float oldBlend = Mathf.Clamp01((now - blendStart) / blendSeconds);
                for (int i = 0; i < previous.Length; i++)
                {
                    byte value = target == null ? (byte)255 : LerpByte(previous[i].r, target[i].r, oldBlend);
                    previous[i] = new Color32(value, value, value, 255);
                }
                blendSeconds = target == null ? 3f : RefreshSeconds;
                target = result.Pixels;
                span = result.Span;
                blendStart = now;
                nextUpload = 0f;
                UpdateCount++;
            }
            float elevation = -light.transform.forward.y;
            // A new weather step rebuilds at once; within a step the sky is static.
            if (field.Timeline.Step != requestedStep) nextBuild = 0f;
            if (now >= nextBuild && elevation > 0.06f && Volatile.Read(ref generating) == 0)
            {
                Queue(field, light, shift);
                requestedTime = field.Time;
                requestedStep = field.Timeline.Step;
                nextBuild = now + RefreshSeconds;
            }

            if (texture == null)
            {
                texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true)
                {
                    name = "Boscali Cloud Sun Transmission", filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave
                };
                nextUpload = 0f;
            }
            if (now >= nextUpload)
            {
                float strength = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.06f, 0.20f, elevation));
                Camera camera = SceneSingleton<CameraStateManager>.i?.mainCamera;
                if (camera != null)
                {
                    GlobalPosition position = camera.transform.GlobalPosition();
                    WeatherPoint local = field.Sample((float)position.x, (float)position.z);
                    float bottom = local.CloudBase + shift;
                    float top = Math.Max(bottom + 500f, local.CloudTop + shift);
                    strength *= 1f - Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(bottom, top, (float)position.y));
                }
                float blend = Mathf.Clamp01((now - blendStart) / blendSeconds);
                for (int i = 0; i < blended.Length; i++)
                {
                    float value = target == null ? 255f : Mathf.Lerp(previous[i].r, target[i].r, blend);
                    byte transmit = (byte)Mathf.RoundToInt(Mathf.Lerp(255f, value, strength));
                    blended[i] = new Color32(transmit, transmit, transmit, 255);
                }
                texture.SetPixels32(blended);
                texture.Apply(false, false);
                nextUpload = now + UploadSeconds;
            }

            // SetCookie normalizes the sun's roll. Match that basis before computing
            // the offset. URP uses (inverseLightPosition.xy - offset) / size + 0.5;
            // positive offset therefore places the cookie center at this local point.
            level.sun.transform.rotation = Quaternion.LookRotation(level.sun.transform.forward, Vector3.up);
            Vector3 anchor = light.transform.InverseTransformPoint(Datum.originPosition);
            level.SetCookie(texture, Math.Max(1000f, span), new Vector2(anchor.x, anchor.y));
            attached = true;
        }

        internal void Hide()
        {
            if (owner == null && !attached && key == null) return;
            Interlocked.Increment(ref revision);
            Interlocked.Exchange(ref ready, null);
            sun.Restore(texture);
            moon.Restore(texture);
            sun = default;
            moon = default;
            owner = null;
            key = null;
            attached = false;
            target = null;
            nextBuild = 0f;
        }

        internal void Restore()
        {
            Hide();
            if (texture != null) UnityEngine.Object.Destroy(texture);
            texture = null;
        }

        private void ResetPixels()
        {
            target = null;
            for (int i = 0; i < previous.Length; i++) previous[i] = new Color32(255, 255, 255, 255);
            nextUpload = 0f;
        }

        private void Queue(WeatherField field, Light light, float shift)
        {
            if (Interlocked.CompareExchange(ref generating, 1, 0) != 0) return;
            int requestRevision = Volatile.Read(ref revision);
            WeatherKey requestKey = field.Key;
            float time = field.Time, halfX = field.HalfX, halfZ = field.HalfZ;
            float hour = field.HourOfDay, haze = field.HazeScale;
            float requestSpan = 2f * Math.Max(80000f, Math.Max(halfX, halfZ) + 45000f);
            // SetCookie aligns sun roll to world-up; the moon retains its actual basis.
            Quaternion rotation = light == owner.sun
                ? Quaternion.LookRotation(light.transform.forward, Vector3.up) : light.transform.rotation;
            Vector3 scale = light.transform.lossyScale;
            Vector3 right = rotation * Vector3.right * scale.x;
            Vector3 up = rotation * Vector3.up * scale.y;
            Vector3 direction = light.transform.forward;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var snapshot = new WeatherField();
                    snapshot.Build(requestKey, time, halfX, halfZ, hour, haze);
                    var pixels = new Color32[Size * Size];
                    const int samples = 12;
                    float baseY = snapshot.Params.CloudBase + shift;
                    float lower = Math.Max(0f, baseY - 300f), upper = 16000f + Math.Max(0f, shift);
                    float dy = (upper - lower) / samples;
                    float rayStep = dy / Math.Max(0.06f, -direction.y);
                    for (int y = 0; y < Size; y++)
                    {
                        if (requestRevision != Volatile.Read(ref revision)) return;
                        for (int x = 0; x < Size; x++)
                        {
                            Vector3 plane = right * (((x + 0.5f) / Size - 0.5f) * requestSpan) +
                                up * (((y + 0.5f) / Size - 0.5f) * requestSpan);
                            float depth = 0f;
                            for (int s = 0; s < samples && depth < 4f; s++)
                            {
                                float altitude = lower + (s + 0.5f) * dy;
                                float t = (altitude - plane.y) / direction.y;
                                float wx = plane.x + direction.x * t, wz = plane.z + direction.z * t;
                                depth += CoarseDensity(snapshot.Sample(wx, wz), altitude, baseY, shift) * rayStep * 0.0012f;
                            }
                            // A soft white border prevents a repeating or clamped dark horizon.
                            float edge = Math.Min(Math.Min(x, Size - 1 - x), Math.Min(y, Size - 1 - y)) / 4f;
                            float shade = 0.57f * (1f - (float)Math.Exp(-depth)) * WeatherMath.Clamp01(edge);
                            byte value = (byte)Math.Round((1f - shade) * 255f);
                            pixels[y * Size + x] = new Color32(value, value, value, 255);
                        }
                    }
                    if (requestRevision == Volatile.Read(ref revision))
                    {
                        Volatile.Write(ref failureReason, null);
                        Interlocked.Exchange(ref ready, new ShadowResult(requestRevision, requestSpan, pixels));
                    }
                }
                catch (Exception error)
                {
                    if (requestRevision == Volatile.Read(ref revision))
                        Volatile.Write(ref failureReason, error.GetType().Name + ": " + error.Message);
                }
                finally { Interlocked.Exchange(ref generating, 0); }
            });
        }

        // Coarse counterpart of the volume's layer/front/cell profiles. No detail noise:
        // this cookie supplies broad ground shading; the volume shades its own fine edges.
        private static float CoarseDensity(WeatherPoint p, float y, float baseY, float shift)
        {
            float flat = WeatherMath.Smoothstep(-100f, 170f, y - baseY);
            // The deck is separate bodies: on average it blocks its cover fraction of the sun.
            float layer = WeatherMath.Clamp01(p.BackgroundCover * 1.1f) * flat *
                (1f - WeatherMath.Smoothstep(baseY + 1200f, baseY + 1850f, y)) * 0.52f;
            float front = WeatherMath.Smoothstep(0.20f, 0.56f, p.FrontCover) *
                WeatherMath.Smoothstep(-100f, 170f, y - p.FrontBase - shift) *
                (1f - WeatherMath.Smoothstep(p.FrontTop + shift - 500f, p.FrontTop + shift + 250f, y)) * 0.58f;
            float top = Math.Max(baseY + 1600f, p.CloudTop + shift);
            float height = WeatherMath.Clamp01((y - baseY) / Math.Max(1f, top - baseY));
            float threshold = 0.25f + 0.43f * height * height;
            float cell = WeatherMath.Smoothstep(threshold - 0.16f, threshold + 0.16f, p.CellShape) *
                flat * (1f - WeatherMath.Smoothstep(0.84f, 1f, height)) * 0.78f;
            return Math.Max(layer, Math.Max(front, cell));
        }

        private static bool Visible(Light light) => light.isActiveAndEnabled && light.intensity > 0f;
        private static byte LerpByte(byte a, byte b, float t) => (byte)Mathf.RoundToInt(Mathf.Lerp(a, b, t));

        private readonly struct LightState
        {
            private readonly Light light;
            private readonly UniversalAdditionalLightData data;
            private readonly Texture cookie;
            private readonly Vector2 size, offset;
            internal LightState(Light light, UniversalAdditionalLightData data)
            {
                this.light = light; this.data = data; cookie = light.cookie;
                size = data.lightCookieSize; offset = data.lightCookieOffset;
            }
            internal void Restore(Texture ownedCookie)
            {
                if (light == null || data == null) return;
                // SetCookie also clears the inactive light's cookie. Restore both members
                // only while they still have values that our ownership can have written.
                if (light.cookie != ownedCookie && light.cookie != null) return;
                light.cookie = cookie;
                data.lightCookieSize = size;
                data.lightCookieOffset = offset;
            }
        }

        private sealed class ShadowResult
        {
            internal readonly int Revision;
            internal readonly float Span;
            internal readonly Color32[] Pixels;
            internal ShadowResult(int revision, float span, Color32[] pixels)
            { Revision = revision; Span = span; Pixels = pixels; }
        }
    }
}
