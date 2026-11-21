using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Core.Damage;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.SeaStructures.Domain;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.SeaStructures.Runtime
{
    /// <summary>
    /// Every live structure, on every peer. A deterministic plan splits the model into many native hit points:
    /// each point has its own <see cref="UnitPart"/> and pool, draws only its own pieces, and tears up locally as
    /// its health falls. A point break can take its children and mountings (a section spine); when every required
    /// point is gone the host disables the ship and every peer turns what is left into a burning wreck that lists,
    /// settles and sinks. Native per-part damage replication is the only network model.
    /// </summary>
    internal sealed class SeaStructure : MonoBehaviour
    {
        public static readonly List<SeaStructure> All = new List<SeaStructure>();
        private const float WreckSeconds = 240f, WreckDepth = 130f, WreckTilt = 7f;
        private const int MaxChunksPerStructure = 120;
        // Blast sizing: the bundled fireball is authored at 90 m flash / 42 m fireball at scale 1, so
        // these multipliers keep a blast proportionate to what actually broke. A detail point pops at
        // 0.5 (a 21 m fireball), a whole spine at up to 1.6 (67 m) — the vehicle-explosion scale
        // range the rest of the mod already uses. The finale rolls a handful of larger balls over
        // the biggest sections still standing instead of one ball per damage point in a single frame.
        private const float BreakBallFactor = 0.02f, BreakBallMin = 0.5f, BreakBallMax = 1.6f;
        private const float BallWindow = 0.6f;
        private const int BallsPerWindow = 4, FinalBalls = 6;
        private const float FinalBallFirst = 2.2f, FinalBallStep = 0.25f, FinalBallGap = 0.45f;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly Color Soot = new Color(0.24f, 0.22f, 0.20f);
        private static readonly FieldInfo TurretDisabled = AccessTools.Field(typeof(Turret), "disabled");

        public int Kind; // serialized: survives the template being instantiated

        private sealed class View
        {
            public SeaPointPlan Plan;
            public UnitPart Part;
            public MeshFilter Filter;
            public MeshRenderer Renderer;
            public Mesh Own;
            public Vector3 BaseLocal;
            public int Fallen;
            public float Last = 1f;
            public int Stage;
            public bool Gone;
            public int Seed;
            public GameObject Fire, Smoke;
        }

        private SeaLayout layout;
        private List<SeaPointPlan> plan;
        private View[] views;
        private MultiHitPoint health;
        private SeaFire fire;
        private MaterialPropertyBlock block;
        private bool exploded, seeded;
        private int chunks;
        private float windowAt = -1f;
        private int windowBalls;

        public Ship Ship { get; private set; }
        public SeaLayout Layout => layout;
        public bool Exploded => exploded;
        public int PointCount => views != null ? views.Length : 0;
        public int BrokenPoints => health != null ? health.BrokenCount : 0;
        public bool AllPointsBroken => health != null && health.AllBroken;
        public bool WreckAlive => wreck != null;
        public float WreckAge => wreckView != null ? wreckView.Age : wreck != null ? Time.time - wreckAt : 0f;
        public float WreckSink => wreckView != null ? wreckView.Sink : 0f;
        public GameObject Wreck => wreck;

        private GameObject wreck;
        private float wreckAt;
        private SeaWreck wreckView;

        /// <summary>Sections with missing pieces, pieces missing in all, and damage points gone.</summary>
        public void Wear(out int sections, out int pieces, out int gone)
        {
            sections = pieces = gone = 0;
            if (views == null) return;
            foreach (View view in views)
            {
                if (view == null) continue;
                if (view.Fallen > 0) { sections++; pieces += view.Fallen; }
                if (view.Gone) gone++;
            }
        }

        public IEnumerable<KeyValuePair<string, UnitPart>> Parts()
        {
            if (views == null) yield break;
            foreach (View view in views)
                if (view?.Part != null) yield return new KeyValuePair<string, UnitPart>(view.Plan.Name, view.Part);
        }

        public SeaPointPlan PointAt(int index) => plan != null && index >= 0 && index < plan.Count ? plan[index] : null;
        public bool PointGone(int index) => views != null && index >= 0 && index < views.Length && views[index] != null && views[index].Gone;
        public float PointFraction(int index) => health != null ? health.Fraction(index) : 1f;

        private void Awake()
        {
            SeaArt.Bind();
            var kind = (SeaKind)Kind;
            layout = SeaLayouts.Of(kind);
            plan = SeaDamagePlans.Of(kind);
            Ship = GetComponent<Ship>();

            var found = new View[plan.Count];
            foreach (SeaPointTag tag in GetComponentsInChildren<SeaPointTag>(true))
            {
                if (tag.Index < 0 || tag.Index >= found.Length) continue;
                UnitPart part = tag.GetComponent<UnitPart>();
                MeshFilter filter = tag.GetComponentInChildren<MeshFilter>(true);
                if (part == null || filter == null) continue;
                found[tag.Index] = new View
                {
                    Plan = plan[tag.Index],
                    Part = part,
                    Filter = filter,
                    Renderer = filter.GetComponent<MeshRenderer>(),
                    BaseLocal = filter.transform.localPosition
                };
            }
            int built = 0;
            foreach (View view in found) if (view != null) built++;
            if (built != plan.Count)
                Debug.LogWarning("[SeaStructures] " + layout.Key + " built " + built + " of " + plan.Count + " damage points");

            health = gameObject.AddComponent<MultiHitPoint>();
            for (int i = 0; i < found.Length; i++)
            {
                View view = found[i];
                if (view == null)
                    Debug.LogError("[SeaStructures] " + layout.Key + " is missing damage point " + i + " (" + plan[i].Name + ")");
                health.Add(plan[i].Name, view?.Part, plan[i].Capacity, plan[i].Parent, plan[i].Required);
            }
            views = found;
        }

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        private void OnDestroy()
        {
            if (views == null) return;
            foreach (View view in views)
                if (view?.Own != null) Destroy(view.Own);
        }

        private void Start()
        {
            if (Ship == null || health == null) return;
            // Awake has reset every native pool to 100; apply the plan's capacities before any hit lands.
            // A client that somehow already took damage keeps its replicated value.
            for (int i = 0; i < views.Length; i++)
            {
                View view = views[i];
                if (view?.Part == null) continue;
                if (Mathf.Approximately(view.Part.hitPoints, 100f)) view.Part.hitPoints = view.Plan.Capacity;
                view.Last = view.Plan.Capacity > 0f ? Mathf.Clamp01(view.Part.hitPoints / view.Plan.Capacity) : 1f;
            }
            health.PointChanged += Changed;
            health.PointBroken += Broke;
            health.AllBrokenReached += Finish;
            fire = SeaFire.Bind(gameObject);
        }

        private void Update()
        {
            if (Ship == null || exploded) return;
            if (Ship.disabled) { Explode(); return; }
            if (seeded) return;
            seeded = true;
            // The spawn position is replicated, so every peer breaks this structure up the same way.
            int at = Mathf.RoundToInt(Ship.startPosition.x) * 31 + Mathf.RoundToInt(Ship.startPosition.z);
            foreach (View view in views) if (view != null) view.Seed = at + view.Plan.Section * 7919 + view.Plan.Parent;
        }

        private void Changed(int index)
        {
            if (exploded) return;
            View view = Find(index);
            if (view == null || view.Gone) return;
            float fraction = health.Fraction(index);
            if (fraction >= view.Last - 0.0001f) return; // damage only; the wreck is latched
            if (view.Filter != null)
            {
                int fallen = SeaMesh.PointFallen(layout.Sections[view.Plan.Section], view.Plan.Pieces, fraction);
                if (fallen > view.Fallen)
                {
                    Shed(view, view.Last, fraction);
                    view.Fallen = fallen;
                }
                Rebuild(view, fraction);
                Look(view, fraction);
            }
            view.Last = fraction;
        }

        private void Broke(int index)
        {
            if (exploded) return;
            View view = Find(index);
            if (view == null || view.Gone) return;
            view.Gone = true;
            Shed(view, view.Last, 0f);
            if (view.Filter != null) view.Filter.sharedMesh = null;
            if (view.Own != null) { Destroy(view.Own); view.Own = null; }
            Vector3 at = view.Filter != null ? view.Filter.transform.TransformPoint(view.Plan.Centre) : view.Part.transform.position;
            if (fire != null && AllowBreakBall())
                fire.Ball(at, Mathf.Clamp(view.Plan.Size.magnitude * BreakBallFactor, BreakBallMin, BreakBallMax));
            Burst(view, at);
            Hide(view.Part.transform);
            Silence(view.Part.transform);
            if (view.Plan.Spine && layout.Sections[view.Plan.Section].Pad && Ship != null) SeaFacility.Disable(gameObject);

            if (!GameAccess.IsServer()) return;
            // Everything standing on a broken point goes with it: the host passes the same fate through the
            // game's own lethal damage so the parts replicate on every peer.
            foreach (UnitPart on in view.Part.GetComponentsInChildren<UnitPart>(true))
                if (on != view.Part && on.hitPoints > 0f) on.TakeDamage(0f, 0f, 1f, 0f, on.hitPoints + 1f, default);
        }

        private void Finish()
        {
            if (exploded) return;
            exploded = true;
            if (GameAccess.IsServer() && Ship != null && !Ship.disabled)
            {
                Ship.Networkdisabled = true;
                Ship.ReportKilled();
            }
            Explode();
        }

        private void Explode()
        {
            if (exploded && wreck != null) return;
            exploded = true;
            // Sections come down in place; the fireballs are a short rolling series over the largest
            // of them, biggest first. The plan is pure, so every peer picks the same spots.
            var standing = new List<View>();
            foreach (View view in views)
            {
                if (view == null || view.Gone) continue;
                Shed(view, view.Last, 0f);
                view.Gone = true;
                if (view.Own != null) { Destroy(view.Own); view.Own = null; }
                if (view.Filter != null) standing.Add(view);
            }
            if (fire != null && standing.Count > 0)
            {
                standing.Sort((a, b) => b.Plan.Size.magnitude.CompareTo(a.Plan.Size.magnitude));
                var at = new List<Vector3>(Mathf.Min(standing.Count, FinalBalls));
                for (int i = 0; i < standing.Count && i < FinalBalls; i++)
                    at.Add(standing[i].Filter.transform.TransformPoint(standing[i].Plan.Centre));
                StartCoroutine(RollBalls(at));
            }
            BuildWreck();
            Hide(transform);
            Silence(transform);
            if (Ship != null) SeaFacility.Disable(gameObject);
        }

        private IEnumerator RollBalls(List<Vector3> at)
        {
            for (int i = 0; i < at.Count; i++)
            {
                fire.Ball(at[i], Mathf.Max(FinalBallFirst - i * FinalBallStep, 0.95f));
                if (i + 1 < at.Count) yield return new WaitForSeconds(FinalBallGap);
            }
        }

        /// <summary>A single catastrophic hit that wipes many points in one frame must not turn into
        /// dozens of simultaneous fireballs: a short window paces the storm while patient point-by-point
        /// kills keep a full blast on every break. Debris, fires and the finale are unaffected.</summary>
        private bool AllowBreakBall()
        {
            if (windowAt < 0f || Time.time - windowAt > BallWindow)
            {
                windowAt = Time.time;
                windowBalls = 0;
            }
            if (windowBalls >= BallsPerWindow) return false;
            windowBalls++;
            return true;
        }

        private void BuildWreck()
        {
            if (wreck != null || layout == null) return;
            Mesh mesh = SeaArt.ToMesh(SeaMesh.Wreck(layout), layout.Key + ":wreck");
            var go = new GameObject(layout.Key + " wreck");
            // Ride the floating origin like the game's own debris; the world pose still matches the ship.
            go.transform.SetParent(Datum.origin != null ? Datum.origin : transform.parent, false);
            go.transform.SetPositionAndRotation(transform.position, transform.rotation);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();            renderer.sharedMaterial = SeaArt.Material(layout);
            block = block ?? new MaterialPropertyBlock();
            block.SetColor(BaseColor, Soot);
            renderer.SetPropertyBlock(block);
            wreckView = go.AddComponent<SeaWreck>();
            wreckView.Configure(this, fire, mesh, WreckSeconds, WreckDepth, WreckTilt);
            wreck = go;
            wreckAt = Time.time;
        }

        private View Find(int index)
        {
            return index >= 0 && index < views.Length ? views[index] : null;
        }

        private void Rebuild(View view, float fraction)
        {
            SeaSection section = layout.Sections[view.Plan.Section];
            SeaMesh.Data data = SeaMesh.Point(section, view.Plan.Pieces, fraction);
            if (view.Own != null) { Destroy(view.Own); view.Own = null; }
            if (data.Vertices.Count == 0) { view.Filter.sharedMesh = null; return; }
            Mesh mesh = SeaArt.ToMesh(data, layout.Key + ":p" + view.Plan.Name + ":worn");
            view.Filter.sharedMesh = mesh;
            view.Own = mesh;
        }

        private void Shed(View view, float from, float to)
        {
            if (view.Filter == null || chunks >= MaxChunksPerStructure) return;
            SeaSection section = layout.Sections[view.Plan.Section];
            var fallen = new List<int>();
            SeaMesh.Falls(section, view.Plan.Pieces, from, to, fallen);
            foreach (int p in fallen)
            {
                SeaPiece piece = section.Pieces[p];
                Vector3 at = view.Filter.transform.TransformPoint(piece.Centre);
                Quaternion rotation = view.Filter.transform.rotation;
                float mass = Mathf.Clamp(piece.Size.sqrMagnitude * 12f, 40f, 4000f);
                Vector3 away = at - transform.position;
                away.y = 0f;
                Vector3 velocity = (away.sqrMagnitude > 1f ? away.normalized : Vector3.forward) * Random.Range(2f, 6f) +
                                   Vector3.up * Random.Range(1.5f, 5f);
                SeaChunk.Spawn(at, rotation, SeaArt.ToMesh(SeaMesh.Chunk(piece), "BoscaliSummer.Sea.Chunk"),
                    SeaArt.Material(layout), velocity, mass);
                chunks++;
                if (chunks >= MaxChunksPerStructure) break;
            }
        }

        private void Look(View view, float fraction)
        {
            if (view.Renderer != null && view.Renderer.sharedMaterial != null)
            {
                block = block ?? new MaterialPropertyBlock();
                view.Renderer.GetPropertyBlock(block);
                block.SetColor(BaseColor, Color.Lerp(Color.white, Soot, (1f - fraction) * 0.8f));
                view.Renderer.SetPropertyBlock(block);
            }
            if (view.Filter != null)
            {
                // Sag only once the point is visibly failing; a fresh hit keeps its silhouette.
                float sag = Mathf.Clamp01((0.5f - fraction) * 2f) * 1.6f;
                float side = ((view.Seed >> 5) & 7) - 3.5f;
                Quaternion turn = Quaternion.Euler(sag * 0.35f * side, 0f, sag * 0.2f * side);
                Vector3 centre = view.Plan.Centre;
                view.Filter.transform.localRotation = turn;
                view.Filter.transform.localPosition = view.BaseLocal + centre - turn * centre;
            }
            if (fire == null || view.Part == null) return;
            if (view.Stage < 1 && fraction < 0.72f)
            {
                view.Stage = 1;
                view.Smoke = fire.Attach(view.Filter.transform, Vector3.zero, 2.2f, false, view.Part);
            }
            if (view.Stage < 2 && fraction < 0.45f)
            {
                view.Stage = 2;
                view.Fire = fire.Attach(view.Filter.transform, Vector3.zero, 2.4f, true, view.Part);
            }
            if (view.Stage < 3 && fraction < 0.2f)
            {
                view.Stage = 3;
                fire.Attach(view.Filter.transform, new Vector3(0f, 0.5f, 0f), 1.7f, true, view.Part);
            }
        }

        private void Burst(View view, Vector3 at)
        {
            if (view.Smoke != null) Destroy(view.Smoke);
            if (view.Fire != null) Destroy(view.Fire);
            if (fire != null) fire.Attach(view.Part.transform, view.Part.transform.InverseTransformPoint(at), 1.8f, true, view.Part);
        }

        private static void Hide(Transform under)
        {
            foreach (Renderer r in under.GetComponentsInChildren<Renderer>(true))
                if (r is MeshRenderer || r is SkinnedMeshRenderer) r.enabled = false;
            foreach (Collider c in under.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        }

        /// <summary>No gun on a broken section or a dead station may keep firing from empty air.</summary>
        private static void Silence(Transform under)
        {
            if (TurretDisabled != null)
                foreach (Turret turret in under.GetComponentsInChildren<Turret>(true))
                    TurretDisabled.SetValue(turret, true);
            foreach (Weapon weapon in under.GetComponentsInChildren<Weapon>(true))
                weapon.Safety = true;
        }
    }
}
