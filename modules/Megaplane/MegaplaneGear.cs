using System.Linq;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Retractable undercarriage of the art (build_megaplane details/detail_gear.py): "Gear_*" legs have their origin on
    /// the retraction hinge and are modelled down; they swing 90 deg about their lateral axis into the bays, forward unless
    /// the name ends in "_Aft". "GearDoor_*_L/_R" have their origin on the outboard hinge line and are modelled closed; they
    /// open 90 deg down and outboard (the fighter-bay door convention). Follows the networked gearDeployed flag, so every
    /// peer animates the same: doors open, legs travel, and on retraction legs stow before the doors close.</summary>
    internal sealed class MegaplaneGear : MonoBehaviour
    {
        private const float LegRate = 1f / 9f, DoorRate = 1f / 3f;   // a 6,500 t undercarriage takes its time
        private Aircraft aircraft;
        private Transform[] legs, doors;
        private Quaternion[] legRest, doorRest;
        private float[] legSign, doorSign;
        private bool initialized;

        internal float Leg { get; private set; }   // 0 stowed .. 1 down
        internal float Door { get; private set; }  // 0 closed .. 1 open
        internal int Legs => legs?.Length ?? 0;

        private void Awake()
        {
            aircraft = GetComponent<Aircraft>();
            Transform[] all = GetComponentsInChildren<Transform>(true);
            legs = all.Where(t => t.name.StartsWith("Gear_")).ToArray();
            doors = all.Where(t => t.name.StartsWith("GearDoor_") && (t.name.EndsWith("_L") || t.name.EndsWith("_R"))).ToArray();
            legRest = legs.Select(t => t.localRotation).ToArray();
            doorRest = doors.Select(t => t.localRotation).ToArray();
            legSign = legs.Select(t => t.name.EndsWith("_Aft") ? 1f : -1f).ToArray();   // -90 about +X swings the foot forward
            doorSign = doors.Select(t => t.name.EndsWith("_L") ? -1f : 1f).ToArray();
        }

        private void Update()
        {
            if (aircraft == null || legs.Length == 0) return;
            bool down = aircraft.NetworkgearDeployed;
            if (!initialized)   // spawn in the commanded pose (air starts: stowed)
            {
                Leg = Door = down ? 1f : 0f;
                initialized = true;
            }
            float dt = Time.deltaTime;
            if (down)
            {
                Door = Mathf.MoveTowards(Door, 1f, DoorRate * dt);
                if (Door >= 1f) Leg = Mathf.MoveTowards(Leg, 1f, LegRate * dt);
            }
            else
            {
                Leg = Mathf.MoveTowards(Leg, 0f, LegRate * dt);
                if (Leg <= 0f) Door = Mathf.MoveTowards(Door, 0f, DoorRate * dt);
            }
            float stow = 90f * (1f - Mathf.SmoothStep(0f, 1f, Leg));
            for (int i = 0; i < legs.Length; i++)
                if (legs[i] != null) legs[i].localRotation = legRest[i] * Quaternion.AngleAxis(legSign[i] * stow, Vector3.right);
            float open = 90f * Mathf.SmoothStep(0f, 1f, Door);
            for (int i = 0; i < doors.Length; i++)
                if (doors[i] != null) doors[i].localRotation = doorRest[i] * Quaternion.AngleAxis(doorSign[i] * open, Vector3.forward);
        }
    }
}
