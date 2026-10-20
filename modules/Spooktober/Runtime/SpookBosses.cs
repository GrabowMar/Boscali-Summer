using BepInEx.Logging;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Spooktober.Configuration;
using BoscaliSummer.Modules.Spooktober.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Spooktober.Runtime
{
    /// <summary>
    /// The secret menu's bosses: spawn one (host only, one at a time), dismiss it, and credit its defeat to this player's chain on every
    /// peer. The kaiju is reached through the <see cref="IKaijuBoss"/> service (Summon, Dismiss, Defeated), looked up on each use because
    /// the Kaiju module may be off; the fallen ace through <see cref="FallenAce"/>.
    /// </summary>
    internal sealed class SpookBosses : MonoBehaviour, ISceneService
    {
        private static ManualLogSource log;

        public static SpooktoberSettings Settings { get; private set; }

        private int creditedGen = -1;
        private IKaijuBoss watched;

        public void Configure(SpooktoberSettings spookSettings, ManualLogSource logger)
        {
            Settings = spookSettings;
            log = logger;
            FallenAce.Defeated += OnAceDefeated;
        }

        public void ResetForScene() => creditedGen = -1;

        private void OnDestroy()
        {
            if (watched != null) watched.Defeated -= OnKaijuDefeated;
            watched = null;
            FallenAce.Defeated -= OnAceDefeated;
            Settings = null;
        }

        /// <summary>The kaiju service when its module is on, else null.</summary>
        private static IKaijuBoss KaijuService => ModuleServices.TryGet(out IKaijuBoss boss) ? boss : null;

        /// <summary>Clients (and the host as a backstop) see the kill as the body's Death action; the host also gets the Defeated hook.</summary>
        private void Update()
        {
            IKaijuBoss boss = KaijuService;
            if (boss != watched) // the service appeared or went away: follow it
            {
                if (watched != null) watched.Defeated -= OnKaijuDefeated;
                if (boss != null) boss.Defeated += OnKaijuDefeated;
                watched = boss;
            }
            if (boss != null && boss.Dead) CreditKaiju(boss.Generation);
        }

        private void OnKaijuDefeated() => CreditKaiju(KaijuService?.Generation ?? -1);

        /// <summary>One kaiju death records once, however many of the hook and the watcher see it.</summary>
        private void CreditKaiju(int gen)
        {
            if (gen == creditedGen) return;
            creditedGen = gen;
            Credit(SpookBoss.Kaiju);
        }

        private void OnAceDefeated() => Credit(SpookBoss.FallenAce);

        /// <summary>A defeat counts for a player whose menu is unlocked, and only moves the chain one step (the kaiju even when F1 summoned it).</summary>
        private static void Credit(SpookBoss boss)
        {
            SpookProgress progress = SpookStore.Current;
            if (!progress.Unlocked || !progress.RecordDefeat(boss)) return;
            SpookStore.Save();
            log?.LogInfo("[Spooktober] " + boss + " defeated; the chain is at " + progress.ChainLevel + ".");
        }

        /// <summary>The boss on the field, if it is still alive (a kaiju that is dying is not).</summary>
        public static SpookBoss? Active
        {
            get
            {
                IKaijuBoss boss = KaijuService;
                if (boss != null && boss.Body != null && !boss.Dead) return SpookBoss.Kaiju;
                return FallenAce.Alive ? SpookBoss.FallenAce : (SpookBoss?)null;
            }
        }

        /// <summary>Spawns <paramref name="boss"/> ahead of the host's aircraft; the answer is the line for the menu. It first checks the
        /// new boss can come out, then whatever is on the field goes (a dying kaiju's body included), so there is never a second boss.
        /// It does not check the chain: the menu locks the cards, the sim hook may spawn any.</summary>
        public static string Spawn(SpookBoss boss)
        {
            if (!GameAccess.IsServer()) return "HOST ONLY";
            if (boss == SpookBoss.Redacted) return "[REDACTED]";
            IKaijuBoss kaiju = KaijuService;
            string refused = boss == SpookBoss.Kaiju ? (kaiju == null ? "KAIJU MODULE OFF" : null) : FallenAce.CannotSpawn();
            if (refused != null) return refused;
            Dismiss();
            return boss == SpookBoss.Kaiju ? kaiju.Summon() : FallenAce.Spawn(Settings);
        }

        /// <summary>Clears the kaiju (a dying one's body too) and the fallen ace, each on its own: neither is ever left behind.</summary>
        public static string Dismiss()
        {
            if (!GameAccess.IsServer()) return "HOST ONLY";
            IKaijuBoss kaiju = KaijuService;
            string gone = kaiju != null && kaiju.Body != null ? kaiju.Dismiss() : null;
            if (FallenAce.Alive) gone = gone == null ? FallenAce.Dismiss() : gone + " / " + FallenAce.Dismiss();
            return gone ?? "NO BOSS";
        }
    }
}
