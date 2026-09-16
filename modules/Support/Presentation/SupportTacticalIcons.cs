using System;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// Procedurally synthesizes crisp, high-contrast NATO tactical vector icons, range rings,
    /// and telemetry badges for the theater map (DynamicMap).
    /// Generated once in static memory with zero runtime disk or bundle dependencies.
    /// All sprites are generated in pure white with anti-aliased alpha channels, allowing
    /// dynamic HUD tinting via Image.color.
    /// </summary>
    internal static class SupportTacticalIcons
    {
        public static Sprite RingSprite { get; private set; }
        public static Sprite DottedRingSprite { get; private set; }
        public static Sprite RadialFillSprite { get; private set; }
        public static Sprite CrosshairSprite { get; private set; }
        public static Sprite BadgeBgSprite { get; private set; }

        public static Sprite RodIcon { get; private set; }
        public static Sprite EmpIcon { get; private set; }
        public static Sprite SatIcon { get; private set; }
        public static Sprite FlrIcon { get; private set; }
        public static Sprite FtfIcon { get; private set; }

        /// <summary>Soft coverage dome: no hard rim, fades to nothing at the edge.</summary>
        public static Sprite CoverageDiscSprite { get; private set; }

        /// <summary>Fine dashed orbit ring, lighter than the strike marker ring.</summary>
        public static Sprite OrbitTrackSprite { get; private set; }

        /// <summary>Horizontal dashed segment, stretched and rotated for transfer paths.</summary>
        public static Sprite DashedLineSprite { get; private set; }

        /// <summary>
        /// A radar-sweep wedge: brightest at its leading edge, fading to nothing across the
        /// arc. Rotated in place over time for the orbital display's sweep — a pre-baked
        /// sprite mutated only by transform rotation, never redrawn per frame.
        /// </summary>
        public static Sprite SweepWedgeSprite { get; private set; }

        /// <summary>Small soft-glow dot for a data-pulse riding a transfer track or graph edge.</summary>
        public static Sprite DataPulseSprite { get; private set; }

        private static bool initialized;

        public static void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;

            RingSprite = CreateRingSprite(128, 3, true);
            DottedRingSprite = CreateDottedRingSprite(128, 3);
            RadialFillSprite = CreateRadialFillSprite(128);
            CrosshairSprite = CreateCrosshairSprite(64);
            BadgeBgSprite = CreateBadgeBgSprite(128, 48);
            CoverageDiscSprite = CreateCoverageDiscSprite(128);
            OrbitTrackSprite = CreateOrbitTrackSprite(128);
            DashedLineSprite = CreateDashedLineSprite(64);
            SweepWedgeSprite = CreateSweepWedgeSprite(128);
            DataPulseSprite = CreateDataPulseSprite(32);

            RodIcon = CreateRodIcon(64);
            EmpIcon = CreateEmpIcon(64);
            SatIcon = CreateSatIcon(64);
            FlrIcon = CreateFlrIcon(64);
            FtfIcon = CreateFtfIcon(64);
        }

        public static Sprite GetIcon(Runtime.SupportActionId action)
        {
            EnsureInitialized();
            switch (action)
            {
                case Runtime.SupportActionId.Artillery: return RodIcon;
                case Runtime.SupportActionId.Emp: return EmpIcon;
                case Runtime.SupportActionId.Recon:
                case Runtime.SupportActionId.ElintSweep:
                    return SatIcon;
                case Runtime.SupportActionId.FlareMissile: return FlrIcon;
                case Runtime.SupportActionId.Fortify: return FtfIcon;
                default: return CrosshairSprite;
            }
        }

        private static Sprite CreateRingSprite(int size, int thickness, bool cardinalTicks)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "SupportTacticalRing",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
            float radius = (size * 0.5f) - (cardinalTicks ? 9f : 3f);
            Color[] pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    float ringAlpha = Mathf.Clamp01(1f - Mathf.Abs(dist - radius) / (thickness * 0.65f));

                    // Cardinal tick marks at N, S, E, W
                    float tickAlpha = 0f;
                    if (cardinalTicks)
                    {
                        bool isXAxis = Mathf.Abs(x + 0.5f - center.x) <= 1.2f && Mathf.Abs(dist - radius) <= 7f;
                        bool isYAxis = Mathf.Abs(y + 0.5f - center.y) <= 1.2f && Mathf.Abs(dist - radius) <= 7f;
                        if (isXAxis || isYAxis) tickAlpha = 1f;
                    }

                    float finalAlpha = Mathf.Max(ringAlpha, tickAlpha);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, finalAlpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite CreateDottedRingSprite(int size, int thickness)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "SupportTacticalDottedRing",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
            float radius = (size * 0.5f) - 4f;
            Color[] pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 pos = new Vector2(x + 0.5f, y + 0.5f);
                    float dist = Vector2.Distance(pos, center);
                    float ringAlpha = Mathf.Clamp01(1f - Mathf.Abs(dist - radius) / (thickness * 0.65f));

                    float angle = Mathf.Atan2(pos.y - center.y, pos.x - center.x);
                    if (angle < 0f) angle += Mathf.PI * 2f;

                    // 16 dashes around the perimeter
                    float dashPattern = Mathf.Sin(angle * 16f);
                    float dashAlpha = dashPattern > 0f ? 1f : 0f;

                    pixels[y * size + x] = new Color(1f, 1f, 1f, ringAlpha * dashAlpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite CreateDashedLineSprite(int size)
        {
            const int height = 3;
            var tex = new Texture2D(size, height, TextureFormat.RGBA32, false)
            {
                name = "SupportDashedLine",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point
            };
            Color[] pixels = new Color[size * height];
            for (int x = 0; x < size; x++)
            {
                bool on = (x % 8) < 5;
                for (int y = 0; y < height; y++)
                    pixels[y * size + x] = on ? new Color(1f, 1f, 1f, 0.9f) : new Color(1f, 1f, 1f, 0f);
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, height), new Vector2(0f, 0.5f), 100f);
        }

        private static Sprite CreateCoverageDiscSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "SupportCoverageDisc",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
            float maxR = size * 0.5f - 1f;
            Color[] pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    float norm = Mathf.Clamp01(dist / maxR);
                    float alpha = dist > maxR ? 0f : Mathf.Pow(1f - norm, 1.7f) * 0.65f;
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite CreateOrbitTrackSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "SupportOrbitTrack",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
            float radius = size * 0.5f - 3f;
            Color[] pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 pos = new Vector2(x + 0.5f, y + 0.5f);
                    float dist = Vector2.Distance(pos, center);
                    float ring = Mathf.Clamp01(1f - Mathf.Abs(dist - radius) / 0.9f);
                    float angle = Mathf.Atan2(pos.y - center.y, pos.x - center.x);
                    if (angle < 0f) angle += Mathf.PI * 2f;
                    float dash = Mathf.Sin(angle * 72f) > -0.15f ? 1f : 0.12f;
                    pixels[y * size + x] = new Color(1f, 1f, 1f, ring * dash * 0.9f);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite CreateRadialFillSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "SupportTacticalRadialFill",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
            float maxR = size * 0.5f - 2f;
            Color[] pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    float norm = Mathf.Clamp01(dist / maxR);
                    // Shaded gradient: soft core, glowing rim, sharp outer edge
                    float alpha = (1f - norm * norm) * 0.25f + Mathf.Pow(norm, 5f) * 0.45f;
                    if (dist > maxR) alpha = 0f;

                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite CreateCrosshairSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "SupportTacticalCrosshair",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
            Color[] pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    float ring = Mathf.Clamp01(1f - Mathf.Abs(dist - 8f) / 1.2f);
                    float centerDot = dist <= 2.2f ? 1f : 0f;

                    // Crosshair tick spokes
                    bool hLine = Mathf.Abs(y + 0.5f - center.y) <= 0.8f && dist >= 12f && dist <= 24f;
                    bool vLine = Mathf.Abs(x + 0.5f - center.x) <= 0.8f && dist >= 12f && dist <= 24f;

                    float alpha = Mathf.Max(ring, centerDot, hLine ? 1f : 0f, vLine ? 1f : 0f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite CreateSweepWedgeSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "SupportSweepWedge",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
            float maxR = size * 0.5f - 1f;
            const float wedgeDeg = 22f;
            Color[] pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 pos = new Vector2(x + 0.5f, y + 0.5f);
                    float dist = Vector2.Distance(pos, center);
                    if (dist > maxR)
                    {
                        pixels[y * size + x] = Color.clear;
                        continue;
                    }

                    float angle = Mathf.Atan2(pos.y - center.y, pos.x - center.x) * Mathf.Rad2Deg;
                    if (angle < 0f) angle += 360f;

                    // The wedge spans [0, wedgeDeg): brightest at the leading edge (0°),
                    // fading to nothing at the trailing edge — a rotating sweep, not a pie.
                    float sweep = angle <= wedgeDeg ? 1f - angle / wedgeDeg : 0f;
                    float radial = Mathf.Pow(dist / maxR, 0.6f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, sweep * radial * 0.32f);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite CreateDataPulseSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "SupportDataPulse",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
            float coreR = size * 0.22f;
            float glowR = size * 0.5f - 1f;
            Color[] pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    float core = Mathf.Clamp01(1f - dist / coreR);
                    float glow = Mathf.Clamp01(1f - dist / glowR);
                    float alpha = Mathf.Max(core, glow * glow * 0.5f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite CreateBadgeBgSprite(int width, int height)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "SupportTacticalBadgeBg",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Color[] pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool isBorder = x == 0 || x == width - 1 || y == 0 || y == height - 1;
                    bool isAccentLeftRail = x >= 0 && x <= 3;

                    if (isAccentLeftRail)
                        pixels[y * width + x] = new Color(1f, 1f, 1f, 0.95f);
                    else if (isBorder)
                        pixels[y * width + x] = new Color(1f, 1f, 1f, 0.45f);
                    else
                        pixels[y * width + x] = new Color(0.04f, 0.07f, 0.11f, 0.82f);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
        }

        // ---- Ability Tactical Icons (64x64) ----------------------------------------------

        private static Sprite CreateRodIcon(int size)
        {
            // Kinetic Penetrator: High-velocity downward arrow/dart with swept fins and shock cone
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "SupportTacticalIconRod",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Color[] pixels = new Color[size * size];
            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x - center.x) / (size * 0.5f); // -1 to 1
                    float v = (y - center.y) / (size * 0.5f); // -1 (bottom) to 1 (top)

                    float alpha = 0f;

                    // Central penetrator dart body
                    if (Mathf.Abs(u) <= 0.12f && v >= -0.7f && v <= 0.65f) alpha = 1f;

                    // Downward sharp penetrator nose tip
                    if (v < -0.7f && v >= -0.92f && Mathf.Abs(u) <= (0.92f + v) * 0.55f) alpha = 1f;

                    // Swept fins at top (v: 0.2 to 0.65)
                    if (v >= 0.2f && v <= 0.65f)
                    {
                        float finWidth = Mathf.Lerp(0.12f, 0.65f, (v - 0.2f) / 0.45f);
                        if (Mathf.Abs(u) <= finWidth && Mathf.Abs(u) >= 0.12f) alpha = 0.9f;
                    }

                    // Hypersonic Mach shockwave lines
                    float machY = -0.5f + Mathf.Abs(u) * 1.4f;
                    if (Mathf.Abs(v - machY) <= 0.08f && Mathf.Abs(u) >= 0.25f && Mathf.Abs(u) <= 0.85f)
                        alpha = Mathf.Max(alpha, 0.85f);

                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite CreateEmpIcon(int size)
        {
            // EMP Shock: Jagged high-voltage lightning bolt with dual radiating pulse rings
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "SupportTacticalIconEmp",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Color[] pixels = new Color[size * size];
            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x - center.x) / (size * 0.5f);
                    float v = (y - center.y) / (size * 0.5f);
                    float dist = Mathf.Sqrt(u * u + v * v);

                    float alpha = 0f;

                    // Stepped lightning bolt geometry:
                    // Segment 1 (upper): from (0.25, 0.8) to (-0.08, 0.05)
                    // Horizontal step: from (-0.08, 0.05) to (0.12, 0.05)
                    // Segment 2 (lower): from (0.12, 0.05) to (-0.25, -0.8)
                    bool inUpper = v >= 0.0f && v <= 0.8f && Mathf.Abs(u - Mathf.Lerp(-0.08f, 0.25f, (v - 0.0f) / 0.8f)) <= 0.14f;
                    bool inMiddle = Mathf.Abs(v - 0.04f) <= 0.1f && u >= -0.22f && u <= 0.24f;
                    bool inLower = v >= -0.8f && v <= 0.08f && Mathf.Abs(u - Mathf.Lerp(-0.25f, 0.12f, (v - -0.8f) / 0.88f)) <= 0.14f;

                    if (inUpper || inMiddle || inLower) alpha = 1f;

                    // Concentric pulse brackets
                    float ring1 = Mathf.Clamp01(1f - Mathf.Abs(dist - 0.72f) / 0.08f);
                    float ring2 = Mathf.Clamp01(1f - Mathf.Abs(dist - 0.92f) / 0.08f);
                    if ((ring1 > 0f || ring2 > 0f) && (Mathf.Abs(u) > 0.4f))
                    {
                        alpha = Mathf.Max(alpha, Mathf.Max(ring1, ring2) * 0.8f);
                    }

                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite CreateSatIcon(int size)
        {
            // Satellite Recon: Orbital satellite body, dual solar wings, and sensor broadcast beam
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "SupportTacticalIconSat",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Color[] pixels = new Color[size * size];
            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x - center.x) / (size * 0.5f);
                    float v = (y - center.y) / (size * 0.5f);

                    float alpha = 0f;

                    // Central satellite bus box
                    if (Mathf.Abs(u) <= 0.2f && Mathf.Abs(v - 0.22f) <= 0.22f) alpha = 1f;

                    // Left solar array panel
                    if (u >= -0.85f && u <= -0.28f && Mathf.Abs(v - 0.22f) <= 0.16f) alpha = 0.95f;
                    // Right solar array panel
                    if (u >= 0.28f && u <= 0.85f && Mathf.Abs(v - 0.22f) <= 0.16f) alpha = 0.95f;

                    // Panel struts
                    if (Mathf.Abs(u) <= 0.28f && Mathf.Abs(v - 0.22f) <= 0.04f) alpha = 1f;

                    // Downward sensor radar sweep waves
                    float sweepDist = Vector2.Distance(new Vector2(u, v), new Vector2(0f, 0.15f));
                    float arc1 = Mathf.Clamp01(1f - Mathf.Abs(sweepDist - 0.45f) / 0.08f);
                    float arc2 = Mathf.Clamp01(1f - Mathf.Abs(sweepDist - 0.75f) / 0.08f);
                    if (v < 0.15f && Mathf.Abs(u) <= Mathf.Abs(v - 0.15f) * 1.3f)
                    {
                        alpha = Mathf.Max(alpha, Mathf.Max(arc1, arc2));
                    }

                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite CreateFlrIcon(int size)
        {
            // Flare Barrage: Multi-ray radiant pyrotechnic burst star with center ignition core
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "SupportTacticalIconFlr",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Color[] pixels = new Color[size * size];
            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x - center.x) / (size * 0.5f);
                    float v = (y - center.y) / (size * 0.5f);
                    float dist = Mathf.Sqrt(u * u + v * v);

                    float alpha = 0f;

                    // Center intense core
                    if (dist <= 0.22f) alpha = 1f;

                    // 8 radiant rays
                    float angle = Mathf.Atan2(v, u);
                    float rays = Mathf.Pow(Mathf.Abs(Mathf.Cos(angle * 4f)), 8f);
                    if (dist <= 0.88f)
                    {
                        alpha = Mathf.Max(alpha, rays * Mathf.Clamp01(1f - dist * 0.9f));
                    }

                    // Outer perimeter spark dots
                    if (Mathf.Abs(dist - 0.78f) <= 0.08f && Mathf.Sin(angle * 8f) > 0.75f)
                    {
                        alpha = 1f;
                    }

                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite CreateFtfIcon(int size)
        {
            // Zone Fortification: Fortified crenellated fortress / defensive shield chevron
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "SupportTacticalIconFtf",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Color[] pixels = new Color[size * size];
            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x - center.x) / (size * 0.5f);
                    float v = (y - center.y) / (size * 0.5f);

                    float alpha = 0f;

                    // Shield outline / shape
                    // Top: v <= 0.65
                    // Bottom point: v = -0.75, u = 0
                    if (v <= 0.65f && v >= -0.75f)
                    {
                        float maxU = v >= 0.0f
                            ? 0.72f
                            : Mathf.Lerp(0.72f, 0.0f, (0.0f - v) / 0.75f);

                        if (Mathf.Abs(u) <= maxU)
                        {
                            // Border & crenellation
                            bool isShieldBorder = Mathf.Abs(Mathf.Abs(u) - maxU) <= 0.14f || Mathf.Abs(v - -0.75f) <= 0.12f;
                            // 3 top battlements (crenellations)
                            bool isCrenellation = v >= 0.45f && (Mathf.Abs(u) <= 0.18f || Mathf.Abs(u - 0.5f) <= 0.18f || Mathf.Abs(u + 0.5f) <= 0.18f);

                            if (isShieldBorder || isCrenellation) alpha = 1f;
                            else alpha = 0.35f; // Translucent shield interior
                        }
                    }

                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
