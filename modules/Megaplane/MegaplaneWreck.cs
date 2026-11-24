using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Retained, local crash presentation built from the same section meshes as the aircraft.</summary>
    internal sealed class MegaplaneWreck : MonoBehaviour
    {
        private static readonly List<MegaplaneWreck> Fields = new List<MegaplaneWreck>();
        private static readonly string[] Names = MegaplaneCrash.GroupNames;
        internal const float RetentionSeconds = MegaplaneCrashRecord.Lifetime;
        internal MegaplaneCrashRecord Impact { get; private set; }
        internal float Age => Time.time - born;
        internal bool Settled => Age >= settleTime;
        internal IReadOnlyList<Transform> Pieces => frames;

        // Geometrical supports indexed by GroupNames. Flaps and small captive items
        // hang on their own piece of wing or body until enough energy tears them free.
        private static readonly int[] Parents = {
            -1,0,1,0,3,0, 0,6,7,8,9, 0,11,12,13,14, 6,11,16,17,16,
            6,7,8,9,11,12,13,14, 2,2,7,12,8,13,9,14,18,19,0,0,0,0,0,0,0
        };
        private static readonly float[] Split = {
            0,.54f,.69f,.48f,.39f,.64f, .54f,.71f,.36f,.64f,.39f, .54f,.71f,.36f,.64f,.39f,
            .44f,.44f,.57f,.57f,.53f, .35f,.42f,.31f,.28f,.35f,.42f,.31f,.28f,
            .76f,.76f,.82f,.82f,.75f,.75f,.83f,.83f,.99f,.99f,2,2,.91f,.91f,2,2,2
        };

        private sealed class Piece
        {
            internal int Group;
            internal Transform Frame;
            internal readonly List<Vector3> Skin = new List<Vector3>();
            internal readonly List<Renderer> Near = new List<Renderer>(), Far = new List<Renderer>();
            internal Vector3 Start, Touch, Middle, End, TrackStart, TrackEnd;
            internal Quaternion Initial, TouchRotation, Rest;
            internal float Flight, Slide, Hop, Width, Flood;
            internal bool DryTrack;
        }

        private readonly List<Piece> moving = new List<Piece>();
        private readonly List<Transform> frames = new List<Transform>();
        private float born, settleTime;
        private bool rested;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        internal static void Clear()
        {
            foreach (MegaplaneWreck field in Fields.ToArray()) if (field != null) field.Expire();
            Fields.Clear();
        }

        internal static bool HasField(Aircraft carrier) => Fields.Any(f => f != null &&
            f.Impact.Spawn == carrier.NetId && f.Impact.Unit == carrier.persistentID.Id);

        private void Expire()
        {
            gameObject.SetActive(false);
            Fields.Remove(this);
            Destroy(gameObject);
        }

        internal static void Show(MegaplaneCrashRecord impact)
        {
            if (impact.Age >= RetentionSeconds || Datum.origin == null || MegaplaneTemplate.Definition == null) return;
            var key = new PersistentID { Id = impact.Unit };
            if (key.TryGetUnit(out Unit unit) && unit is Aircraft carrier && carrier.NetId == impact.Spawn &&
                carrier.TryGetComponent(out MegaplaneCrash crash)) crash.Conceal();
            Fields.RemoveAll(w => w == null);
            if (Fields.Any(w => w.Impact.Spawn == impact.Spawn && w.Impact.Unit == impact.Unit)) return;
            if (Fields.Count >= MegaplaneCrashRecord.MaxFields) Fields.OrderBy(w => w.born).First().Expire();
            var go = new GameObject("KY44_Wreck_" + impact.Spawn);
            go.transform.SetParent(Datum.origin, false);
            go.transform.localPosition = impact.Contact;
            var wreck = go.AddComponent<MegaplaneWreck>();
            wreck.Impact = impact;
            wreck.born = Time.time - Mathf.Max(impact.Age, 0f);
            Fields.Add(wreck);
            wreck.Build();
            wreck.Pose(wreck.Age);
            MegaplaneWreckSmoke.Add(wreck);
            if (impact.Age < .30f && !GameManager.IsHeadless)
            {
                Vector3 at = go.transform.position;
                if (!impact.Water && SceneSingleton<EffectManager>.i != null)
                {
                    // The native pool is filled in Start, which may still be pending on a newly loaded peer.
                    try { SceneSingleton<EffectManager>.i.ImpactDust(48000f,at.ToGlobalPosition(),Quaternion.FromToRotation(Vector3.up,impact.Normal)); }
                    catch (ArgumentOutOfRangeException) { }
                }
                else if (impact.Water && SceneSingleton<ParticleEffectManager>.i != null && GameAssets.i.splash_large != null)
                    SceneSingleton<ParticleEffectManager>.i.GetPrefabEffect(GameAssets.i.splash_large).Play(at, Quaternion.identity);
            }
        }

        private void Build()
        {
            GameObject prefab = MegaplaneTemplate.Definition.unitPrefab;
            AeroPart[] owners = MegaplaneCrash.GroupOwners(prefab);
            var index = new Dictionary<AeroPart, int>();
            for (int i = 0; i < owners.Length; i++) if (owners[i] != null) index[owners[i]] = i;
            var clusters = new int[Names.Length];
            var pieces = new Dictionary<int, Piece>();
            float closing = Mathf.Max(0f, -Vector3.Dot(Impact.Velocity, Impact.Normal));
            Vector3 tangent = Vector3.ProjectOnPlane(Impact.Velocity, Impact.Normal);
            float severity = Mathf.Clamp01((closing - 5f) / 66f + Mathf.Max(tangent.magnitude - 25f, 0f) / 265f);
            Vector3 contactInAirframe = Quaternion.Inverse(Impact.Rotation) * (Impact.Contact - Impact.Origin);
            var crush = MegaplaneWreckCrush.Create(transform,contactInAirframe,Quaternion.Inverse(Impact.Rotation)*Impact.Normal,
                severity,Impact.Water);
            for (int i = 0; i < clusters.Length; i++)
            {
                clusters[i] = -1;
                if (owners[i] == null || (Impact.Surviving & (1UL << i)) == 0) continue;
                Vector3 at = prefab.transform.InverseTransformPoint(owners[i].transform.position);
                int parent = Parents[i];
                if (i == 20 && clusters[16] < 0) parent = 17;
                float local = severity + .19f * Mathf.Clamp01(1f - Vector3.Distance(at, contactInAirframe) / 145f);
                clusters[i] = parent >= 0 && clusters[parent] >= 0 && local < Split[i] ? clusters[parent] : i;
            }
            var block = new MaterialPropertyBlock();
            foreach (MeshFilter source in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                bool far = source.name.StartsWith("GrozaLOD_", StringComparison.Ordinal);
                bool paint = source.name.StartsWith("Detail_Livery_", StringComparison.Ordinal);
                bool service = source.name.StartsWith("Detail_SparAccess_", StringComparison.Ordinal) ||
                    source.name.StartsWith("Detail_HullPlateCatches_", StringComparison.Ordinal) || source.name == "Detail_ForwardAerials";
                if ((!far && !paint && !service) || source.sharedMesh == null) continue;
                string artName = far ? source.transform.parent.name : source.name;
                // The alternate bay payload is present in the shared file, hidden on the actual template.
                if (!source.transform.parent.gameObject.activeSelf || !source.gameObject.activeSelf) continue;
                AeroPart owner = source.GetComponentInParent<AeroPart>(true);
                if (owner == null || !index.TryGetValue(owner, out int group) || clusters[group] < 0) continue;
                int cluster = clusters[group];
                if (!pieces.TryGetValue(cluster, out Piece piece))
                {
                    piece = new Piece { Group = cluster, Frame = new GameObject(Names[cluster]).transform };
                    piece.Frame.SetParent(transform, false);
                    pieces.Add(cluster, piece);
                    frames.Add(piece.Frame);
                    moving.Add(piece);
                }
                var leaf = new GameObject(artName);
                leaf.transform.SetParent(piece.Frame, false);
                Vector3 local = prefab.transform.InverseTransformPoint(source.transform.position);
                Quaternion rot = Quaternion.Inverse(prefab.transform.rotation) * source.transform.rotation;
                PoseGear(prefab.transform,source.transform,ref local,ref rot);
                leaf.transform.SetLocalPositionAndRotation(local, rot);
                leaf.transform.localScale = source.transform.lossyScale;
                leaf.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
                MeshRenderer painted = leaf.AddComponent<MeshRenderer>();
                Material[] materials = source.GetComponent<MeshRenderer>().sharedMaterials;
                painted.sharedMaterials = materials;
                if (!service) piece.Far.Add(painted);
                MeshRenderer near = painted;
                // Keep the actual ribbing, inlet/fan profiles, glazing and curved shell at
                // close range. The compact source LOD already has the correct distant outline.
                if (far && (artName == Names[group] || artName.StartsWith("Fan_Pod_", StringComparison.Ordinal) ||
                    artName.StartsWith("Int_Bulkhead_", StringComparison.Ordinal) || artName.StartsWith("Int_Spars_", StringComparison.Ordinal) ||
                    artName == "Glass_Canopy" || artName == "Detail_WindscreenFrames") &&
                    source.transform.parent.TryGetComponent(out MeshFilter detailed))
                {
                    var detail = new GameObject("Near_" + artName);
                    detail.transform.SetParent(leaf.transform, false);
                    detail.AddComponent<MeshFilter>().sharedMesh = detailed.sharedMesh;
                    near = detail.AddComponent<MeshRenderer>();
                    near.sharedMaterials = detailed.GetComponent<MeshRenderer>().sharedMaterials;
                }
                piece.Near.Add(near);
                float heat = Mathf.Clamp01((1f-Vector3.Distance(local, contactInAirframe)/140f) * severity);
                Scorch(painted,materials,heat,block);
                if (near != painted) Scorch(near,near.sharedMaterials,heat,block);
                if (artName == Names[group])
                {
                    int fore = -1, aft = -1;
                    if (group == 0) { fore = 1; aft = 3; }
                    if (group == 1) { fore = 2; aft = 0; }
                    if (group == 2) aft = 1;
                    if (group == 3) { fore = 0; aft = 4; }
                    if (group == 4) fore = 3;
                    if (group >= 6 && group <= 15)
                    {
                        fore = group == 10 || group == 15 ? -1 : group + 1;
                        aft = Parents[group];
                    }
                    if (group == 16 || group == 17) fore = Parents[group];
                    MegaplaneWreckArt.Add(leaf.transform, artName, fore >= 0 && clusters[fore] != cluster,
                        aft >= 0 && clusters[aft] != cluster);
                }
                if (crush != null) crush.Apply(piece.Frame,leaf.transform);
                if (artName == Names[group])
                {
                    foreach (Transform child in leaf.transform)
                    {
                        if (child == near.transform) continue;
                        piece.Near.AddRange(child.GetComponentsInChildren<Renderer>(true));
                        foreach (MeshFilter fracture in child.GetComponentsInChildren<MeshFilter>(true))
                            AddFractureSupports(piece,fracture);
                    }
                }
                Vector3[] vertices = leaf.GetComponent<MeshFilter>().sharedMesh.vertices;
                int stride = Mathf.Max(1, vertices.Length / 100);
                for (int v = 0; v < vertices.Length; v += stride)
                    piece.Skin.Add(local + rot * Vector3.Scale(vertices[v], leaf.transform.localScale));
            }
            if (crush != null) crush.Finish();
            foreach (Piece piece in moving) Plan(piece, severity, tangent, closing);
            foreach (Piece p in moving.Where(p => p.DryTrack && p.Width > 4f && (p.TrackEnd - p.TrackStart).sqrMagnitude > 36f)
                .OrderByDescending(p => p.Width * (p.TrackEnd-p.TrackStart).magnitude).Take(3))
                MegaplaneWreckMarks.Add(transform,p.TrackStart,p.TrackEnd,p.Width,false,p.Flight,p.Slide,Impact.Age);
        }

        private void PoseGear(Transform root, Transform source, ref Vector3 position, ref Quaternion rotation)
        {
            // All wreck meshes are copied into a flat piece. Compose the real gear hinge
            // rotations on their descendants before flattening, including partially cycled gear.
            for (Transform hinge = source; hinge != root && hinge != null; hinge = hinge.parent)
            {
                float angle;
                Vector3 axis;
                if (hinge.name.StartsWith("Gear_",StringComparison.Ordinal))
                {
                    angle = 90f*(1f-Mathf.SmoothStep(0f,1f,Impact.GearLeg/255f))*(hinge.name.EndsWith("_Aft") ? 1f : -1f);
                    axis = Vector3.right;
                }
                else if (hinge.name.StartsWith("GearDoor_",StringComparison.Ordinal) && (hinge.name.EndsWith("_L") || hinge.name.EndsWith("_R")))
                {
                    angle = 90f*Mathf.SmoothStep(0f,1f,Impact.GearDoor/255f)*(hinge.name.EndsWith("_L") ? -1f : 1f);
                    axis = Vector3.forward;
                }
                else continue;
                Quaternion rest = Quaternion.Inverse(root.rotation)*hinge.rotation;
                Quaternion delta = Quaternion.AngleAxis(angle,rest*axis);
                Vector3 pivot = root.InverseTransformPoint(hinge.position);
                position = pivot+delta*(position-pivot);
                rotation = delta*rotation;
            }
        }

        private static void AddFractureSupports(Piece piece, MeshFilter fracture)
        {
            Vector3[] points = fracture.sharedMesh.vertices;
            if (points.Length == 0) return;
            Matrix4x4 toPiece = piece.Frame.worldToLocalMatrix*fracture.transform.localToWorldMatrix;
            for (int i=0; i<points.Length; i++) points[i] = toPiece.MultiplyPoint3x4(points[i]);
            // A torn lip can turn underneath an inverted wing; retain tips in all 26 directions.
            for (int x=-1; x<=1; x++)
                for (int y=-1; y<=1; y++)
                    for (int z=-1; z<=1; z++)
                    {
                        if (x==0 && y==0 && z==0) continue;
                        Vector3 axis = new Vector3(x,y,z), tip = points[0];
                        float best = Vector3.Dot(tip,axis);
                        for (int i=1; i<points.Length; i++)
                        {
                            float projection = Vector3.Dot(points[i],axis);
                            if (projection > best) { tip = points[i]; best = projection; }
                        }
                        piece.Skin.Add(tip);
                    }
            for (int i=0,stride=Mathf.Max(1,points.Length/64); i<points.Length; i+=stride) piece.Skin.Add(points[i]);
        }

        private static void Scorch(Renderer renderer,Material[] materials,float heat,MaterialPropertyBlock block)
        {
            for (int slot = 0; slot < materials.Length; slot++)
            {
                Material mat = materials[slot];
                if (mat == null || !mat.HasProperty(BaseColor)) continue;
                Color tint = mat.GetColor(BaseColor);
                block.Clear();
                block.SetColor(BaseColor, new Color(tint.r*(.84f-.42f*heat), tint.g*(.82f-.46f*heat),
                    tint.b*(.78f-.45f*heat), tint.a));
                renderer.SetPropertyBlock(block, slot);
            }
        }

        private void Plan(Piece p, float severity, Vector3 tangent, float closing)
        {
            Bounds bounds = new Bounds(p.Skin[0], Vector3.zero);
            foreach (Vector3 point in p.Skin) bounds.Encapsulate(point);
            Vector3 pivot = bounds.center;
            foreach (Transform child in p.Frame) child.localPosition -= pivot;
            var levels = p.Frame.gameObject.AddComponent<LODGroup>();
            levels.SetLODs(new[] { new LOD(.20f,p.Near.ToArray()), new LOD(.0005f,p.Far.ToArray()) });
            levels.RecalculateBounds();
            levels.size = 90f;
            for (int i = 0; i < p.Skin.Count; i++) p.Skin[i] -= pivot;
            var rand = new System.Random(unchecked((int)(Impact.Spawn * 1103515245u) + p.Group * 4657));
            float R(float a,float b) => Mathf.Lerp(a,b,(float)rand.NextDouble());
            p.Initial = Impact.Rotation;
            p.Start = Impact.Origin - Impact.Contact + p.Initial * pivot;
            Vector3 axial = Vector3.ProjectOnPlane(tangent, Vector3.up).normalized;
            if (axial.sqrMagnitude < .01f) axial = Vector3.ProjectOnPlane(Impact.Rotation * Vector3.forward, Vector3.up).normalized;
            if (axial.sqrMagnitude < .01f) axial = Vector3.forward;
            Vector3 cross = Vector3.Cross(Vector3.up, axial);
            Vector3 outwards = Vector3.ProjectOnPlane(p.Start, Vector3.up).normalized;
            bool pod = p.Group >= 21 && p.Group <= 28;
            bool wing = p.Group >= 6 && p.Group <= 15;
            float slide = Mathf.Min(245f, (.24f * tangent.magnitude + .0095f * tangent.sqrMagnitude) *
                (.43f + .62f * severity) * (pod ? 1.30f : wing ? .79f : 1f) * R(.66f,1.28f));
            float spread = (4f + .59f * closing + .075f * tangent.magnitude) * (.25f+.75f*severity);
            Vector3 travel = axial*slide + outwards*spread*R(.45f,1.16f) + cross*spread*R(-.33f,.33f);
            // The initial rebound and the ground slide are separate trajectories. A steep
            // impact throws panels outwards; hard cylindrical pods run on down-track.
            Vector3 end = p.Start + travel;
            Vector3 touch = p.Start + travel * R(.17f,.41f);
            Vector3 middle = Vector3.Lerp(touch,end,.5f);
            float endBed = Ground(end,out Vector3 endGround,out Vector3 normal);
            float turn = R(-1,1)*(8f+severity*75f);
            float roll = pod ? R(-105f,105f) : wing ? R(-11f,11f) : R(-30f,30f);
            if (wing && severity > .55f && R(0,1) < .33f) roll += 178f;
            float pitch = R(-5f,5f)*(1f+severity);
            Vector3 heading = Vector3.ProjectOnPlane(Impact.Rotation*Vector3.forward,normal).normalized;
            if (heading.sqrMagnitude < .01f) heading = Vector3.ProjectOnPlane(axial,normal).normalized;
            p.Rest = Quaternion.AngleAxis(turn,normal) * Quaternion.LookRotation(heading,normal) * Quaternion.Euler(pitch,0,roll);
            p.TouchRotation = Quaternion.Slerp(p.Initial,p.Rest,.66f)*Quaternion.Euler(R(-8,8)*severity,0,R(-15,15)*severity);
            p.Flight = R(.65f,1.25f) + .018f*Mathf.Min(90f,closing);
            p.Slide = R(2f,3.8f) + Mathf.Min(3.2f,slide / 50f);
            p.Hop = Mathf.Min(32f, R(1.2f,2.8f)+closing*.34f*R(.43f,1.06f))*(.4f+.6f*severity);
            p.Width = Mathf.Clamp(Mathf.Min(bounds.size.x,bounds.size.z)*.52f, .8f, 12f);
            p.TrackEnd = endGround;
            float startBed = Ground(touch,out p.TrackStart,out _);
            p.Touch = Seat(p,touch,p.TouchRotation,out float touchDepth);
            p.Middle = Seat(p,middle,Quaternion.Slerp(p.TouchRotation,p.Rest,.68f),out float midDepth);
            p.End = Seat(p,end,p.Rest,out float endDepth);
            p.DryTrack = startBed == 0f && endBed == 0f && Ground(middle,out _,out _) == 0f;
            float draft = Mathf.Max(1f,bounds.size.y*.7f);
            p.Touch.y -= Mathf.Min(touchDepth,draft*.72f);
            p.Middle.y -= Mathf.Min(midDepth,draft);
            float immersion = Mathf.Min(endDepth,draft*1.15f);
            p.End.y -= immersion;
            if (endDepth > immersion)
            {
                float flood = pod ? 26f : wing ? R(3.2f,9.5f) : R(11f,22f);
                p.Flood = Mathf.Min(endDepth-immersion,flood);
            }
            p.Skin.Clear();
            settleTime = Mathf.Max(settleTime,p.Flight+p.Slide+(p.Flood > 0 ? 11f : 0f));
        }

        // Return the available water column over the local seabed. A carrier can span
        // a coastline; each support resolves its own actual dry ground or water level.
        private float Ground(Vector3 at, out Vector3 point, out Vector3 normal)
        {
            point = at;
            normal = Vector3.up;
            float sea = -Impact.Contact.y;
            Vector3 from = transform.TransformPoint(new Vector3(at.x, 420f, at.z));
            if (Physics.Raycast(from,Vector3.down,out RaycastHit hit,920f,PhysicsLayers.StaticsMask,QueryTriggerInteraction.Ignore))
            {
                point.y = transform.InverseTransformPoint(hit.point).y;
                if (!MegaplaneCrash.IsWater(hit.collider) && point.y >= sea-.03f)
                {
                    normal = hit.normal.y > .2f ? hit.normal : Vector3.up;
                    return 0f;
                }
                float depth = sea-point.y;
                if (MegaplaneCrash.IsWater(hit.collider))
                {
                    from.y = Datum.LocalSeaY-.10f;
                    depth = Physics.Raycast(from,Vector3.down,out RaycastHit bed,500f,PhysicsLayers.StaticsMask,
                        QueryTriggerInteraction.Ignore) && !MegaplaneCrash.IsWater(bed.collider)
                        ? Datum.LocalSeaY-bed.point.y : 500f;
                }
                point.y = sea;
                return Mathf.Max(.01f,depth);
            }
            normal = Impact.Normal.y > .2f ? Impact.Normal : Vector3.up;
            point.y = -(at.x*normal.x+at.z*normal.z)/normal.y;
            if (!Impact.Water && point.y > sea+.03f) return 0f;
            point.y = sea;
            normal = Vector3.up;
            return 500f;
        }

        private Vector3 Seat(Piece piece, Vector3 at, Quaternion rotation, out float depth)
        {
            // Nine actual low points cover the footprint of even a mostly intact wing. A
            // single center height leaves the downhill wing buried or the other end airborne.
            Bounds box = new Bounds(rotation*piece.Skin[0],Vector3.zero);
            foreach (Vector3 vertex in piece.Skin) box.Encapsulate(rotation*vertex);
            var bottom = new Vector3[9];
            for (int i=0; i<9; i++) bottom[i].y = float.PositiveInfinity;
            foreach (Vector3 v in piece.Skin)
            {
                Vector3 pt = rotation*v;
                int x = Mathf.Min(2,(int)(3f*Mathf.InverseLerp(box.min.x,box.max.x,pt.x)));
                int z = Mathf.Min(2,(int)(3f*Mathf.InverseLerp(box.min.z,box.max.z,pt.z)));
                int slot = 3*z+x;
                if (pt.y < bottom[slot].y) bottom[slot] = pt;
            }
            float height = float.NegativeInfinity;
            float bedSupport = float.NegativeInfinity;
            foreach (Vector3 pt in bottom)
            {
                if (float.IsInfinity(pt.y)) continue;
                float water = Ground(at+pt,out Vector3 ground,out _);
                height = Mathf.Max(height,ground.y-pt.y);
                bedSupport = Mathf.Max(bedSupport,ground.y-water-pt.y);
            }
            depth = Mathf.Max(0f,height-bedSupport);
            at.y = height-.05f;
            return at;
        }

        private void Pose(float age)
        {
            foreach (Piece p in moving)
            {
                if (age < p.Flight)
                {
                    float t = Mathf.Clamp01(age/p.Flight);
                    p.Frame.SetLocalPositionAndRotation(Vector3.Lerp(p.Start,p.Touch,t)+Vector3.up*(p.Hop*4f*t*(1f-t)),
                        Quaternion.Slerp(p.Initial,p.TouchRotation,t));
                    continue;
                }
                float slide = Mathf.Clamp01((age-p.Flight)/p.Slide);
                float q = 1f-(1f-slide)*(1f-slide);
                Vector3 pos = q < .5f ? Vector3.Lerp(p.Touch,p.Middle,q*2f) : Vector3.Lerp(p.Middle,p.End,2f*q-1f);
                pos.y += Mathf.Sin(slide*Mathf.PI)*Mathf.Exp(-4f*slide)*Mathf.Min(p.Hop*.19f,2.5f);
                if (p.Flood > 0) pos.y -= p.Flood*Mathf.SmoothStep(0f,1f,(age-p.Flight-p.Slide)/11f);
                p.Frame.SetLocalPositionAndRotation(pos,Quaternion.Slerp(p.TouchRotation,p.Rest,q));
            }
        }

        private void Update()
        {
            float age = Age;
            if (age >= RetentionSeconds) { Expire(); return; }
            if (rested) return;
            Pose(age);
            rested = Settled;
        }

        private void OnDestroy() => Fields.Remove(this);
    }
}
