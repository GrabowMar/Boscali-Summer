using BepInEx.Logging;
using BoscaliSummer.Features.Weather.Configuration;
using BoscaliSummer.Features.Weather.Domain;
using BoscaliSummer.Framework.Lifecycle;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Features.Weather.Runtime
{
    /// <summary>
    /// Draws one towering cumulonimbus column with an anvil per live storm cell, from the
    /// vanilla cloud particles and the deck's own material, so the storms belong to the sky
    /// instead of sitting in front of it. Exactly <see cref="StormField.MaxCells"/> rigs are
    /// built and reused; nothing here mutates the shared material or writes world state.
    /// </summary>
    internal sealed class SupercellRenderer : MonoBehaviour, ISceneService
    {
        /// <summary>
        /// One reusable storm rig. The particle modules are structs that proxy the native
        /// system, so they are fetched once per GameObject and stored; the handles stay valid
        /// for the system's lifetime and re-fetching them per tick would be pure churn.
        /// </summary>
        private sealed class Rig
        {
            public GameObject Root;
            public ParticleSystem Tower;
            public ParticleSystem Anvil;
            public ParticleSystem.MainModule TowerMain;
            public ParticleSystem.EmissionModule TowerEmission;
            public ParticleSystem.ShapeModule TowerShape;
            public ParticleSystem.VelocityOverLifetimeModule TowerVelocity;
            public ParticleSystem.MainModule AnvilMain;
            public ParticleSystem.EmissionModule AnvilEmission;
            public ParticleSystem.ShapeModule AnvilShape;
            public ParticleSystem.VelocityOverLifetimeModule AnvilVelocity;
            public float LastAge;
            public bool Configured;
            public bool Alive;
        }

        private const float RefreshInterval = 0.2f;
        private const float MinIntensity = 0.05f;
        private const int TowerMaxParticles = 90;
        private const int AnvilMaxParticles = 96;
        private const float TowerRateMin = 1.5f;
        private const float TowerRateMax = 12f;
        private const float AnvilRateMin = 1f;
        private const float AnvilRateMax = 7f;
        private const float TowerCrossSeconds = 90f;
        private const float AnvilSpreadSeconds = 140f;
        /// <summary>Fallback bias when the setting is missing; see CreateSystem for the sign.</summary>
        private const float DefaultSortingFudge = -100f;

        private static readonly Color PaleTint = new Color(0.84f, 0.87f, 0.91f);
        private static readonly Color StormTint = new Color(0.38f, 0.42f, 0.50f);
        private static readonly Color AnvilTint = new Color(0.94f, 0.95f, 0.97f);

        private readonly Rig[] rigs = new Rig[StormField.MaxCells];
        private readonly bool[] slotPresent = new bool[StormField.MaxCells];

        private WeatherSettings settings;
        private WeatherManager manager;
        private ManualLogSource log;
        private float nextRefresh;
        private bool announced;
        private bool deckHijackApplied;

        public int ActiveSystems { get; private set; }

        public void Configure(WeatherSettings weatherSettings, WeatherManager weatherManager, ManualLogSource logger)
        {
            settings = weatherSettings;
            manager = weatherManager;
            log = logger;
        }

        public void ResetForScene()
        {
            for (int slot = 0; slot < rigs.Length; slot++)
            {
                Rig rig = rigs[slot];
                if (rig == null) continue;
                Deactivate(rig);
                if (rig.Root != null && rig.Root.transform.parent != null)
                    rig.Root.transform.SetParent(null, false);
                rig.Configured = false;
                rig.LastAge = 0f;
            }
            ActiveSystems = 0;
            nextRefresh = 0f;
            // The scene that arrives brings its own, visible cloud deck, so the intent has to be
            // applied again rather than remembered as done.
            WeatherCloudAccess.Invalidate();
            deckHijackApplied = false;
        }

        private void OnDestroy()
        {
            WeatherCloudAccess.Release();
            ResetForScene();
            for (int slot = 0; slot < rigs.Length; slot++)
            {
                Rig rig = rigs[slot];
                if (rig == null || rig.Root == null) continue;
                Destroy(rig.Root);
                rigs[slot] = null;
            }
        }

        /// <summary>
        /// Take the local vanilla cloud deck away when the player asked the cells to own the sky,
        /// and give it back the moment either the cells or the module is switched off. Hiding the
        /// deck with nothing to replace it would leave an empty sky, so both settings gate it.
        /// </summary>
        private void ApplyDeckHijack()
        {
            bool want = settings.ReplaceVanillaClouds.Value &&
                        settings.Supercells.Value &&
                        settings.Enabled.Value;
            if (want == deckHijackApplied) return;
            if (WeatherCloudAccess.SetLocalDeckHidden(want, log)) deckHijackApplied = want;
        }

        private void Update()
        {
            if (Application.isBatchMode || manager == null || settings == null) return;
            if (!manager.Enabled || !settings.Enabled.Value || !settings.Supercells.Value)
            {
                if (ActiveSystems > 0) DeactivateAll();
                ApplyDeckHijack();
                return;
            }
            ApplyDeckHijack();
            if (SceneSingleton<CameraStateManager>.i == null) return;
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + RefreshInterval;
            Refresh();
        }

        private void Refresh()
        {
            if (Datum.origin == null) return;
            if (!WeatherCloudAccess.TryResolve(log) || !EnsureRigs()) return;

            for (int slot = 0; slot < slotPresent.Length; slot++) slotPresent[slot] = false;

            StormCell[] cells = manager.CellBuffer;
            int count = manager.CellCount;
            if (cells != null)
            {
                if (count > cells.Length) count = cells.Length;
                for (int i = 0; i < count; i++)
                {
                    StormCell cell = cells[i];
                    if (cell.Slot < 0 || cell.Slot >= rigs.Length) continue;
                    Rig rig = rigs[cell.Slot];
                    if (rig == null) continue;
                    slotPresent[cell.Slot] = true;
                    if (cell.Intensity <= MinIntensity)
                    {
                        Deactivate(rig);
                        continue;
                    }
                    Activate(rig, cell);
                }
            }

            int active = 0;
            for (int slot = 0; slot < rigs.Length; slot++)
            {
                Rig rig = rigs[slot];
                if (rig == null) continue;
                if (!slotPresent[slot]) Deactivate(rig);
                if (rig.Alive) active++;
            }
            ActiveSystems = active;
        }

        private void Activate(Rig rig, StormCell cell)
        {
            // A slot re-rolls a cell every StormField.Period; its age restarting is how a rig
            // notices the new cell and rebuilds the shape for its radius and height.
            if (!rig.Configured || cell.Age < rig.LastAge)
            {
                ConfigureRig(rig, cell);
                rig.Configured = true;
            }
            rig.LastAge = cell.Age;

            rig.Root.transform.position = new Vector3(cell.X, Datum.LocalSeaY + cell.CloudBase, cell.Z);
            if (!rig.Root.activeSelf) rig.Root.SetActive(true);
            if (!rig.Tower.isPlaying) rig.Tower.Play(true);

            if (cell.IsSupercell)
            {
                if (!rig.Anvil.gameObject.activeSelf) rig.Anvil.gameObject.SetActive(true);
                if (!rig.Anvil.isPlaying) rig.Anvil.Play(true);
            }
            else if (rig.Anvil.gameObject.activeSelf)
            {
                rig.Anvil.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                rig.Anvil.gameObject.SetActive(false);
            }

            // Intensity is the only per-tick ramp here; the manager already smooths the cell.
            float intensity = Mathf.Clamp01((cell.Intensity - MinIntensity) / (1f - MinIntensity));
            float detail = Mathf.Clamp01(settings.SupercellDetail.Value);
            Color tint = Color.Lerp(PaleTint, StormTint, intensity);
            rig.TowerMain.startColor = new ParticleSystem.MinMaxGradient(
                Shade(tint, 0.84f), Shade(tint, 1.12f));
            rig.TowerMain.startLifetimeMultiplier = Mathf.Lerp(0.55f, 1f, intensity);
            rig.TowerEmission.rateOverTime =
                new ParticleSystem.MinMaxCurve(Mathf.Lerp(TowerRateMin, TowerRateMax, intensity) * detail);

            if (cell.IsSupercell)
            {
                Color anvil = Color.Lerp(tint, AnvilTint, 0.4f);
                rig.AnvilMain.startColor = new ParticleSystem.MinMaxGradient(
                    Shade(anvil, 0.92f), Shade(anvil, 1.08f));
                rig.AnvilEmission.rateOverTime =
                    new ParticleSystem.MinMaxCurve(Mathf.Lerp(AnvilRateMin, AnvilRateMax, intensity) * detail);
            }

            rig.Alive = true;
        }

        private static void ConfigureRig(Rig rig, StormCell cell)
        {
            float radius = Mathf.Max(cell.Radius, 1f);
            float top = Mathf.Max(cell.TopHeight, 1f);
            // The column crosses its own height in TowerCrossSeconds, so a particle's life is
            // the tower it is allowed to climb; intensity shortens that life per tick.
            float riseSpeed = Mathf.Max(top / TowerCrossSeconds, 4f);
            float lifetime = Mathf.Clamp(top / riseSpeed, 60f, 240f);

            rig.TowerMain.startLifetime = new ParticleSystem.MinMaxCurve(
                Mathf.Clamp(lifetime * 0.85f, 60f, 240f),
                Mathf.Clamp(lifetime * 1.05f, 60f, 240f));
            rig.TowerMain.startSize = new ParticleSystem.MinMaxCurve(radius * 0.55f, radius * 1.15f);
            // Unity has no cylinder shape; a zero-angle cone is one, and it keeps the hollow
            // core that makes the column read as a tower instead of a solid drum.
            rig.TowerShape.shapeType = ParticleSystemShapeType.Cone;
            rig.TowerShape.angle = 0f;
            rig.TowerShape.radius = radius * 0.85f;
            rig.TowerShape.radiusThickness = 0.35f;
            rig.TowerVelocity.x = new ParticleSystem.MinMaxCurve(cell.VelocityX);
            rig.TowerVelocity.y = new ParticleSystem.MinMaxCurve(riseSpeed * 0.85f, riseSpeed * 1.15f);
            rig.TowerVelocity.z = new ParticleSystem.MinMaxCurve(cell.VelocityZ);

            float anvilRadius = Mathf.Max(cell.AnvilRadius, 1f);
            float anvilLifetime = Mathf.Clamp(anvilRadius / AnvilSpreadSeconds, 35f, 100f);
            rig.AnvilMain.startLifetime = new ParticleSystem.MinMaxCurve(anvilLifetime * 0.7f, anvilLifetime * 1.15f);
            rig.AnvilMain.startSize = new ParticleSystem.MinMaxCurve(anvilRadius * 0.55f, anvilRadius * 0.95f);
            rig.AnvilShape.shapeType = ParticleSystemShapeType.Circle;
            rig.AnvilShape.radius = anvilRadius;
            rig.AnvilShape.radiusThickness = 0.5f;
            rig.AnvilVelocity.x = new ParticleSystem.MinMaxCurve(cell.VelocityX);
            rig.AnvilVelocity.z = new ParticleSystem.MinMaxCurve(cell.VelocityZ);
            rig.AnvilVelocity.radial =
                new ParticleSystem.MinMaxCurve(anvilRadius / Mathf.Max(anvilLifetime, 1f) * 0.6f);
            rig.Anvil.transform.localPosition = new Vector3(0f, top, 0f);
        }

        private bool EnsureRigs()
        {
            bool built = false;
            for (int slot = 0; slot < rigs.Length; slot++)
            {
                Rig rig = rigs[slot];
                if (rig != null && rig.Root != null)
                {
                    if (rig.Root.transform.parent != Datum.origin)
                        rig.Root.transform.SetParent(Datum.origin, true);
                    continue;
                }
                rigs[slot] = BuildRig(slot);
                built = true;
            }

            if (built && !announced)
            {
                announced = true;
                log?.LogInfo("Weather supercells: " + rigs.Length + " rigs sharing the vanilla cloud material '" +
                    WeatherCloudAccess.PuffMaterial.name + "' (" + WeatherCloudAccess.Template.name + ").");
            }
            return true;
        }

        private Rig BuildRig(int slot)
        {
            Material material = WeatherCloudAccess.PuffMaterial;
            var root = new GameObject("BoscaliSummer.Supercell" + slot);
            root.transform.SetParent(Datum.origin, false);

            float fudge = settings != null ? settings.CloudSortFudge.Value : DefaultSortingFudge;
            ParticleSystem tower = CreateSystem(root.transform, "Tower", material, TowerMaxParticles, fudge);
            ParticleSystem anvil = CreateSystem(root.transform, "Anvil", material, AnvilMaxParticles, fudge);

            var rig = new Rig
            {
                Root = root,
                Tower = tower,
                Anvil = anvil,
                TowerMain = tower.main,
                TowerEmission = tower.emission,
                TowerShape = tower.shape,
                TowerVelocity = tower.velocityOverLifetime,
                AnvilMain = anvil.main,
                AnvilEmission = anvil.emission,
                AnvilShape = anvil.shape,
                AnvilVelocity = anvil.velocityOverLifetime
            };
            root.SetActive(false);
            return rig;
        }

        private static ParticleSystem CreateSystem(Transform parent, string name, Material material, int maxParticles, float fudge)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            ParticleSystem system = gameObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 5f;
            main.simulationSpace = ParticleSystemSimulationSpace.Custom;
            main.customSimulationSpace = Datum.origin;
            main.useUnscaledTime = false;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.stopAction = ParticleSystemStopAction.None;
            main.startSpeed = 0f;
            main.maxParticles = maxParticles;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;

            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;

            ParticleSystemRenderer renderer = gameObject.GetComponent<ParticleSystemRenderer>();
            // The deck's own material, borrowed by reference so the game keeps its lighting in
            // sync and a storm only reads it. A negative fudge draws the cell in front of the
            // vanilla deck: Unity documents that lower values win, and the naive positive default
            // buried the towers behind the very cloud they are supposed to tower over. The value
            // is a setting because the right bias can only be found by looking at the sky.
            renderer.material = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingFudge = fudge;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return system;
        }

        private static void Deactivate(Rig rig)
        {
            if (rig == null || rig.Root == null || !rig.Alive) return;
            rig.Tower.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            rig.Anvil.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            rig.Root.SetActive(false);
            rig.Alive = false;
        }

        private void DeactivateAll()
        {
            for (int slot = 0; slot < rigs.Length; slot++) Deactivate(rigs[slot]);
            ActiveSystems = 0;
        }

        private static Color Shade(Color color, float scale)
        {
            return new Color(
                Mathf.Clamp01(color.r * scale),
                Mathf.Clamp01(color.g * scale),
                Mathf.Clamp01(color.b * scale),
                color.a);
        }
    }
}
