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
        internal int DelayedBreaks { get; private set; }
        internal int ReleasedBreaks => moving.Count(p => p.Mount != null && Age >= p.Release);
        internal float LastBreakTime { get; private set; }
        internal float PeakPodRoll { get; private set; }
        internal float PeakHullHop { get; private set; }
        internal float PeakPanelHop { get; private set; }

        // Geometrical supports indexed by GroupNames. Flaps and small captive items
        // hang on their own piece of wing or body until enough energy tears them free.
        private static readonly int[] Parents = {
            -1,0,1,0,3,0, 0,6,7,8,9, 0,11,12,13,14, 6,11,16,17,16,
            6,7,8,9,11,12,13,14, 2,2,7,12,8,13,9,14,18,19,0,0,0,0,0,0,0, 2,2,2,2
        };
        private static readonly float[] Split = {
            0,.54f,.69f,.48f,.39f,.64f, .54f,.71f,.36f,.64f,.39f, .54f,.71f,.36f,.64f,.39f,
            .44f,.44f,.57f,.57f,.53f, .35f,.42f,.31f,.28f,.35f,.42f,.31f,.28f,
            .76f,.76f,.82f,.82f,.75f,.75f,.83f,.83f,.99f,.99f,2,2,.91f,.91f,2,2,2, .69f,.69f,.84f,.73f
        };

        private sealed class Piece
        {
            internal int Group;
            internal Transform Frame;
            internal readonly List<Vector3> Skin = new List<Vector3>();
            internal readonly List<Renderer> Near = new List<Renderer>(), Far = new List<Renderer>();
            internal Piece Mount;
            internal Bounds Bounds;
            internal Vector3 Start, MountOffset, SpinAxis, TrackStart, TrackEnd;
            internal Vector3[] SlidePath;
            internal Quaternion Initial, TouchRotation, Finish;
            internal float Release, Flight, Slide, Hop, BounceTime, Spin, Width, Flood;
            internal bool DryTrack, WaterEntry;
        }

        private readonly List<Piece> moving = new List<Piece>();
        private readonly List<Transform> frames = new List<Transform>();
        private float born, settleTime;
        private bool rested;

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
            string contactSkin = !Impact.Water && severity > .35f ? MegaplaneSkinDamage.ContactSite(prefab.transform, Impact.Surviving, contactInAirframe) : null;
            foreach (MeshFilter source in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                bool far = source.name.StartsWith("GrozaLOD_", StringComparison.Ordinal);
                bool paint = source.name.StartsWith("Detail_Livery_", StringComparison.Ordinal);
                bool service = source.name.StartsWith("Detail_SparAccess_", StringComparison.Ordinal) ||
                    source.name.StartsWith("Detail_HullPlateCatches_", StringComparison.Ordinal) || source.name == "Detail_ForwardAerials";
                if ((!far && !paint && !service) || source.sharedMesh == null) continue;
                string artName = far ? source.transform.parent.name : source.name;
                if (artName.StartsWith("ForeHullProxy_", StringComparison.Ordinal)) continue;
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
                    artName.StartsWith("Detail_ForeBay_", StringComparison.Ordinal) ||
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
                    ulong flag = 1UL << group;
                    int damageStage = (Impact.HeavilyDamagedSkin & flag) != 0 ? 2 : (Impact.DamagedSkin & flag) != 0 ? 1 : 0;
                    MegaplaneSkinDamage.Wreck(leaf.transform, damageStage, contactSkin, piece.Near, piece.Far);
                    MegaplaneWreckArt.Add(leaf.transform, artName, fore >= 0 && clusters[fore] != cluster,
                        aft >= 0 && clusters[aft] != cluster);
                }
                if (crush != null) crush.Apply(piece.Frame,leaf.transform);
                if (artName == Names[group])
                {
                    foreach (Transform child in leaf.transform)
                    {
                        if (child == near.transform || child.name.StartsWith("GrozaSkinDamage_", StringComparison.Ordinal)) continue;
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
            foreach (Piece piece in moving) CenterGeometry(piece);
            // A mount's impact pose must be known before planning a section shed from it.
            foreach (Piece piece in moving.OrderBy(p => p.Group))
            {
                if (severity >= .68f && !Impact.Water &&
                    (piece.Group == 10 || piece.Group == 15 || piece.Group == 22 || piece.Group == 26))
                {
                    int parent = clusters[Parents[piece.Group]];
                    if (parent >= 0 && parent != piece.Group && pieces.TryGetValue(parent,out Piece mount) && !mount.WaterEntry)
                        piece.Mount = mount;
                }
                Plan(piece, severity);
            }
            foreach (Piece p in moving.Where(p => p.DryTrack && p.Width > 4f && (p.TrackEnd - p.TrackStart).sqrMagnitude > 36f)
                .OrderByDescending(p => p.Width * (p.TrackEnd-p.TrackStart).magnitude).Take(3))
                MegaplaneWreckMarks.Add(transform,p.TrackStart,p.TrackEnd,p.Width,false,p.Release+p.Flight,p.Slide,Impact.Age);
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

        private void CenterGeometry(Piece p)
        {
            Bounds bounds = new Bounds(p.Skin[0], Vector3.zero);
            foreach (Vector3 point in p.Skin) bounds.Encapsulate(point);
            Vector3 pivot = bounds.center;
            p.Bounds = bounds;
            foreach (Transform child in p.Frame) child.localPosition -= pivot;
            var levels = p.Frame.gameObject.AddComponent<LODGroup>();
            levels.SetLODs(new[] { new LOD(.20f,p.Near.ToArray()), new LOD(.0005f,p.Far.ToArray()) });
            levels.RecalculateBounds();
            levels.size = 90f;
            for (int i = 0; i < p.Skin.Count; i++) p.Skin[i] -= pivot;
        }

        private void Plan(Piece p, float severity)
        {
            var rand = new System.Random(unchecked((int)(Impact.Spawn * 1103515245u) + p.Group * 4657));
            float R(float a,float b) => Mathf.Lerp(a,b,(float)rand.NextDouble());
            p.Initial = Impact.Rotation;
            Vector3 lever = p.Initial * p.Bounds.center;
            p.Start = Impact.Origin - Impact.Contact + lever;
            Vector3 velocity = Impact.Velocity + Vector3.ClampMagnitude(Vector3.Cross(Impact.AngularVelocity, lever), 100f);
            if (p.Mount != null)
            {
                // The wingtip/engine remains bolted on as its wing first ploughs into the surface.
                // Inherit the velocity at this particular mount, including rotation of the wing.
                Piece mount = p.Mount;
                p.MountOffset = p.Bounds.center - mount.Bounds.center;
                p.Release = mount.Release + mount.Flight + Mathf.Clamp(mount.Slide * R(.13f,.22f), .34f, 1.35f);
                Evaluate(mount, p.Release-.04f, out Vector3 before, out Quaternion previous);
                Evaluate(mount, p.Release, out Vector3 here, out p.Initial);
                p.Start = here + p.Initial * p.MountOffset;
                velocity = Vector3.ClampMagnitude((p.Start-before-previous*p.MountOffset)/.04f, velocity.magnitude);
                DelayedBreaks++;
                LastBreakTime = Mathf.Max(LastBreakTime, p.Release);
            }
            Ground(p.Start, out _, out Vector3 firstNormal);
            Vector3 seated = Seat(p,p.Start,p.Initial,out _);
            float closing = -Vector3.Dot(velocity, firstNormal);
            float clearance = Mathf.Max(0f,(p.Start.y-seated.y)*firstNormal.y);
            float gravity = 9.81f * Mathf.Max(.25f,firstNormal.y);
            float impactSpeed = Mathf.Sqrt(closing*closing+2f*gravity*clearance);
            p.Flight = Mathf.Clamp((impactSpeed-closing)/gravity,.045f,4.5f);
            Vector3 tangent = Vector3.ProjectOnPlane(velocity, firstNormal);
            Vector3 axial = Vector3.ProjectOnPlane(tangent, Vector3.up).normalized;
            if (axial.sqrMagnitude < .01f) axial = Vector3.ProjectOnPlane(Impact.Rotation * Vector3.forward, Vector3.up).normalized;
            if (axial.sqrMagnitude < .01f) axial = Vector3.forward;
            Vector3 cross = Vector3.Cross(Vector3.up, axial);
            bool pod = p.Group >= 21 && p.Group <= 28;
            bool wing = p.Group >= 6 && p.Group <= 15;
            bool panel = wing || p.Group >= 31 && p.Group <= 38 || p.Group >= 46;
            bool hull = p.Group >= 0 && p.Group <= 4;
            float speed = tangent.magnitude;
            Vector3 touch = p.Start + Vector3.ProjectOnPlane(velocity, Vector3.up)*p.Flight;
            float touchBed = Ground(touch,out p.TrackStart,out Vector3 touchNormal);
            p.WaterEntry = touchBed > 0f;
            // Long hull barrels gouge the surface. Pods run on farther, whereas thin
            // panels shed speed fast. Offset develops sideways only from the asymmetry of the strike.
            float skid = Mathf.Min(225f,(.25f*speed+.008f*speed*speed)*R(.85f,1.12f)*
                (pod ? 1.28f : panel ? .66f : .97f)*(p.WaterEntry ? .41f : 1f));
            float side = Mathf.Clamp(Vector3.Dot(lever,cross)/72f,-1f,1f);
            float deflect = side*Mathf.Min(impactSpeed,105f)*severity*(pod ? .18f : panel ? .25f : .065f);
            Vector3 downhill = Vector3.ProjectOnPlane(Vector3.ProjectOnPlane(Vector3.down,touchNormal),Vector3.up);
            Vector3 travel = axial*skid + cross*deflect*(p.WaterEntry ? .15f : 1f) + downhill*(pod ? 15f : 4f)*severity;
            Vector3 end = touch+travel;
            float endBed = Ground(end,out p.TrackEnd,out Vector3 normal);
            Vector3 omega = Impact.AngularVelocity;
            p.TouchRotation = omega.sqrMagnitude > .00001f && p.Mount == null ?
                Quaternion.AngleAxis(Mathf.Min(130f,omega.magnitude*p.Flight*Mathf.Rad2Deg),omega.normalized)*p.Initial : p.Initial;
            Vector3 heading = Vector3.ProjectOnPlane(p.TouchRotation*Vector3.forward,normal).normalized;
            if (heading.sqrMagnitude < .01f) heading = Vector3.ProjectOnPlane(axial,normal).normalized;
            float turn = (side*.42f+R(-.65f,.65f))*severity*(pod ? 84f : panel ? 61f : 25f);
            if (p.WaterEntry) turn *= .18f;
            float inverted = Vector3.Dot(p.Initial*Vector3.up,normal)<-.24f ? 180f : 0f;
            p.Finish = Quaternion.AngleAxis(turn,normal)*Quaternion.LookRotation(heading,normal)*
                Quaternion.Euler(R(-4f,4f),0f,inverted+R(-9f,9f)*severity);
            // A cylindrical nacelle settles nose-down as it floods, without rolling along the sea.
            if (p.WaterEntry && pod && endBed > 0f)
                p.Finish *= Quaternion.AngleAxis(inverted == 0f ? 18f : -18f, Vector3.right);
            bool dry = !Impact.Water && !p.WaterEntry && endBed == 0f;
            float restitution = hull ? R(.055f,.085f) : pod ? R(.105f,.145f) : panel ? R(.14f,.21f) : .11f;
            float reboundSpeed = Mathf.Min(impactSpeed,100f)*restitution;
            if (dry) p.Hop = Mathf.Min(hull ? 3.4f : pod ? 9f : 15f,reboundSpeed*reboundSpeed / 19.62f);
            p.BounceTime = Mathf.Sqrt(8f*p.Hop/9.81f);
            if (hull) PeakHullHop = Mathf.Max(PeakHullHop,p.Hop);
            if (panel) PeakPanelHop = Mathf.Max(PeakPanelHop,p.Hop);
            p.SpinAxis = pod ? Vector3.forward : Vector3.right;
            if (dry && pod)
            {
                Vector3 sideways = Vector3.Cross(p.Finish*Vector3.forward,normal).normalized;
                float radius = Mathf.Max(1f, .5f*Mathf.Min(p.Bounds.size.x,p.Bounds.size.y));
                p.Spin = Mathf.Clamp(Vector3.Dot(travel,sideways)/radius*Mathf.Rad2Deg*.74f,-650f,650f);
                PeakPodRoll = Mathf.Max(PeakPodRoll,Mathf.Abs(p.Spin));
            }
            else if (dry && panel && severity > .5f && turn != 0f)
                p.Spin = Mathf.Sign(turn)*Mathf.Lerp(38f,210f,severity)*R(.50f,1.15f);
            p.Slide = Mathf.Clamp(Mathf.Max(2f*travel.magnitude/Mathf.Max(12f,speed*.74f),p.BounceTime*1.6f),1.45f,8.5f);
            p.Width = Mathf.Clamp(Mathf.Min(p.Bounds.size.x,p.Bounds.size.z)*.52f,.8f,12f);
            p.DryTrack = touchBed == 0f && endBed == 0f && Ground((touch+end)*.5f,out _,out _) == 0f;
            // Sample the lower skin at each attitude, including intermediate full turns.
            // Sliding a long panel on its centre height alone would drive its ends underground.
            int steps = Mathf.Clamp(Mathf.Max(Mathf.CeilToInt(Mathf.Abs(p.Spin)/35f),
                Mathf.CeilToInt(travel.magnitude/44f)),2,19);
            p.SlidePath = new Vector3[steps+1];
            float draft = Mathf.Max(1f,p.Bounds.size.y*.7f);
            for (int i=0; i<=steps; i++)
            {
                float q = i/(float)steps;
                Vector3 foot = Seat(p,Vector3.Lerp(touch,end,q),SlideAttitude(p,q),out float depth);
                float immersion = Mathf.Min(depth,draft*Mathf.Lerp(.72f,1.15f,q));
                foot.y -= immersion;
                p.SlidePath[i] = foot;
                if (i == steps && depth > immersion)
                    p.Flood = Mathf.Min(depth-immersion,pod ? 26f : panel ? R(3.2f,9.5f) : R(11f,22f));
            }
            p.Skin.Clear();
            settleTime = Mathf.Max(settleTime,p.Release+p.Flight+p.Slide+(p.Flood > 0 ? 11f : 0f));
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

        private static Quaternion SlideAttitude(Piece p, float distance) =>
            Quaternion.Slerp(p.TouchRotation,p.Finish,distance) * Quaternion.AngleAxis(p.Spin*distance,p.SpinAxis);

        private static void Evaluate(Piece p, float age, out Vector3 pos, out Quaternion rotation)
        {
            if (p.Mount != null && age < p.Release)
            {
                Evaluate(p.Mount,age,out pos,out rotation);
                pos += rotation*p.MountOffset;
                return;
            }
            float airborne = age - p.Release;
            if (airborne < p.Flight)
            {
                float t = Mathf.Clamp01(airborne/p.Flight);
                pos = Vector3.Lerp(p.Start,p.SlidePath[0],t);
                pos.y += 4.905f*p.Flight*p.Flight*t*(1f-t);
                rotation = Quaternion.Slerp(p.Initial,p.TouchRotation,t);
                return;
            }
            float grounded = airborne-p.Flight;
            float tSlide = Mathf.Clamp01(grounded/p.Slide);
            float remaining = 1f-tSlide;
            float distance = 1f-remaining*remaining*(p.WaterEntry ? remaining : 1f);
            float path = distance*(p.SlidePath.Length-1);
            int index = Mathf.Min((int)path,p.SlidePath.Length-2);
            pos = Vector3.Lerp(p.SlidePath[index],p.SlidePath[index+1],path-index);
            rotation = SlideAttitude(p,distance);
            if (p.BounceTime > .05f && grounded < p.BounceTime*1.38f)
            {
                float u = grounded/p.BounceTime;
                float height = p.Hop;
                if (u > 1f) { u = (u-1f)/.38f; height *= .38f*.38f; }
                pos.y += 4f*height*u*(1f-u);
            }
            if (p.Flood > 0f)
                pos.y -= p.Flood*Mathf.SmoothStep(0f,1f,(grounded-p.Slide)/11f);
        }

        private void Pose(float age)
        {
            foreach (Piece p in moving)
            {
                Evaluate(p,age,out Vector3 pos,out Quaternion rotation);
                p.Frame.SetLocalPositionAndRotation(pos,rotation);
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
