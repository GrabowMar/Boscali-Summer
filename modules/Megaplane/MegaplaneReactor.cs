using System.Linq;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Fictional plant failure for the Groza. Only the host advances the plant or disables the carrier.</summary>
    internal sealed class MegaplaneReactor : MonoBehaviour
    {
        internal const byte Flash = 11, Spent = 12;
        private AeroPart core, left, right;
        private AeroPart[] surrounding;
        private Aircraft carrier;
        private MegaplaneFx effects;
        private Vector3 plantPosition;
        private float flashUntil;
        internal float Heat { get; private set; }
        internal byte State { get; private set; }  // 0..10 heat bands, then flash and spent
        internal int Flashes { get; private set; }
        internal uint UnitId => carrier.persistentID.Id;
        internal bool Installed { get; private set; }
        internal float AvailablePower => !Installed ? 1f : State >= Flash ? 0f :
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.04f, .70f, MegaplaneFlight.PodHealth(core)));

        private void Awake()
        {
            carrier = GetComponent<Aircraft>();
            effects = GetComponent<MegaplaneFx>();
            var parts = GetComponentsInChildren<AeroPart>(true);
            core = parts.FirstOrDefault(p => p.name == "reactor");
            left = parts.FirstOrDefault(p => p.name == "coolant_L");
            right = parts.FirstOrDefault(p => p.name == "coolant_R");
            Installed = core != null && left != null && right != null;
            surrounding = parts.Where(p => p.name == "hump" || p.name == "body_F" || p.name == "fuselage_R").ToArray();
            var vent = GetComponentsInChildren<Transform>(true).FirstOrDefault(p => p.name == "ReactorVent");
            if (core != null) plantPosition = transform.InverseTransformPoint(vent != null ? vent.position : core.transform.position);
        }

        private void OnEnable() => MegaplaneNet.Register(this);
        private void OnDisable() => MegaplaneNet.Unregister(this);

        private void Update()
        {
            if (!carrier.IsServer || State == Spent) return;
            if (State == Flash)
            {
                if (Time.time >= flashUntil) Publish(Spent);
                return;
            }
            if (!Installed || core == null) return;
            float cooling = Mathf.Max(MegaplaneFlight.PodHealth(left), MegaplaneFlight.PodHealth(right));
            bool critical = core.hitPoints <= 35f;
            bool spreading = critical && surrounding.Count(p => p != null && !p.IsDetached() && p.hitPoints < 50f) >= 2;
            // A serviceable cooling circuit can arrest the rise. A spreading central fire takes longer.
            float rate = critical && cooling < .5f ? Mathf.Lerp(1f / 20f, 1f / 34f, cooling * 2f)
                       : spreading ? 1f / 48f : -1f / 12f;
            Heat = Mathf.Clamp01(Heat + rate * Time.deltaTime);
            if (Heat < 1f)
            {
                Publish((byte)Mathf.CeilToInt(Heat * 10f));
                return;
            }
            flashUntil = Time.time + 2.5f;
            Publish(Flash);
            if (!carrier.disabled) { carrier.Networkdisabled = true; carrier.ReportKilled(); }
        }

        private void Publish(byte state)
        {
            if (State == state) return;
            Apply(state);
            MegaplaneNet.Broadcast(UnitId, MegaplaneWire.ReactorIndex, state);
        }

        internal void RemoteState(byte state)
        {
            if (carrier.IsServer || state > Spent || State == Spent || State == Flash && state != Spent) return;
            Heat = Mathf.Clamp01(state / 10f);
            Apply(state);
        }

        private void Apply(byte state)
        {
            if (State == state) return;
            MegaplaneReactorWarnings.Report(carrier, State, state);
            State = state;
            if (state != Flash) return;
            Flashes++;
            effects.PlantBreach(core, transform.TransformPoint(plantPosition));
        }
    }
}
