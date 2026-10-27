using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Spooktober.Kaiju.Configuration;
using BoscaliSummer.Modules.Spooktober.Kaiju.Domain;
using BoscaliSummer.Modules.Spooktober.Kaiju.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Spooktober.Kaiju.Runtime
{
    /// <summary>
    /// One kaiju per mission. The host summons it (F1 button or the sim hook) in front of its own aircraft, runs the
    /// <see cref="KaijuBrain"/> and streams the pose ten times a second; clients build their own <see cref="KaijuBody"/>
    /// from the first state they hear and follow the stream.
    /// </summary>
    internal sealed class KaijuDirector : MonoBehaviour, ISceneService, IKaijuBoss
    {
        public static KaijuDirector Instance { get; private set; }

        private KaijuSettings settings;
        private KaijuBrain brain;
        private KaijuBody remote;
        private byte gen, lastAction = KaijuNet.Gone;
        private float nextSend, lastElapsed;

        public KaijuBrain Brain => brain;
        public KaijuBody Body => brain != null ? brain.Body : remote;

        // IKaijuBoss: what other modules (Spooktober) see through the Core contract.
        GameObject IKaijuBoss.Body => Body != null ? Body.gameObject : null;
        public bool Dead => Body != null && Body.Action == KaijuAction.Death;
        public int Generation => Body != null ? Body.Gen : -1;
        public event System.Action Defeated;

        public void Configure(KaijuSettings kaijuSettings)
        {
            settings = kaijuSettings;
            settings.Summon = Summon;
            settings.Dismiss = Dismiss;
            settings.Defeated += OnDefeated;
            KaijuBody.SpotScaleSetting = () => settings.WeakSpotScale.Value;
            Instance = this;
        }

        private void OnDefeated() => Defeated?.Invoke();

        private void OnDestroy()
        {
            if (settings != null) settings.Defeated -= OnDefeated;
        }

        public void ResetForScene()
        {
            brain = null; // the body lives under Datum.origin and goes with the scene
            if (remote != null) Destroy(remote.gameObject);
            remote = null;
            lastAction = KaijuNet.Gone;
        }

        private void Update()
        {
            KaijuAssets.EnsureRegistered(); // backstop for KaijuPrefabPatch; a no-op once registered
            if (brain == null) return;
            if (!GameAccess.IsServer() || !MissionManager.IsRunning)
            {
                Dismiss();
                return;
            }
            brain.Tick(Time.deltaTime);
            if (brain.Finished)
            {
                Dismiss();
                return;
            }
            if (Time.time < nextSend) return;
            nextSend = Time.time + 0.1f;
            Broadcast();
        }

        public string CannotSummon()
        {
            if (!settings.Enabled.Value) return "Kaiju is switched off in the config.";
            if (!GameAccess.IsServer() || !MissionManager.IsRunning) return "Host a running mission first.";
            if (KaijuAssets.Body == null) return "The kaiju art bundle is missing from this build.";
            return KaijuAssets.Node == null ? "No game building to clone for weak spots." : null;
        }

        public string Summon()
        {
            string why = CannotSummon();
            if (why != null) return why;
            Dismiss();
            Transform eye = GameManager.GetLocalAircraft(out Aircraft aircraft) && aircraft != null ? aircraft.transform
                : Camera.main != null ? Camera.main.transform : null;
            if (eye == null) return "No aircraft or camera to summon in front of.";
            Vector3 forward = eye.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.01f ? forward.normalized : Vector3.forward;
            Vector3 at = (eye.position + forward * (settings.SummonDistanceKm.Value * 1000f)).ToGlobalPosition().AsVector3();
            gen++;
            brain = new KaijuBrain(gen, at, Quaternion.LookRotation(-forward).eulerAngles.y, EnemyHq(), settings);
            if (brain.Body == null)
            {
                brain = null;
                return "Could not build the kaiju; see the log.";
            }
            Broadcast();
            return "KAIJU rising " + settings.SummonDistanceKm.Value.ToString("0") + " km ahead.";
        }

        public string Dismiss()
        {
            if (brain == null) return "No kaiju.";
            KaijuNet.Send(new KaijuState { Gen = gen, Action = KaijuNet.Gone });
            brain.Despawn();
            if (brain.Body != null) Destroy(brain.Body.gameObject);
            brain = null;
            return "Kaiju dismissed.";
        }

        private void Broadcast()
        {
            KaijuBody body = brain.Body;
            Vector3 p = body.Position.AsVector3(), aim = body.Aim.AsVector3();
            KaijuNet.Send(new KaijuState
            {
                Gen = gen, Action = (byte)body.Action, X = p.x, Y = p.y, Z = p.z, Yaw = body.Yaw,
                Elapsed = Time.time - body.ActionStart, AimX = aim.x, AimY = aim.y, AimZ = aim.z, HasAim = body.HasAim,
            });
        }

        /// <summary>Client side of the stream. The action clock is only reset when the action changes or restarts, so
        /// network jitter never restarts a clip.</summary>
        public void Receive(KaijuState s)
        {
            if (s.Action == KaijuNet.Gone)
            {
                if (remote != null && remote.Gen == s.Gen) Destroy(remote.gameObject);
                return;
            }
            var at = new GlobalPosition(s.X, s.Y, s.Z);
            if (remote == null || remote.Gen != s.Gen)
            {
                if (remote != null) Destroy(remote.gameObject);
                remote = KaijuBody.Create(s.Gen, at, s.Yaw);
                if (remote == null) return;
                remote.Smooth = true;
                lastAction = KaijuNet.Gone;
            }
            remote.Position = at;
            remote.Yaw = s.Yaw;
            remote.Aim = new GlobalPosition(s.AimX, s.AimY, s.AimZ);
            remote.HasAim = s.HasAim;
            if (s.Action != lastAction || s.Elapsed + 0.05f < lastElapsed)
            {
                remote.Action = (KaijuAction)s.Action;
                remote.ActionStart = Time.time - s.Elapsed;
            }
            lastAction = s.Action;
            lastElapsed = s.Elapsed;
        }

        /// <summary>Hardpoints belong to the side opposing the host's player, so they are always enemy-lockable in
        /// single player and co-op. The kaiju itself attacks every aircraft.</summary>
        private static FactionHQ EnemyHq()
        {
            GameManager.GetLocalHQ(out FactionHQ mine);
            FactionHQ first = null;
            foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
            {
                if (hq == null) continue;
                if (first == null) first = hq;
                if (hq != mine) return hq;
            }
            return first;
        }
    }
}
