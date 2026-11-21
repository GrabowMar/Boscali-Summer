using System.Collections.Generic;
using BoscaliSummer.Modules.Weather.Domain;
using BoscaliSummer.Modules.Weather.Audio;
using BoscaliSummer.Modules.Weather.Visuals;
using BoscaliSummer.Core.Contracts;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Weather.Runtime
{
    /// <summary>Four bounded spatial strikes, derived without observer-rain or new wire state. The two brightest light the clouds.</summary>
    internal sealed class LightningDirector : IClientEffect
    {
        private struct ActiveStrike { internal LightningEvent Event; internal bool Valid; }
        private const int Slots = 4, Branches = 4, Lines = 1 + Branches, Points = 33, BranchPoints = 17;
        private readonly ActiveStrike[] strikes = new ActiveStrike[Slots];
        private readonly LineRenderer[] bolts = new LineRenderer[Slots * Lines];
        private readonly Vector3[] channel = new Vector3[Points], fork = new Vector3[BranchPoints];
        private readonly ThunderSoundscape thunder = new ThunderSoundscape();
        private readonly WeatherField slotField = new WeatherField();
        private readonly StormCell[] slotCells = new StormCell[StormLattice.EnumerateCap];
        private int slotCellCount;
        private int cachedSlot = -1;
        private WeatherKey builtKey;   // what slotField was built from
        private float builtHalfX, builtHalfZ;
        private WeatherKey key;
        private float previousTime = -1f;
        private GameObject root;
        private Material boltMaterial;
        private Texture2D boltTexture;
        public string EffectId => "lightning";
        /// <summary>Night-sky sprites and jets fired by strikes (WeatherManager sets it).</summary>
        internal SkyPhenomena Phenomena;
        internal float FlashNow { get; private set; }
        internal Vector4 FlashA { get; private set; }
        internal Vector4 FlashB { get; private set; }
        internal int Strikes { get; private set; }
        internal int QueuedThunder => thunder.Queued;
        internal int ThunderVoices => thunder.Playing;
        public void ReleaseFx() => Reset();
        public void DescribeFx(IDictionary<string, object> state)
        {
            state["fx.lightning.strikes"] = Strikes;
            state["fx.lightning.active"] = (FlashA.w > 0f ? 1 : 0) + (FlashB.w > 0f ? 1 : 0);
            state["fx.lightning.queuedThunder"] = thunder.Queued;
            state["fx.lightning.voices"] = thunder.Playing;
        }

        internal void Tick(WeatherField field, float missionTime, GlobalPosition observer,
            Camera camera, bool reducedFlashes, bool audioEnabled, bool cockpit)
        {
            if (Application.isBatchMode || field == null || !field.IsBuilt || camera == null) { Reset(); return; }
            Vector3 observerGlobal = new Vector3(observer.x, observer.y, observer.z);
            bool discontinuity = key == null || !field.Key.SameTape(key) || previousTime < 0f ||
                missionTime < previousTime || missionTime - previousTime > 2.5f;
            if (discontinuity)
            {
                ClearEvents(); key = field.Key; previousTime = missionTime;
            }
            else if (missionTime > previousTime)
            {
                int firstSlot = Mathf.Max(0, Mathf.FloorToInt(previousTime / StormLightning.SlotSeconds));
                int lastSlot = Mathf.FloorToInt(missionTime / StormLightning.SlotSeconds);
                for (int slot = firstSlot; slot <= lastSlot; slot++)
                {
                    if (cachedSlot != slot)
                    {
                        // Peers with different render/update phases use precisely the same
                        // atmospheric strength and sites at the slot boundary.
                        // The slot field is a pure function of the key and the extent (the tape does not move, and the time it is built
                        // for is never read), so it is built once per key; a boundary, every two seconds, used to rebuild it all.
                        if (!ReferenceEquals(builtKey, field.Key) || builtHalfX != field.HalfX || builtHalfZ != field.HalfZ)
                        {
                            slotField.Build(field.Key, slot * StormLightning.SlotSeconds, field.HalfX, field.HalfZ, field.HourOfDay);
                            builtKey = field.Key; builtHalfX = field.HalfX; builtHalfZ = field.HalfZ;
                        }
                        // The slot's sources are a pure function of the key and the slot: the active storm cells within 180 km of the
                        // observer's tape position, nearest first, at most 24 (spec 6.1). A cell's event does not depend on the observer.
                        slotField.Frame.ToTape(observerGlobal.x, observerGlobal.z, out float qx, out float qz);
                        slotCellCount = StormLattice.Enumerate(slotField.Lattice, qx, qz, StormLattice.EnumerateRange, slotCells);
                        cachedSlot = slot;
                    }
                    for (int source = 0; source < slotCellCount + slotField.SuperstructureCount; source++)
                    {
                        LightningEvent strike;
                        bool has = source < slotCellCount
                            ? StormLightning.TryCell(slotCells[source], slotField.Frame, slot, out strike)
                            : StormLightning.TryHero(slotField.SuperstructureAt(source - slotCellCount), slotField.Key.LayoutSeed, slot, out strike);
                        if (!has || strike.Time <= previousTime || strike.Time > missionTime) continue;
                        float distance = Vector3.Distance(observerGlobal, new Vector3(strike.X, strike.Y, strike.Z));
                        if (distance > 180000f) continue;
                        int free = -1;
                        for (int i = 0; i < Slots && free < 0; i++)
                            if (!strikes[i].Valid || missionTime - strikes[i].Event.Time > 0.8f) free = i;
                        if (free < 0) continue;
                        strikes[free] = new ActiveStrike { Event = strike, Valid = true }; Strikes++;
                        if (!reducedFlashes && strike.BottomY < strike.Base && distance < 20000f) // a ground strike close enough to see its foot
                            BoscaliSummer.Core.Fx.ParticleFx.Spawn("wx-lightning-strike", new GlobalPosition(strike.X, strike.BottomY, strike.Z).ToLocalPosition(), Quaternion.identity);
                        if (!reducedFlashes) Phenomena?.Strike(observerGlobal, strike);
                        if (audioEnabled && distance <= ThunderSoundscape.MaxDistance)
                            thunder.Enqueue(strike.Time + LightningMath.ThunderDelay(distance), distance, strike.Seed,
                                Vector3.Dot((new Vector3(strike.X, strike.Y, strike.Z) - observerGlobal).normalized, camera.transform.right));
                    }
                }
                previousTime = missionTime;
            }
            // The two brightest flashes light the clouds (the shader has two slots); every live bolt is drawn.
            FlashA = FlashB = Vector4.zero;
            float pixelAngle = 2f * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, camera.pixelHeight);
            for (int i = 0; i < strikes.Length; i++)
            {
                ActiveStrike active = strikes[i]; float age = active.Valid ? missionTime - active.Event.Time : 2f;
                if (age > 0.8f || age < 0f) { strikes[i].Valid = false; HideBolt(i); continue; }
                float gain = reducedFlashes ? 0f : LightningMath.FlashEnvelope(age);
                // A ground strike lights the cloud base where its channel leaves the cloud, the part seen from under the storm; centred
                // mid-cloud (4-7 km up) the base under the bolt kept under half the flash (2026-10-19). An in-cloud flash lights it from inside.
                bool ground = active.Event.BottomY < active.Event.Base;
                var flash = new Vector4(active.Event.X, ground ? active.Event.Base : active.Event.Y, active.Event.Z, gain);
                if (gain > FlashA.w) { FlashB = FlashA; FlashA = flash; }
                else if (gain > FlashB.w) FlashB = flash;
                // A channel shows only under the cloud: ground strikes, seen from below the base (in-cloud strikes only light the cloud).
                if (gain > 0.015f && !reducedFlashes && active.Event.BottomY < active.Event.Base && observerGlobal.y < active.Event.Base)
                    DrawBolt(i, active.Event, gain, Vector3.Distance(observerGlobal, new Vector3(active.Event.X, active.Event.Y, active.Event.Z)) * pixelAngle);
                else HideBolt(i);
            }
            FlashNow = Mathf.Max(FlashA.w, FlashB.w); thunder.Tick(missionTime, audioEnabled, cockpit);
        }

        private void HideBolt(int index)
        {
            for (int k = 0; k < Lines; k++)
                if (bolts[index * Lines + k] != null) bolts[index * Lines + k].enabled = false;
        }

        /// <param name="metresPerPixel">Keep the hot core resolved at distance; the broader ribbon carries only its faint corona.</param>
        internal void DrawBolt(int index, LightningEvent strike, float gain, float metresPerPixel)
        {
            if (root == null)
            {
                Shader shader = CanopyShaderBundle.GetSkyGlowShader() ?? Shader.Find("Sprites/Default");
                if (shader == null || !shader.isSupported) return;
                root = new GameObject("BoscaliStormBolts");
                // After the cloud composite (2997), as before: only a sub-base channel seen from below is exposed here.
                // One textured ribbon supplies both the white core and blue corona, even without camera bloom.
                boltMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, renderQueue = 2998 };
                boltTexture = ChannelTexture();
                boltMaterial.SetTexture("_MainTex", boltTexture);
                if (boltMaterial.HasProperty("_Intensity")) boltMaterial.SetFloat("_Intensity", 4f);
                if (boltMaterial.HasProperty("_Color")) boltMaterial.SetColor("_Color", new Color(4f, 4f, 4f, 1f));
                for (int i = 0; i < bolts.Length; i++)
                {
                    var obj = new GameObject("StormBolt" + i); obj.transform.SetParent(root.transform, false);
                    LineRenderer line = obj.AddComponent<LineRenderer>();
                    line.sharedMaterial = boltMaterial; line.useWorldSpace = true;
                    line.positionCount = i % Lines == 0 ? Points : BranchPoints;
                    line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
                    line.textureMode = LineTextureMode.Stretch; line.alignment = LineAlignment.View;
                    line.numCornerVertices = 1; line.numCapVertices = 2; line.enabled = false; bolts[i] = line;
                }
            }
            float width = Mathf.Max(5f, metresPerPixel * 1.4f) * 6f;
            var colour = new Color(1f, 1f, 1f, Mathf.Clamp01(gain));
            LineRenderer bolt = bolts[index * Lines];
            bolt.startWidth = width; bolt.endWidth = width * 0.7f;
            Vector3 top = new GlobalPosition(strike.X, strike.Base, strike.Z).ToLocalPosition();
            Vector3 bottom = new GlobalPosition(strike.X, strike.BottomY, strike.Z).ToLocalPosition();
            float height = strike.Base - strike.BottomY;
            BuildChannel(channel, top, bottom, strike.Seed, Mathf.Min(250f, height * 0.09f));
            bolt.SetPositions(channel);
            bolt.startColor = bolt.endColor = colour; bolt.enabled = true;
            int count = 2 + Mathf.Min(2, (int)(WeatherMath.Hash01(strike.Seed, 29, 3) * 3f));
            // Forks retain the same paths on every return stroke, but their fine tips disappear before the main channel.
            for (int b = 0; b < Branches; b++)
            {
                LineRenderer branch = bolts[index * Lines + 1 + b];
                if (b >= count) { branch.enabled = false; continue; }
                int from = 4 + (int)(WeatherMath.Hash01(strike.Seed, 30 + b, 3) * 20f);
                Vector3 start = channel[from];
                float drop = Mathf.Min(start.y - bottom.y, height * WeatherMath.HashRange(strike.Seed, 31 + b, 3, 0, 0.12f, 0.38f));
                float heading = WeatherMath.HashRange(strike.Seed, 32 + b, 3, 0, 0f, 6.2832f);
                float reach = drop * WeatherMath.HashRange(strike.Seed, 33 + b, 3, 0, 0.4f, 1.2f);
                Vector3 end = start + new Vector3(Mathf.Cos(heading) * reach, -drop, Mathf.Sin(heading) * reach);
                BuildChannel(fork, start, end, strike.Seed ^ unchecked((uint)(b + 1) * 2246822519u), drop * 0.14f);
                branch.SetPositions(fork);
                branch.startWidth = width * WeatherMath.HashRange(strike.Seed, 35 + b, 3, 0, 0.35f, 0.6f);
                branch.endWidth = width * 0.04f;
                branch.startColor = new Color(1f, 1f, 1f, Mathf.Pow(colour.a, 1.5f) * 0.75f);
                branch.endColor = new Color(0.65f, 0.78f, 1f, 0f);
                branch.enabled = true;
            }
        }

        private static void BuildChannel(Vector3[] points, Vector3 start, Vector3 end, uint seed, float displacement)
        {
            points[0] = start; points[points.Length - 1] = end;
            // Seeded midpoint displacement: large bends carry smaller angular kinks instead of equal-size zigzags.
            // Rebuild from global endpoints each frame so floating-origin shifts cannot detach the branches.
            for (int span = points.Length - 1; span > 1; span /= 2)
            {
                for (int a = 0; a < points.Length - 1; a += span)
                {
                    int mid = a + span / 2;
                    points[mid] = (points[a] + points[a + span]) * 0.5f + new Vector3(
                        WeatherMath.HashRange(seed, mid, span, 19, -displacement, displacement), 0f,
                        WeatherMath.HashRange(seed, mid, span, 21, -displacement, displacement));
                }
                displacement *= 0.55f;
            }
        }

        private static Texture2D ChannelTexture()
        {
            const int height = 64;
            var texture = new Texture2D(2, height, TextureFormat.RGBA32, false)
            { name = "StormChannelCore", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color[height * 2];
            for (int y = 0; y < height; y++)
            {
                float cross = y / (height - 1f) * 2f - 1f;
                float core = Mathf.Exp(-cross * cross * 36f);
                Color colour = Color.Lerp(new Color(0.3f, 0.5f, 1f), Color.white, Mathf.Exp(-cross * cross * 18f));
                colour.a = Mathf.Clamp01(core + 0.2f * Mathf.Exp(-cross * cross * 7f)) * (1f - Mathf.SmoothStep(0.75f, 1f, Mathf.Abs(cross)));
                pixels[y * 2] = pixels[y * 2 + 1] = colour;
            }
            texture.SetPixels(pixels); texture.Apply(false, true);
            return texture;
        }

        private void ClearEvents()
        {
            for (int i = 0; i < strikes.Length; i++) { strikes[i] = default; HideBolt(i); }
            FlashA = FlashB = Vector4.zero; FlashNow = 0f; thunder.Clear();
            cachedSlot = -1;
            slotCellCount = 0;
        }
        /// <summary>Stop active strikes and thunder without releasing the owned bolt resources.</summary>
        internal void Suspend()
        {
            ClearEvents(); previousTime = -1f; key = null;
        }

        internal void Reset()
        {
            ClearEvents(); previousTime = -1f; key = null; thunder.Dispose();
            if (root != null) Object.Destroy(root); if (boltMaterial != null) Object.Destroy(boltMaterial);
            if (boltTexture != null) Object.Destroy(boltTexture);
            root = null; boltMaterial = null; boltTexture = null; for (int i = 0; i < bolts.Length; i++) bolts[i] = null;
        }
    }
}
