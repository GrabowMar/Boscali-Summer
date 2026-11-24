using System.Linq;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Spins the engine fans of the art: "Fan_Pod_*" nodes (blades + spiral-painted spinner, origin on the spin axis,
    /// build_megaplane pod()) turn about the aircraft's forward axis. Each fan spools toward the speed available
    /// from its own engine. Damaged engines slow independently and a detached fan coasts down on the falling pod.</summary>
    internal sealed class MegaplaneFans : MonoBehaviour
    {
        // On-screen rate, not real rpm: a 15 m fan at its real ~600 rpm (3,600 deg/s) strobes at frame rate and reads as
        // standing still or turning backwards. These rates keep the spiral readable.
        private const float BigRate = 420f, SmallRate = 640f, Idle = .22f, SpoolUp = .12f, SpoolDown = .08f;
        private Aircraft aircraft;
        private MegaplaneReactor reactor;
        private Transform[] fans;
        private Quaternion[] rest;
        private AeroPart[] engines;
        private float[] rate, angle, rotorSpeed;

        internal float Spool { get; private set; }
        internal int Count => fans?.Length ?? 0;
#if !PUBLIC_RELEASE
        internal float SpeedOf(AeroPart engine)
        {
            int index = System.Array.IndexOf(engines, engine);
            return index >= 0 ? rotorSpeed[index] : 0f;
        }
#endif

        private void Awake()
        {
            aircraft = GetComponent<Aircraft>();
            reactor = GetComponent<MegaplaneReactor>();
            fans = GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Fan_Pod_")).ToArray();
            engines = fans.Select(t => t.GetComponentInParent<AeroPart>(true)).ToArray();
            rest = fans.Select(t => t.localRotation).ToArray();
            rate = fans.Select(t => t.name.EndsWith("1") || t.name.EndsWith("2") ? BigRate : SmallRate).ToArray();   // pods 1-2 big, 3-4 small
            angle = new float[fans.Length];
            rotorSpeed = new float[fans.Length];
        }

        private void Update()
        {
            if (aircraft == null || fans.Length == 0) return;
            float throttle = aircraft.disabled ? 0f : Mathf.Lerp(Idle, 1f, Mathf.Clamp01(aircraft.GetInputs().throttle));
            if (reactor != null) throttle *= reactor.AvailablePower;
            float sum = 0f;
            for (int i = 0; i < fans.Length; i++)
            {
                Transform f = fans[i];
                if (f == null) continue;
                float target = f.IsChildOf(transform) ? throttle * MegaplaneFlight.PodHealth(engines[i]) : 0f;
                float change = target > rotorSpeed[i] ? SpoolUp : SpoolDown;
                rotorSpeed[i] = Mathf.MoveTowards(rotorSpeed[i], target, change * Time.deltaTime);
                sum += rotorSpeed[i];
                angle[i] = Mathf.Repeat(angle[i] + rate[i] * rotorSpeed[i] * Time.deltaTime, 360f);
                // the art keeps every node unrotated, so local forward is the engine axis (Blender +Y -> Unity +Z)
                f.localRotation = rest[i] * Quaternion.AngleAxis(angle[i], Vector3.forward);
            }
            Spool = sum / fans.Length;
        }
    }
}
