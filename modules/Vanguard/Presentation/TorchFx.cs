using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// What one SRB-2 TORCH bottle looks and sounds like, on every peer: the vanilla booster's plume, smoke trail, lights and
    /// roar (cloned with the casing in <see cref="TorchBoost"/>), a hot IR source while it burns, an ignition flash, a dust
    /// kick close to the ground, the casing falling away at burnout, and a camera thump and rumble for the pilot alone.
    /// </summary>
    internal sealed class TorchFx : MonoBehaviour
    {
        private static readonly System.Reflection.FieldInfo TrailLife = AccessTools.Field(typeof(TrailEmitter), "emitLifetime");
        private ParticleSystem[] smoke;
        private TrailEmitter[] trails;
        private Light[] lamps;
        private AudioSource[] sound;
        private float[] rates, glow, volume;
        private Renderer[] casings;
        private Transform art; // the baked bottle (StoreArt), or null: then the vanilla booster casing on this object is the bottle
        private IRSource heat;
        private Aircraft craft;
        private float flash, dustAt;

        /// <summary>Puts a freshly cloned booster at rest: nothing plays, burns or emits until the bottle is lit.</summary>
        public static void Quiet(GameObject bottle)
        {
            foreach (ParticleSystem system in bottle.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = system.main;
                main.playOnAwake = false;
            }
            foreach (Light lamp in bottle.GetComponentsInChildren<Light>(true)) lamp.enabled = false;
            foreach (AudioSource source in bottle.GetComponentsInChildren<AudioSource>(true)) source.playOnAwake = false;
            foreach (TrailEmitter trail in bottle.GetComponentsInChildren<TrailEmitter>(true)) trail.enabled = false;
        }

        private void Awake()
        {
            smoke = GetComponentsInChildren<ParticleSystem>(true);
            trails = GetComponentsInChildren<TrailEmitter>(true);
            lamps = GetComponentsInChildren<Light>(true);
            sound = GetComponentsInChildren<AudioSource>(true);
            art = transform.Find("StoreArt_" + StoreArt.Torch);
            casings = art != null ? art.GetComponentsInChildren<Renderer>(true) : GetComponents<MeshRenderer>();
            rates = new float[smoke.Length];
            for (int i = 0; i < smoke.Length; i++) rates[i] = smoke[i].emission.rateOverTimeMultiplier;
            glow = new float[lamps.Length];
            for (int i = 0; i < lamps.Length; i++) glow[i] = lamps[i].intensity;
            volume = new float[sound.Length];
            for (int i = 0; i < sound.Length; i++) volume[i] = sound[i].volume;
        }

        private void OnDestroy()
        {
            if (heat != null && craft != null) craft.RemoveIRSource(heat);
        }

        public void Ignite(Aircraft aircraft)
        {
            craft = aircraft;
            flash = 1f;
            foreach (ParticleSystem system in smoke) system.Play();
            foreach (Light lamp in lamps) lamp.enabled = true;
            foreach (AudioSource source in sound) source.Play();
            foreach (TrailEmitter trail in trails)
            {
                TrailLife?.SetValue(trail, TorchProfile.BurnSeconds + 5f); // the trail switches itself off after a fixed emit time
                trail.rb = aircraft.rb;
                trail.StartTrail();
            }
            heat = new IRSource(transform, TorchProfile.IrIntensity, false);
            aircraft.AddIRSource(heat);
            if (Mine) SceneSingleton<CameraStateManager>.i?.ShakeCamera(.5f, .9f);
            Dust();
        }

        /// <summary>Per frame while burning, with the thrust fraction.</summary>
        public void Tick(float throttle)
        {
            flash = Mathf.MoveTowards(flash, 0f, Time.deltaTime * 2.5f);
            for (int i = 0; i < lamps.Length; i++) lamps[i].intensity = glow[i] * (throttle + 3f * flash);
            for (int i = 0; i < smoke.Length; i++)
            {
                ParticleSystem.EmissionModule emission = smoke[i].emission;
                emission.rateOverTimeMultiplier = rates[i] * throttle;
            }
            for (int i = 0; i < sound.Length; i++) sound[i].volume = volume[i] * Mathf.Clamp01(throttle + .3f);
            if (Mine) SceneSingleton<CameraStateManager>.i?.ShakeCamera(.03f * throttle, .07f * throttle);
            Dust();
        }

        /// <summary>Burnout: the plume and lamps stop, the smoke already in the air drifts on, the heat source goes.</summary>
        public void Out()
        {
            if (smoke == null) return;
            foreach (ParticleSystem system in smoke) system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            foreach (TrailEmitter trail in trails) trail.StopTrail();
            foreach (Light lamp in lamps) lamp.enabled = false;
            foreach (AudioSource source in sound) source.Stop();
            if (heat != null && craft != null) craft.RemoveIRSource(heat);
            heat = null;
        }

        /// <summary>The spent casing leaves the pylon: hide the mounted one and let a local copy tumble away.</summary>
        public void Separate()
        {
            if (casings.Length == 0 || !casings[0].enabled || craft == null) return;
            GameObject drop;
            if (art != null)
            {
                drop = Instantiate(art.gameObject, art.position, art.rotation); // the whole baked bottle, LODs and all
                drop.name = "TorchCasing";
            }
            else
            {
                MeshFilter shape = GetComponent<MeshFilter>();
                if (shape == null) return;
                drop = new GameObject("TorchCasing");
                drop.transform.SetPositionAndRotation(transform.position, transform.rotation);
                drop.transform.localScale = transform.lossyScale;
                drop.AddComponent<MeshFilter>().sharedMesh = shape.sharedMesh;
                drop.AddComponent<MeshRenderer>().sharedMaterials = casings[0].sharedMaterials;
            }
            foreach (Renderer casing in casings) casing.enabled = false;
            Rigidbody body = drop.AddComponent<Rigidbody>();
            body.mass = TorchProfile.EmptyKg * .5f;
            body.drag = .3f;
            body.angularDrag = .05f;
            body.velocity = craft.rb.GetPointVelocity(transform.position) + (transform.position - craft.transform.position).normalized * 4f + Vector3.down * 2f;
            body.angularVelocity = Random.insideUnitSphere * 2f;
            Destroy(drop, TorchProfile.CasingLife);
        }

        /// <summary>Rearmed: a fresh bottle is back on the pylon.</summary>
        public void Restore()
        {
            if (casings != null) foreach (Renderer casing in casings) casing.enabled = true;
            flash = 0f;
        }

        private bool Mine => GameManager.GetLocalAircraft(out Aircraft me) && me == craft;

        private void Dust()
        {
            if (craft == null || craft.radarAlt > TorchProfile.DustAgl || Time.time < dustAt) return;
            dustAt = Time.time + .5f;
            Vector3 from = transform.position;
            Vector3 at = Physics.Raycast(from, Vector3.down, out RaycastHit hit, TorchProfile.DustAgl * 2f, PhysicsLayers.StaticsMask)
                ? hit.point : from + Vector3.down * craft.radarAlt;
            ParticleFx.Spawn("fx-collapse-dust", at, Quaternion.identity, 2.5f);
        }
    }

    /// <summary>
    /// The pilot's TORCH line on channel vg.torch (4 Hz): "BURN 5.2 S" over the bottle count and thrust, with a LIT and a
    /// SPENT notice. Only the aircraft the player flies.
    /// </summary>
    internal sealed class TorchHud : MonoBehaviour, ISceneService
    {
        private const string Owner = "vanguard.torch";
        private const float Interval = .25f;
        private IHudBoard board;
        private IHudLine line;
        private bool burning;
        private float next;

        public void ResetForScene()
        {
            burning = false;
            Drop();
        }

        private void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + Interval;
            if (board == null && !ModuleServices.TryGet(out board)) return;
            VanguardHudFeeds.DeclareChannel(TorchProfile.Channel, "TORCH");
            int lit = 0;
            float left = 0f, thrust = 0f;
            Aircraft me = null;
            bool now = GameManager.GetLocalAircraft(out me) && me != null && !me.disabled && TorchPod.Burn(me, out lit, out left, out thrust);
            if (!now)
            {
                if (burning) board.Notice(TorchProfile.Channel, HudTone.Info, left > 0f ? "TORCH HOLD" : "TORCH SPENT",
                    left > 0f ? TorchProfile.Words(left) + " LEFT" : "CASINGS AWAY");
                burning = false;
                Drop();
                return;
            }
            if (!burning) board.Notice(TorchProfile.Channel, HudTone.Info, "TORCH LIT", "HOLD FIRE TO BURN");
            burning = true;
            line ??= VanguardHudFeeds.Acquire(board, Owner, TorchProfile.Channel, "burn");
            line?.Set(HudTone.Caution, TorchProfile.Words(left), TorchProfile.Detail(thrust, lit), left / TorchProfile.BurnSeconds);
        }

        private void Drop()
        {
            line?.Release();
            line = null;
        }
    }
}
