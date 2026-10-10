using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>One pivoted joint swung from its stowed (FBX) pose by angle x amount about a fixed axis in its parent frame.</summary>
    internal struct AegisHinge
    {
        public Transform Joint;
        public Quaternion Rest;
        public Vector3 Axis;
        public float Angle;

        public static AegisHinge Of(Transform joint, Vector3 worldAxis, float angle) => new AegisHinge
        {
            Joint = joint,
            Rest = joint.localRotation,
            // Direction only: the 100x FBX joint scale does not touch it.
            Axis = joint.parent != null ? joint.parent.InverseTransformDirection(worldAxis) : worldAxis,
            Angle = angle,
        };

        public void Apply(float amount)
        {
            if (Joint != null) Joint.localRotation = Quaternion.AngleAxis(Angle * amount, Axis) * Rest;
        }

        /// <summary>World centre of a joint's meshes (LODs are its children); the pivot when it has none.</summary>
        public static Vector3 Centre(Transform joint)
        {
            Renderer[] renderers = joint.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return joint.position;
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b.center;
        }

        public static Transform Find(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }
    }

    /// <summary>
    /// Every peer: AEGIS pod cell covers Door0..2. A cover swings open the moment its cell is fired (vanilla
    /// MountedMissile state, set by LaunchMount on every peer), is open before the rail drops the dart
    /// (cell railDelay = AegisSnap.DoorLead) and closes again behind it. A missing joint is skipped.
    /// </summary>
    internal sealed class AegisDoors : MonoBehaviour
    {
        private MountedMissile[] cells;
        private AegisHinge[] doors;
        private float[] firedAt;

        /// <summary>Rigs every pod the station holds: a two-station hardpoint set merges both pods into one 6-dart station.</summary>
        public static void Attach(WeaponStation station)
        {
            foreach (Weapon weapon in station.Weapons)
            {
                Transform pod = weapon != null && weapon is MountedMissile ? weapon.transform.parent : null;
                if (pod != null && pod.GetComponent<AegisDoors>() == null) Rig(pod);
            }
        }

        private static void Rig(Transform pod)
        {
            MountedMissile[] cells = pod.GetComponentsInChildren<MountedMissile>(true);
            Transform first = cells[0].transform;
            var doors = new AegisHinge[cells.Length];
            for (int i = 0; i < cells.Length; i++)
            {
                Transform door = AegisHinge.Find(pod, "Door" + CellIndex(cells[i], i));
                if (door == null) continue;
                float lateral = Vector3.Dot(AegisHinge.Centre(door) - door.position, first.right);
                doors[i] = AegisHinge.Of(door, first.forward, AegisSnap.DoorSwing(lateral));
            }
            var c = pod.gameObject.AddComponent<AegisDoors>();
            c.cells = cells;
            c.doors = doors;
            c.firedAt = new float[cells.Length];
            for (int i = 0; i < cells.Length; i++) c.firedAt[i] = -1f;
        }

        // Builder names cells AegisCell0..2 after the pod model's CELLS order, which also numbers the covers.
        private static int CellIndex(MountedMissile cell, int fallback)
        {
            string n = cell.name;
            return n.Length > 0 && char.IsDigit(n[n.Length - 1]) ? n[n.Length - 1] - '0' : fallback;
        }

        private void Update()
        {
            float now = Time.time;
            for (int i = 0; i < cells.Length; i++)
            {
                bool fired = cells[i] != null && cells[i].GetAmmoLoaded() == 0;
                if (!fired) firedAt[i] = -1f;
                else if (firedAt[i] < 0f)
                {
                    firedAt[i] = now;
                    if (doors[i].Joint != null) VanguardStats.Bump("aegis.doorOpen");
                }
                doors[i].Apply(firedAt[i] < 0f ? 0f : AegisSnap.Door(now - firedAt[i]));
            }
        }
    }

    /// <summary>Every peer: the dart's folded grid fins FinA..FinD snap radial 0.05-0.2 s after release (missile age).</summary>
    internal sealed class AegisFins : MonoBehaviour
    {
        private static readonly string[] Names = { "FinA", "FinB", "FinC", "FinD" };
        private Missile missile;
        private AegisHinge[] fins;

        public static void Attach(Missile missile)
        {
            if (VanguardKeys.RoleOf(missile.definition.jsonKey) != VanguardRole.Interceptor ||
                missile.GetComponent<AegisFins>() != null) return;
            Transform visual = missile.transform.Find("VanguardVisual");
            if (visual == null) return;
            Transform body = missile.transform;
            var fins = new AegisHinge[Names.Length];
            int found = 0;
            for (int i = 0; i < Names.Length; i++)
            {
                Transform fin = AegisHinge.Find(visual, Names[i]);
                if (fin == null) continue;
                float angle = AegisSnap.FinSwing(AegisHinge.Centre(fin) - fin.position, fin.position - body.position, body.forward, out Vector3 axis);
                if (angle <= 0f) continue;
                fins[i] = AegisHinge.Of(fin, axis, angle);
                found++;
            }
            if (found == 0) return;
            var c = missile.gameObject.AddComponent<AegisFins>();
            c.missile = missile;
            c.fins = fins;
        }

        private void Update()
        {
            if (missile == null || missile.disabled)
            {
                enabled = false;
                return;
            }
            float open = AegisSnap.Fins(missile.timeSinceSpawn);
            for (int i = 0; i < fins.Length; i++) fins[i].Apply(open);
            if (open < 1f) return;
            VanguardStats.Bump("aegis.finsOpen");
            enabled = false;
        }
    }
}
