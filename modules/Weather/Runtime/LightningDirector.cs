using System.Collections.Generic;
using BoscaliSummer.Modules.Weather.Domain;
using BoscaliSummer.Modules.Weather.Audio;
using BoscaliSummer.Core.Contracts;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Weather.Runtime
{
    /// <summary>Two bounded spatial strikes, derived without observer-rain or new wire state.</summary>
    internal sealed class LightningDirector : IClientEffect
    {
        private struct ActiveStrike { internal LightningEvent Event; internal bool Valid; }
        private readonly ActiveStrike[] strikes = new ActiveStrike[2];
        private readonly LineRenderer[] bolts = new LineRenderer[2];
        private readonly ThunderSoundscape thunder = new ThunderSoundscape();
        private readonly WeatherField slotField = new WeatherField();
        private int cachedSlot = -1;
        private WeatherKey key;
        private float previousTime = -1f;
        private GameObject root;
        private Material boltMaterial;
        public string EffectId => "lightning";
        public FxBudget Budget => new FxBudget(0, 2, 2, false);
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

        internal void Tick(float dt, WeatherField field, float missionTime, GlobalPosition observer,
            LevelInfo level, Camera camera, bool reducedFlashes, bool audioEnabled, bool cockpit)
        {
            if (Application.isBatchMode || field == null || !field.IsBuilt || camera == null) { Reset(); return; }
            Vector3 observerGlobal = new Vector3((float)observer.x, (float)observer.y, (float)observer.z);
            bool discontinuity = key == null || !key.Equals(field.Key) || previousTime < 0f ||
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
                        slotField.Build(field.Key, slot * StormLightning.SlotSeconds, field.HalfX, field.HalfZ, field.HourOfDay);
                        cachedSlot = slot;
                    }
                    for (int source = 0; source < slotField.CellCount + slotField.SuperstructureCount; source++)
                    {
                        if (!StormLightning.TryEvent(slotField, source, slot, out LightningEvent strike) ||
                            strike.Time <= previousTime || strike.Time > missionTime) continue;
                        float distance = Vector3.Distance(observerGlobal, new Vector3(strike.X, strike.Y, strike.Z));
                        if (distance > 180000f) continue;
                        int free = !strikes[0].Valid || missionTime - strikes[0].Event.Time > 0.8f ? 0 :
                            !strikes[1].Valid || missionTime - strikes[1].Event.Time > 0.8f ? 1 : -1;
                        if (free < 0) continue;
                        strikes[free] = new ActiveStrike { Event = strike, Valid = true }; Strikes++;
                        if (audioEnabled && distance <= 10000f)
                            thunder.Enqueue(strike.Time + LightningMath.ThunderDelay(distance), distance, strike.Seed,
                                Vector3.Dot((new Vector3(strike.X, strike.Y, strike.Z) - observerGlobal).normalized, camera.transform.right));
                    }
                }
                previousTime = missionTime;
            }
            FlashA = FlashB = Vector4.zero;
            for (int i = 0; i < strikes.Length; i++)
            {
                ActiveStrike active = strikes[i]; float age = active.Valid ? missionTime - active.Event.Time : 2f;
                if (age > 0.8f || age < 0f) { strikes[i].Valid = false; if (bolts[i] != null) bolts[i].enabled = false; continue; }
                float gain = reducedFlashes ? 0f : LightningMath.FlashEnvelope(age);
                var flash = new Vector4(active.Event.X, active.Event.Y, active.Event.Z, gain);
                if (i == 0) FlashA = flash; else FlashB = flash;
                if (gain > 0.015f && !reducedFlashes) DrawBolt(i, active.Event, gain);
                else if (bolts[i] != null) bolts[i].enabled = false;
            }
            FlashNow = Mathf.Max(FlashA.w, FlashB.w); thunder.Tick(missionTime, audioEnabled, cockpit);
        }

        private void DrawBolt(int index, LightningEvent strike, float gain)
        {
            if (root == null)
            {
                Shader shader = Shader.Find("Sprites/Default"); if (shader == null) return;
                root = new GameObject("BoscaliStormBolts");
                boltMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, renderQueue = 2996 };
                for (int i = 0; i < bolts.Length; i++)
                {
                    var obj = new GameObject("StormBolt" + i); obj.transform.SetParent(root.transform, false);
                    LineRenderer line = obj.AddComponent<LineRenderer>();
                    line.sharedMaterial = boltMaterial; line.useWorldSpace = true; line.positionCount = 13;
                    line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
                    line.startWidth = 7f; line.endWidth = 2f; line.enabled = false; bolts[i] = line;
                }
            }
            LineRenderer bolt = bolts[index];
            for (int n = 0; n < 13; n++)
            {
                float h = n / 12f, bend = Mathf.Sin(h * Mathf.PI);
                float x = strike.X + WeatherMath.HashRange(strike.Seed, n, 19, 0, -170f, 170f) * bend;
                float z = strike.Z + WeatherMath.HashRange(strike.Seed, n, 21, 0, -170f, 170f) * bend;
                bolt.SetPosition(n, new GlobalPosition(x, Mathf.Lerp(strike.Y, strike.BottomY, h), z).ToLocalPosition());
            }
            bolt.startColor = bolt.endColor = new Color(0.72f, 0.82f, 1f, gain * 0.75f); bolt.enabled = true;
        }

        private void ClearEvents()
        {
            for (int i = 0; i < strikes.Length; i++) { strikes[i] = default; if (bolts[i] != null) bolts[i].enabled = false; }
            FlashA = FlashB = Vector4.zero; FlashNow = 0f; thunder.Clear();
            cachedSlot = -1;
        }
        internal void Reset()
        {
            ClearEvents(); previousTime = -1f; key = null; thunder.Dispose();
            if (root != null) Object.Destroy(root); if (boltMaterial != null) Object.Destroy(boltMaterial);
            root = null; boltMaterial = null; for (int i = 0; i < bolts.Length; i++) bolts[i] = null;
        }
    }
}
