using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>One forward assist sponson. Its native AeroPart owns both the hitbox and the moving artwork.</summary>
    internal sealed class MegaplaneNoseFan : MonoBehaviour
    {
        [SerializeField] private AeroPart owner;
        [SerializeField] private Transform innerLid, outerLid, rotor;
        [SerializeField] private float side;
        private Quaternion innerRest, outerRest, rotorRest;
        private float opening, spool, angle;

        internal AeroPart Owner => owner;
        internal float Power { get; private set; }
        internal float LidAngle => innerLid == null ? 0f : Quaternion.Angle(innerRest, innerLid.localRotation);

        internal void Bind(Dictionary<string, Transform> nodes, string suffix)
        {
            owner = GetComponentInParent<AeroPart>(true);
            innerLid = nodes["Detail_AssistLidInner_" + suffix];
            outerLid = nodes["Detail_AssistLidOuter_" + suffix];
            rotor = nodes["Detail_AssistFan_" + suffix];
            side = suffix == "L" ? -1f : 1f;
        }

        private void Awake()
        {
            innerRest = innerLid.localRotation;
            outerRest = outerLid.localRotation;
            rotorRest = rotor.localRotation;
        }

        internal void Step(float demand, float dt)
        {
            bool mounted = owner != null && !owner.IsDetached();
            float health = mounted ? Mathf.Clamp01(owner.hitPoints * .01f) : 0f;
            float target = demand * health;
            // Open before the fan spools up, and coast down before the lids meet.
            float lidTarget = Mathf.Clamp01(3f * Mathf.Max(target, spool));
            opening = Mathf.MoveTowards(opening, lidTarget, dt / 2f);
            spool = Mathf.MoveTowards(spool, Mathf.Min(target, opening), dt / 3f);
            float degrees = 68f * side * opening;
            innerLid.localRotation = innerRest * Quaternion.AngleAxis(degrees, Vector3.forward);
            outerLid.localRotation = outerRest * Quaternion.AngleAxis(-degrees, Vector3.forward);
            angle = Mathf.Repeat(angle + side * 510f * spool * dt, 360f);
            rotor.localRotation = rotorRest * Quaternion.AngleAxis(angle, Vector3.up);
            Power = Mathf.Min(spool, health);
        }
    }
}
