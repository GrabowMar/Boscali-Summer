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
        private const int Slots = 4, Branches = 2, Lines = 1 + Branches, Points = 13, BranchPoints = 7;
        private readonly ActiveStrike[] strikes = new ActiveStrike[Slots];
        private readonly LineRenderer[] bolts = new LineRenderer[Slots * Lines];
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
            Vector3 observerGlobal = new Vector3((float)observer.x, (float)observer.y, (float)observer.z);
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

        /// <param name="metresPerPixel">The bolt's size of one screen pixel: a 7 m channel is sub-pixel past ~15 km, so the width keeps ~2.5 px.</param>
        private void DrawBolt(int index, LightningEvent strike, float gain, float metresPerPixel)
        {
            if (root == null)
            {
                Shader shader = Shader.Find("Sprites/Default"); if (shader == null) return;
                root = new GameObject("BoscaliStormBolts");
                // HDR tint over the vertex colour (which clamps at 1), so bloom gives the channel its glow. Queue 2998, after the cloud
                // composite (2997): before it, the composite laid the storm behind the bolt over it and no bolt ever showed (live 2026-10-19).
                // Nothing hides the bolt now, which is why only the sub-base channel is drawn, for a viewer under the base.
                boltMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, renderQueue = 2998, color = new Color(2.2f, 2.4f, 3f, 1f) };
                for (int i = 0; i < bolts.Length; i++)
                {
                    var obj = new GameObject("StormBolt" + i); obj.transform.SetParent(root.transform, false);
                    LineRenderer line = obj.AddComponent<LineRenderer>();
                    line.sharedMaterial = boltMaterial; line.useWorldSpace = true;
                    line.positionCount = i % Lines == 0 ? Points : BranchPoints;
                    line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
                    line.numCapVertices = 2; line.enabled = false; bolts[i] = line;
                }
            }
            float width = Mathf.Max(7f, metresPerPixel * 2.5f);
            var colour = new Color(0.72f, 0.82f, 1f, Mathf.Clamp01(gain));
            LineRenderer bolt = bolts[index * Lines];
            bolt.startWidth = width; bolt.endWidth = width * 0.35f;
            for (int n = 0; n < Points; n++)
            {
                bolt.SetPosition(n, BoltPoint(strike, n));
            }
            bolt.startColor = bolt.endColor = colour; bolt.enabled = true;
            // Forks leave the channel part-way down and wander out and down; thinner and dimmer than the channel.
            for (int b = 0; b < Branches; b++)
            {
                LineRenderer branch = bolts[index * Lines + 1 + b];
                int from = 3 + (int)(WeatherMath.Hash01(strike.Seed, 30 + b, 3) * 6f);
                Vector3 start = BoltPoint(strike, from);
                float drop = (strike.Base - strike.BottomY) * WeatherMath.HashRange(strike.Seed, 31 + b, 3, 0, 0.15f, 0.35f);
                float heading = WeatherMath.HashRange(strike.Seed, 32 + b, 3, 0, 0f, 6.2832f);
                float reach = drop * WeatherMath.HashRange(strike.Seed, 33 + b, 3, 0, 0.5f, 1.1f);
                for (int n = 0; n < BranchPoints; n++)
                {
                    float h = n / (BranchPoints - 1f);
                    float wobble = WeatherMath.HashRange(strike.Seed, 34 + b, n, 0, -0.3f, 0.3f) * drop * Mathf.Sin(h * Mathf.PI);
                    branch.SetPosition(n, start + new Vector3((Mathf.Cos(heading) * reach + wobble) * h, -drop * h, (Mathf.Sin(heading) * reach - wobble) * h));
                }
                branch.startWidth = width * 0.55f; branch.endWidth = width * 0.15f;
                branch.startColor = branch.endColor = new Color(colour.r, colour.g, colour.b, colour.a * 0.7f);
                branch.enabled = true;
            }
        }

        private static Vector3 BoltPoint(LightningEvent strike, int n)
        {
            // Kinks scale with the channel: a fixed 170 m on a ~900 m sub-base channel drew a coiled squiggle (live 2026-10-19).
            float h = n / (Points - 1f), bend = Mathf.Sin(h * Mathf.PI), kink = Mathf.Min(170f, 0.07f * (strike.Base - strike.BottomY));
            float x = strike.X + WeatherMath.HashRange(strike.Seed, n, 19, 0, -kink, kink) * bend;
            float z = strike.Z + WeatherMath.HashRange(strike.Seed, n, 21, 0, -kink, kink) * bend;
            return new GlobalPosition(x, Mathf.Lerp(strike.Base, strike.BottomY, h), z).ToLocalPosition();
        }

        private void ClearEvents()
        {
            for (int i = 0; i < strikes.Length; i++) { strikes[i] = default; HideBolt(i); }
            FlashA = FlashB = Vector4.zero; FlashNow = 0f; thunder.Clear();
            cachedSlot = -1;
            slotCellCount = 0;
        }
        internal void Reset()
        {
            ClearEvents(); previousTime = -1f; key = null; thunder.Dispose();
            if (root != null) Object.Destroy(root); if (boltMaterial != null) Object.Destroy(boltMaterial);
            root = null; boltMaterial = null; for (int i = 0; i < bolts.Length; i++) bolts[i] = null;
        }
    }
}
