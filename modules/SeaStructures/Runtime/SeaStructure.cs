using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.SeaStructures.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.SeaStructures.Runtime
{
    /// <summary>
    /// On every live structure, on every peer. The structure is static. Each section is a segment with its own hit points
    /// (the game's, replicated): it sheds loose pieces as they drop (in the order <see cref="SeaMesh"/> fixes, so peers agree
    /// without messages), and at zero it blows off and is gone, with everything standing on it. When the last segment
    /// goes the host marks the unit destroyed and every peer blows up what is left. Only the host decides anything: it
    /// passes a destroyed segment's fate to what stood on it and reports the kill, both through the game's own calls.
    /// </summary>
    internal sealed class SeaStructure : MonoBehaviour
    {
        public static readonly List<SeaStructure> All = new List<SeaStructure>();
        private const int MaxChunks = 18;
        private const float ChunkRange = 5000f;

        public int Kind; // serialized: survives the template being instantiated

        private sealed class Look
        {
            public int Section, Seed, Fallen;
            public float HitPoints = 100f;
            public bool Gone;
            public UnitPart Part;
            public MeshFilter Filter;
            public Mesh Own;
        }

        private readonly List<Look> looks = new List<Look>();
        private SeaLayout layout;
        private bool seeded, exploded;
        private float next;
        private int chunks;

        public Ship Ship { get; private set; }
        public SeaLayout Layout => layout;
        public bool Exploded => exploded;

        /// <summary>Sections drawn with pieces missing, pieces missing in all, and segments gone.</summary>
        public void Wear(out int sections, out int pieces, out int gone)
        {
            sections = pieces = gone = 0;
            foreach (Look look in looks)
            {
                if (look.Fallen > 0) { sections++; pieces += look.Fallen; }
                if (look.Gone) gone++;
            }
        }

        public IEnumerable<KeyValuePair<string, UnitPart>> Parts()
        {
            foreach (Look look in looks)
                if (look.Part != null) yield return new KeyValuePair<string, UnitPart>(layout.Sections[look.Section].Name, look.Part);
        }

        private void Awake()
        {
            SeaArt.Bind();
            layout = SeaLayouts.Of((SeaKind)Kind);
            Ship = GetComponent<Ship>();
            foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>(true))
            {
                string n = filter.name;
                if (!n.StartsWith(SeaTemplates.MeshPrefix) || !int.TryParse(n.Substring(SeaTemplates.MeshPrefix.Length), out int section)) continue;
                UnitPart part = filter.transform.parent != null ? filter.transform.parent.GetComponent<UnitPart>() : null;
                if (part != null && section >= 0 && section < layout.Sections.Length)
                    looks.Add(new Look { Section = section, Part = part, Filter = filter });
            }
        }

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        private void OnDestroy()
        {
            foreach (Look look in looks)
                if (look.Own != null) Destroy(look.Own);
        }

        private void Update()
        {
            if (Ship == null || exploded) return;
            if (Ship.disabled) { Explode(); return; }
            if (Time.time < next) return;
            next = Time.time + 0.3f;
            if (!seeded)
            {
                // The spawn position is replicated, so every peer breaks this structure up the same way.
                seeded = true;
                int at = Mathf.RoundToInt(Ship.startPosition.x) * 31 + Mathf.RoundToInt(Ship.startPosition.z);
                foreach (Look look in looks) look.Seed = at + look.Section * 7919;
            }
            bool standing = false;
            foreach (Look look in looks)
            {
                if (look.Gone || look.Part == null || look.Filter == null) continue;
                bool root = look.Part.transform == transform; // the base stands until the rest is gone
                float hp = look.Part.hitPoints;
                if (hp <= 0f && !root) { Remove(look); continue; }
                standing |= !root;
                if (hp == look.HitPoints) continue;
                SeaSection section = layout.Sections[look.Section];
                int fallen = SeaMesh.Fallen(section, look.Seed, hp);
                if (fallen != look.Fallen)
                {
                    if (fallen > look.Fallen) Shed(look, section, hp);
                    Mesh mesh = fallen == 0 ? null : SeaArt.ToMesh(SeaMesh.Build(section, look.Seed, hp), layout.Key + ":" + look.Section + ":worn");
                    look.Filter.sharedMesh = mesh != null ? mesh : SeaArt.Whole(layout, look.Section);
                    if (look.Own != null) Destroy(look.Own);
                    look.Own = mesh;
                    look.Fallen = fallen;
                }
                look.HitPoints = hp;
            }
            if (!standing && GameAccess.IsServer() && !Ship.disabled)
            {
                Ship.Networkdisabled = true;
                Ship.ReportKilled();
            }
        }

        /// <summary>A segment at zero: it blows off (the game's own part explosion), its pieces fall, and it and everything on
        /// it stop being drawn or hit. The host passes the same fate to the parts that stood on it.</summary>
        private void Remove(Look look)
        {
            look.Gone = true;
            SeaSection section = layout.Sections[look.Section];
            if (section.Pad) SeaFacility.Disable(gameObject);
            look.Fallen = section.Pieces.Length;
            Shed(look, section, float.NegativeInfinity);
            look.Part.SpawnFragments();
            Hide(look.Part.transform);
            if (!GameAccess.IsServer()) return;
            foreach (UnitPart on in look.Part.GetComponentsInChildren<UnitPart>(true))
                if (on != look.Part && on.hitPoints > 0f) on.TakeDamage(0f, 0f, 1f, 0f, on.hitPoints + 1f, default);
        }

        private static void Hide(Transform under)
        {
            foreach (Renderer r in under.GetComponentsInChildren<Renderer>(true))
                if (r is MeshRenderer || r is SkinnedMeshRenderer) r.enabled = false;
            foreach (Collider c in under.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        }

        /// <summary>The unit is destroyed: every part still there goes up at once and nothing is left to draw or hit. The game
        /// removes the unit after its own delay.</summary>
        private void Explode()
        {
            exploded = true;
            chunks = 0;
            foreach (Look look in looks)
            {
                if (look.Gone || look.Part == null || look.Filter == null) continue;
                look.Gone = true;
                Shed(look, layout.Sections[look.Section], float.NegativeInfinity);
            }
            foreach (UnitPart part in GetComponentsInChildren<UnitPart>(true)) part.SpawnFragments();
            Hide(transform);
        }

        /// <summary>The pieces that just broke off tumble into the sea: plain falling boxes, near the camera only, a few at a time.
        /// Negative infinity sheds everything still standing, core included.</summary>
        private void Shed(Look look, SeaSection section, float hp)
        {
            Camera cam = Camera.main;
            if (cam == null || (cam.transform.position - look.Filter.transform.position).sqrMagnitude > ChunkRange * ChunkRange) return;
            Transform at = look.Filter.transform;
            bool all = float.IsNegativeInfinity(hp);
            for (int i = 0; i < section.Pieces.Length && chunks < MaxChunks; i++)
            {
                SeaPiece p = section.Pieces[i];
                if (!SeaMesh.Stands(p, look.Seed, i, look.HitPoints) || (!all && SeaMesh.Stands(p, look.Seed, i, hp))) continue;
                var chunk = new GameObject("SeaChunk");
                chunk.transform.SetParent(Datum.origin, false); // rides the floating origin like the game's own debris
                chunk.transform.SetPositionAndRotation(at.TransformPoint(p.Centre), at.rotation);
                Mesh mesh = SeaArt.ToMesh(SeaMesh.Chunk(p), "BoscaliSummer.Sea.Chunk");
                chunk.AddComponent<MeshFilter>().sharedMesh = mesh;
                chunk.AddComponent<MeshRenderer>().sharedMaterial = SeaArt.Material(layout);
                Rigidbody rb = chunk.AddComponent<Rigidbody>();
                rb.mass = 500f;
                Vector3 away = at.TransformPoint(p.Centre) - transform.position;
                away.y = 0f;
                rb.velocity = away.normalized * Random.Range(2f, 7f) * (all ? 2.5f : 1f) + Vector3.up * Random.Range(1f, 5f) * (all ? 3f : 1f);
                rb.angularVelocity = Random.insideUnitSphere * 1.5f;
                chunks++;
                Destroy(mesh, 8f);
                Destroy(chunk, 8f);
            }
            if (chunks > 0) { CancelInvoke(nameof(ChunksGone)); Invoke(nameof(ChunksGone), 8f); }
        }

        private void ChunksGone() => chunks = 0;
    }
}
