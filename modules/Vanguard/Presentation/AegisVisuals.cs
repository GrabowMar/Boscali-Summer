using System;
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

        public static Vector3 Tip(Transform joint)
        {
            MeshFilter mesh = joint.GetComponentInChildren<MeshFilter>(true);
            return mesh != null && mesh.sharedMesh != null
                ? mesh.transform.TransformPoint(mesh.sharedMesh.bounds.center) - joint.position
                : Vector3.zero;
        }
    }

    /// <summary>Animates the fired cell's cover through the rail release on every peer.</summary>
    internal sealed class AegisDoors : MonoBehaviour
    {
        private static readonly Color Standby = new Color(0.22f, 0.30f, 0.31f);
        private static readonly Color Cycling = new Color(0.95f, 0.60f, 0.18f);
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int Smoothness = Shader.PropertyToID("_Smoothness");

        private MountedMissile[] cells;
        private AegisHinge[] doors;
        private float[] openedAt;
        private SkywellGlow status;

        public static void AttachPod(Transform pod)
        {
            if (pod == null || pod.GetComponent<AegisDoors>() != null) return;
            Transform visual = pod.Find("VanguardVisual");
            if (visual == null) visual = pod;
            MountedMissile[] cells = pod.GetComponentsInChildren<MountedMissile>(true);
            if (cells.Length == 0) return;
            var rig = pod.gameObject.AddComponent<AegisDoors>();
            rig.enabled = false;
            rig.cells = cells;
            rig.doors = new AegisHinge[cells.Length];
            rig.openedAt = new float[cells.Length];
            for (int i = 0; i < cells.Length; i++)
            {
                rig.openedAt[i] = float.NegativeInfinity;
                Transform door = StoreArt.Joint(visual, "Door" + CellIndex(cells[i], i));
                if (door == null) continue;
                Transform cell = cells[i].transform;
                float lateral = Vector3.Dot(AegisHinge.Tip(door), cell.right);
                rig.doors[i] = AegisHinge.Of(door, cell.forward, AegisSnap.DoorSwing(lateral));
            }
            rig.status = new SkywellGlow(visual);
            rig.status.Set(Standby, 0f);
            TintGlass(visual.Find("Glass"));
        }

        public static void Open(MountedMissile cell)
        {
            if (cell == null || cell.transform.parent == null) return;
            var rig = cell.transform.parent.GetComponent<AegisDoors>();
            if (rig == null) return;
            int index = Array.IndexOf(rig.cells, cell);
            if (index < 0 || rig.doors[index].Joint == null) return;
            rig.openedAt[index] = Time.time;
            rig.status.Set(Cycling, 0.22f);
            rig.enabled = true;
            VanguardStats.Bump("aegis.doorOpen");
        }

        private static void TintGlass(Transform glass)
        {
            if (glass == null) return;
            var block = new MaterialPropertyBlock();
            foreach (Renderer renderer in glass.GetComponentsInChildren<Renderer>(true))
            {
                renderer.GetPropertyBlock(block);
                block.SetColor(BaseColor, new Color(0.075f, 0.13f, 0.16f));
                block.SetFloat(Smoothness, 0.78f);
                renderer.SetPropertyBlock(block);
            }
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
            bool moving = false;
            for (int i = 0; i < doors.Length; i++)
            {
                if (float.IsNegativeInfinity(openedAt[i])) continue;
                float age = now - openedAt[i];
                doors[i].Apply(AegisSnap.Door(age));
                if (age < AegisSnap.DoorHold + AegisSnap.DoorClose) moving = true;
                else openedAt[i] = float.NegativeInfinity;
            }
            if (moving) return;
            status.Set(Standby, 0f);
            enabled = false;
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
                Transform fin = StoreArt.Joint(visual, Names[i]);
                if (fin == null) continue;
                float angle = AegisSnap.FinSwing(AegisHinge.Tip(fin), fin.position - body.position, body.forward, out Vector3 axis);
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
