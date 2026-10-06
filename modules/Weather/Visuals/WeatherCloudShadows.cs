using System;
using System.Threading;
using BoscaliSummer.Modules.Weather.Domain;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Modules.Weather.Visuals
{
    /// <summary>Cloud shadows through the native directional-light cookie: a camera-centred,
    /// 48 km, 256-square transmission map computed from the same cloud bodies the volume
    /// shader draws, so each cloud casts its own shadow on terrain, sea and aircraft.
    /// Rebuilt on a worker when the camera moves far, the sun turns, or the weather steps;
    /// the weather is static in between, so nothing needs rebuilding while it holds.</summary>
    internal sealed class WeatherCloudShadows
    {
        private const int Size = 256;
        private const float Span = 48000f;
        private const float RecentreDistance = 9000f;
        private const float SettlingRefreshSeconds = 20f;
        private const float BlendSeconds = 4f;
        private const float UploadSeconds = 0.25f;
        private readonly Color32[] blended = new Color32[Size * Size];
        private readonly byte[] previous = new byte[Size * Size];
        private byte[] target;
        private Texture2D texture;
        private LevelInfo owner;
        private LightState sun, moon;
        private WeatherKey key;
        private int step = -1;
        private Vector3 projectedDirection;
        private double centreX, centreZ;
        private float nextBuild, nextUpload, blendStart, heightShift;
        private int revision, generating;
        private ShadowResult ready;
        private string failureReason;
        private bool attached;
        private float uploadedStrength = -1f, uploadedBlend = -1f;

        internal bool Active => attached && texture != null;
        internal int UpdateCount { get; private set; }
        internal string FailureReason => Volatile.Read(ref failureReason);

        internal void Update(LevelInfo level, WeatherField field, float currentCloudHeight, Camera camera)
        {
            if (Application.isBatchMode || level == null || field == null || !field.IsBuilt || camera == null ||
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
            Light light = WeatherLighting.Source(level);
            // No light up (vanilla kills the sun under overcast): hold the last projection
            // rather than restoring the native cookie and flashing the ground bright.
            if (light == null) return;

            float now = Time.unscaledTime;
            float shift = currentCloudHeight - field.Regional().CloudBase;
            GlobalPosition eye = camera.transform.GlobalPosition();
            bool keyChanged = key == null || !key.Equals(field.Key);
            bool turned = Vector3.Dot(projectedDirection, light.transform.forward) < 0.9986f;
            if (keyChanged || turned || Math.Abs(shift - heightShift) > 250f)
            {
                Interlocked.Increment(ref revision);
                Interlocked.Exchange(ref ready, null);
                key = field.Key;
                projectedDirection = light.transform.forward;
                heightShift = shift;
                step = -1;
                nextBuild = 0f;
            }
            // A new weather step, a drift away from the centre, or a still-settling sky rebuilds.
            if (field.Timeline.Step != step) nextBuild = 0f;
            double dx = eye.x - centreX, dz = eye.z - centreZ;
            if (target == null || dx * dx + dz * dz > RecentreDistance * RecentreDistance) nextBuild = 0f;

            ShadowResult result = Interlocked.Exchange(ref ready, null);
            if (result != null && result.Revision == Volatile.Read(ref revision))
            {
                bool moved = result.CentreX != centreX || result.CentreZ != centreZ || target == null;
                // Same clouds, same centre: fade. A recentred map shows the same static
                // clouds, so it swaps in at once without a visible change.
                float oldBlend = moved ? 1f : Mathf.Clamp01((now - blendStart) / BlendSeconds);
                for (int i = 0; i < previous.Length; i++)
                    previous[i] = moved ? result.Pixels[i] : LerpByte(previous[i], target[i], oldBlend);
                target = result.Pixels;
                centreX = result.CentreX;
                centreZ = result.CentreZ;
                blendStart = now;
                nextUpload = 0f;
                UpdateCount++;
            }

            float elevation = -light.transform.forward.y;
            if (now >= nextBuild && elevation > 0.06f && Volatile.Read(ref generating) == 0)
            {
                Queue(field, light, shift, eye.x, eye.z);
                step = field.Timeline.Step;
                bool settling = field.Key.Dynamic &&
                    field.Time < WeatherTimeline.SettledAt(field.Key, field.Timeline);
                nextBuild = settling ? now + SettlingRefreshSeconds : float.PositiveInfinity;
            }

            if (texture == null)
            {
                texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true)
                {
                    name = "Boscali Cloud Sun Transmission", filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave
                };
                nextUpload = 0f;
                uploadedStrength = uploadedBlend = -1f;
            }
            if (now >= nextUpload)
            {
                nextUpload = now + UploadSeconds;
                float strength = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.06f, 0.20f, elevation));
                WeatherPoint local = field.Sample((float)eye.x, (float)eye.z);
                float bottom = local.CloudBase + shift;
                float top = Math.Max(bottom + 500f, local.CloudTop + shift);
                // A cookie cannot tell receiver altitude: fade it as the observer climbs
                // through the deck so aircraft above the clouds lose the ground shadows.
                strength *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(bottom, top, (float)eye.y));
                float blend = Mathf.Clamp01((now - blendStart) / BlendSeconds);
                // The cookie is static between weather steps: rebuild its 64k pixels only
                // while a new map fades in or the strength has moved.
                bool changed = blend != uploadedBlend || Mathf.Abs(strength - uploadedStrength) > 0.004f;
                uploadedBlend = blend;
                if (changed) uploadedStrength = strength;
                if (changed)
                for (int i = 0; i < blended.Length; i++)
                {
                    float value = target == null ? 255f : Mathf.Lerp(previous[i], target[i], blend);
                    byte transmit = (byte)Mathf.RoundToInt(Mathf.Lerp(255f, value, strength));
                    blended[i] = new Color32(transmit, transmit, transmit, 255);
                }
                if (changed)
                {
                    texture.SetPixels32(blended);
                    texture.Apply(false, false);
                }
            }

            // SetCookie normalizes the sun's roll. Match that basis before computing the
            // offset. URP uses (inverseLightPosition.xy - offset) / size + 0.5, so the offset
            // is the cookie centre in light space.
            sun.BeforeWrite(true);
            moon.BeforeWrite(false);
            level.sun.transform.rotation = Quaternion.LookRotation(level.sun.transform.forward, Vector3.up);
            Vector3 centreLocal = new GlobalPosition((float)centreX, 0f, (float)centreZ).ToLocalPosition();
            Vector3 anchor = light.transform.InverseTransformPoint(centreLocal);
            level.SetCookie(texture, Span, new Vector2(anchor.x, anchor.y));
            sun.Wrote(true);
            moon.Wrote(false);
            attached = true;
        }

        internal void Hide()
        {
            if (owner == null && !attached && key == null) return;
            Interlocked.Increment(ref revision);
            Interlocked.Exchange(ref ready, null);
            sun.Restore();
            moon.Restore();
            sun = default;
            moon = default;
            owner = null;
            key = null;
            step = -1;
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
            for (int i = 0; i < previous.Length; i++) previous[i] = 255;
            nextUpload = 0f;
        }

        private void Queue(WeatherField field, Light light, float shift, double centreGlobalX, double centreGlobalZ)
        {
            byte[] noise = WeatherVolumeDressing.NoiseData;
            if (noise == null || noise.Length != WeatherVolumeDressing.NoiseTexels * 4) return;
            if (Interlocked.CompareExchange(ref generating, 1, 0) != 0) return;
            int requestRevision = Volatile.Read(ref revision);
            WeatherKey requestKey = field.Key;
            float time = field.Time, halfX = field.HalfX, halfZ = field.HalfZ;
            float hour = field.HourOfDay, haze = field.HazeScale;
            // SetCookie aligns sun roll to world-up; the moon retains its actual basis.
            Quaternion rotation = light == owner.sun
                ? Quaternion.LookRotation(light.transform.forward, Vector3.up) : light.transform.rotation;
            Vector3 scale = light.transform.lossyScale;
            Vector3 right = rotation * Vector3.right * scale.x;
            Vector3 up = rotation * Vector3.up * scale.y;
            Vector3 direction = light.transform.forward;
            float cx = (float)centreGlobalX, cz = (float)centreGlobalZ;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var snapshot = new WeatherField();
                    snapshot.Build(requestKey, time, halfX, halfZ, hour, haze);
                    var body = new CloudBodies(noise, WeatherVolumeDressing.NoiseSize, snapshot.Params, snapshot.PrevailingHeading, snapshot.Split, 0f, snapshot);
                    var pixels = new byte[Size * Size];
                    const int samples = 14;
                    float baseY = snapshot.Params.CloudBase + shift;
                    float lower = Math.Max(0f, baseY - 800f), upper = 12000f + Math.Max(0f, shift);
                    float dy = (upper - lower) / samples;
                    float rayStep = dy / Math.Max(0.06f, -direction.y);
                    for (int y = 0; y < Size; y++)
                    {
                        if (requestRevision != Volatile.Read(ref revision)) return;
                        for (int x = 0; x < Size; x++)
                        {
                            // The plane through the centre, perpendicular to the light.
                            float u = ((x + 0.5f) / Size - 0.5f) * Span, v = ((y + 0.5f) / Size - 0.5f) * Span;
                            float px = cx + right.x * u + up.x * v, py = right.y * u + up.y * v;
                            float pz = cz + right.z * u + up.z * v;
                            // One weather sample where the ray crosses the deck; cloud bodies
                            // vary along the ray, the weather map barely does over a few km.
                            float deckT = (baseY + 700f - py) / direction.y;
                            WeatherPoint p = snapshot.Sample(px + direction.x * deckT, pz + direction.z * deckT);
                            float depth = 0f;
                            for (int s = 0; s < samples && depth < 4f; s++)
                            {
                                float altitude = lower + (s + 0.5f) * dy;
                                float t = (altitude - py) / direction.y;
                                float wx = px + direction.x * t, wz = pz + direction.z * t;
                                depth += body.Density(p, wx, altitude, wz, shift) * rayStep * 0.0021f;
                            }
                            // A soft border fades the shadows out toward the cookie's edge.
                            float edge = Math.Min(Math.Min(x, Size - 1 - x), Math.Min(y, Size - 1 - y)) / 24f;
                            float shade = 0.62f * (1f - (float)Math.Exp(-depth)) * WeatherMath.Clamp01(edge);
                            pixels[y * Size + x] = (byte)Math.Round((1f - shade) * 255f);
                        }
                    }
                    if (requestRevision == Volatile.Read(ref revision))
                    {
                        Volatile.Write(ref failureReason, null);
                        Interlocked.Exchange(ref ready, new ShadowResult(requestRevision, cx, cz, pixels));
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

        private static byte LerpByte(byte a, byte b, float t) => (byte)Mathf.RoundToInt(Mathf.Lerp(a, b, t));

        private struct LightState
        {
            private readonly Light light;
            private readonly UniversalAdditionalLightData data;
            private Texture cookie, writtenCookie;
            private Vector2 size, offset, writtenSize, writtenOffset;
            private Quaternion rotation, writtenRotation;
            private bool written, wroteRotation;
            internal LightState(Light light, UniversalAdditionalLightData data)
            {
                this = default;
                this.light = light; this.data = data; cookie = light.cookie;
                size = data.lightCookieSize; offset = data.lightCookieOffset;
                rotation = light.transform.rotation;
            }
            internal void BeforeWrite(bool rotate)
            {
                if (light == null || data == null) return;
                // Native sky updates and other mods may replace individual values while active.
                // Rebase those originals before taking ownership again.
                if (!written || light.cookie != writtenCookie) cookie = light.cookie;
                if (!written || !data.lightCookieSize.Equals(writtenSize)) size = data.lightCookieSize;
                if (!written || !data.lightCookieOffset.Equals(writtenOffset)) offset = data.lightCookieOffset;
                if (rotate && (!wroteRotation || !light.transform.rotation.Equals(writtenRotation)))
                    rotation = light.transform.rotation;
            }
            internal void Wrote(bool rotate)
            {
                if (light == null || data == null) return;
                writtenCookie = light.cookie; writtenSize = data.lightCookieSize;
                writtenOffset = data.lightCookieOffset; written = true;
                if (rotate) { writtenRotation = light.transform.rotation; wroteRotation = true; }
            }
            internal void Restore()
            {
                if (light == null || data == null) return;
                if (written && light.cookie == writtenCookie) light.cookie = cookie;
                if (written && data.lightCookieSize.Equals(writtenSize)) data.lightCookieSize = size;
                if (written && data.lightCookieOffset.Equals(writtenOffset)) data.lightCookieOffset = offset;
                if (wroteRotation && light.transform.rotation.Equals(writtenRotation))
                    light.transform.rotation = rotation;
                written = wroteRotation = false;
            }
        }

        private sealed class ShadowResult
        {
            internal readonly int Revision;
            internal readonly double CentreX, CentreZ;
            internal readonly byte[] Pixels;
            internal ShadowResult(int revision, double centreX, double centreZ, byte[] pixels)
            { Revision = revision; CentreX = centreX; CentreZ = centreZ; Pixels = pixels; }
        }
    }
}
