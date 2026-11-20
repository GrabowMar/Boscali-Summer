using System.Linq;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Spins the engine fans of the art: "Fan_Pod_*" nodes (blades + spiral-painted spinner, origin on the spin axis,
    /// build_megaplane pod()) turn about the aircraft's forward axis. Speed follows the throttle through a slow spool (big fans
    /// take seconds to wind up and run down), idling when the throttle is closed. A fan whose pod was shot off (no longer under
    /// the aircraft) or the whole aircraft once disabled windmills down to a stop. Visual only, every peer runs its own.</summary>
    internal sealed class MegaplaneFans : MonoBehaviour
    {
        // On-screen rate, not real rpm: a 15 m fan at its real ~600 rpm (3,600 deg/s) strobes at frame rate and reads as
        // standing still or turning backwards. These rates keep the spiral readable.
        private const float BigRate = 420f, SmallRate = 640f, Idle = .22f, SpoolUp = .12f, SpoolDown = .08f;
        private Aircraft aircraft;
        private Transform[] fans;
        private Quaternion[] rest;
        private float[] rate, angle;
        private float spool;

        internal float Spool => spool;
        internal int Count => fans?.Length ?? 0;

        private void Awake()
        {
            aircraft = GetComponent<Aircraft>();
            fans = GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Fan_Pod_")).ToArray();
            rest = fans.Select(t => t.localRotation).ToArray();
            rate = fans.Select(t => t.name.EndsWith("1") || t.name.EndsWith("2") ? BigRate : SmallRate).ToArray();   // pods 1-2 big, 3-4 small
            angle = new float[fans.Length];
        }

        private void Update()
        {
            if (aircraft == null || fans.Length == 0) return;
            bool alive = !aircraft.disabled;
            float target = alive ? Mathf.Lerp(Idle, 1f, Mathf.Clamp01(aircraft.GetInputs().throttle)) : 0f;
            spool = Mathf.MoveTowards(spool, target, (target > spool ? SpoolUp : SpoolDown) * Time.deltaTime);
            for (int i = 0; i < fans.Length; i++)
            {
                Transform f = fans[i];
                if (f == null) continue;
                float s = f.IsChildOf(transform) ? spool : 0f;
                angle[i] = Mathf.Repeat(angle[i] + rate[i] * s * Time.deltaTime, 360f);
                // the art keeps every node unrotated, so local forward is the engine axis (Blender +Y -> Unity +Z)
                f.localRotation = rest[i] * Quaternion.AngleAxis(angle[i], Vector3.forward);
            }
        }
    }
}
