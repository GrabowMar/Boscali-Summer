namespace BoscaliSummer.Modules.Spooktober.Domain
{
    /// <summary>How the fallen ace is flying: hunting the target, diving on it, circling it unseen, or dodging a missile.</summary>
    internal enum AceMood : byte { Hunt, Strike, Stalk, Feint }

    /// <summary>The ace's tuning knobs (metres, seconds, hull fractions). A later task builds one from config.</summary>
    internal struct AceTuning
    {
        /// <summary>Lethal hits it shrugs off, plus the one that is real.</summary>
        public int Lives;
        /// <summary>A missile launched farther than this from its target is a phantom and veers off.</summary>
        public float PhantomRange;
        /// <summary>Hull fraction lost within <see cref="BlinkWindow"/> that makes it blink away.</summary>
        public float BlinkHullLoss, BlinkWindow, BlinkCooldown;
        /// <summary>Distance range the blink lands in.</summary>
        public float BlinkMin, BlinkMax;
        public float MoodMin, MoodMax, StalkMin, StalkMax;
        public float DamageScale;
        public float RepairPerSecond, RepairDelay;

        public static AceTuning Default => new AceTuning
        {
            Lives = 3,
            PhantomRange = 3000f,
            BlinkHullLoss = 0.15f,
            BlinkWindow = 2f,
            BlinkCooldown = 20f,
            BlinkMin = 2000f,
            BlinkMax = 4000f,
            MoodMin = 10f,
            MoodMax = 30f,
            StalkMin = 8f,
            StalkMax = 12f,
            DamageScale = 0.35f,
            RepairPerSecond = 0.01f,
            RepairDelay = 6f,
        };
    }

    /// <summary>What the host sees this tick. <see cref="Now"/> is the clock the mood timers run on.</summary>
    internal struct AceSense
    {
        public float Now, TargetDistance, HullLostInWindow, SinceHit;
        public bool HasTarget, MissileInbound;
    }

    /// <summary>
    /// The fallen ace's decisions: its mood, its lives and when it blinks, veers a missile or repairs.
    /// Pure (pinned by tests/BoscaliSummer.Tests/Features/Spooktober); the host-side director feeds it senses and acts on the answers.
    /// Defeat is only <see cref="AbsorbLethal"/> returning false; a despawn or a disconnect never costs a life.
    /// </summary>
    internal sealed class AceRules
    {
        private const float FeintSeconds = 3f;
        private const float StrikeRange = 2500f;

        private bool started;
        private float moodFor;
        private float blinkReadyAt = float.NegativeInfinity;

        public AceTuning Tuning { get; }
        public int Lives { get; private set; }
        public AceMood Mood { get; private set; }
        /// <summary>The <see cref="AceSense.Now"/> the current mood began at.</summary>
        public float MoodSince { get; private set; }

        public AceRules(AceTuning t)
        {
            if (t.Lives < 1) t.Lives = 1;
            Tuning = t;
            Lives = t.Lives;
        }

        /// <summary>
        /// Advance the mood machine and return the mood. Each mood's length is drawn from <paramref name="roll01"/> (0..1) when it
        /// begins; the same roll picks the next mood when it ends. A stalk ending (back to hunt) is where the runtime blinks it behind the target.
        /// </summary>
        public AceMood Tick(AceSense s, float roll01)
        {
            if (!started)
            {
                started = true;
                Enter(AceMood.Hunt, s.Now, roll01);
            }
            if (!s.HasTarget)
            {
                if (Mood != AceMood.Hunt) Enter(AceMood.Hunt, s.Now, roll01);
                return Mood;
            }
            if (s.MissileInbound && Mood != AceMood.Feint)
            {
                Enter(AceMood.Feint, s.Now, roll01);
                return Mood;
            }
            if (s.Now - MoodSince < moodFor) return Mood;

            switch (Mood)
            {
                case AceMood.Hunt:
                    // a far hunt that rolls low stays a hunt, on a fresh timer
                    Enter(s.TargetDistance < StrikeRange ? AceMood.Strike : roll01 > 0.5f ? AceMood.Stalk : AceMood.Hunt, s.Now, roll01);
                    break;
                case AceMood.Strike:
                    Enter(roll01 > 0.6f ? AceMood.Stalk : AceMood.Hunt, s.Now, roll01);
                    break;
                default: // Feint and Stalk both end in a hunt
                    Enter(AceMood.Hunt, s.Now, roll01);
                    break;
            }
            return Mood;
        }

        private void Enter(AceMood mood, float now, float roll01)
        {
            Mood = mood;
            MoodSince = now;
            switch (mood)
            {
                case AceMood.Feint: moodFor = FeintSeconds; break;
                case AceMood.Stalk: moodFor = Tuning.StalkMin + roll01 * (Tuning.StalkMax - Tuning.StalkMin); break;
                default: moodFor = Tuning.MoodMin + roll01 * (Tuning.MoodMax - Tuning.MoodMin); break;
            }
        }

        /// <summary>True once the hull took a blink's worth of damage in the window and the cooldown is over; saying yes starts the cooldown.</summary>
        public bool WantsBlink(AceSense s)
        {
            if (s.HullLostInWindow < Tuning.BlinkHullLoss || s.Now < blinkReadyAt) return false;
            blinkReadyAt = s.Now + Tuning.BlinkCooldown;
            return true;
        }

        /// <summary>A lethal hit with lives to spare is a rebirth (true); the last life is the real death (false).</summary>
        public bool AbsorbLethal()
        {
            if (Lives <= 1) return false;
            Lives--;
            return true;
        }

        public static bool PhantomVeers(float launchDistance, float range) => launchDistance > range;

        /// <summary>Hull fraction to give back this step: nothing until it has gone <c>RepairDelay</c> seconds unhit.</summary>
        public float RepairNow(AceSense s, float dt) => s.SinceHit >= Tuning.RepairDelay ? Tuning.RepairPerSecond * dt : 0f;

        /// <summary>The HUD line: "FALLEN ACE", a middle dot, then a full block per life left and a light shade per life spent
        /// (the kit and HUD fonts have no diamonds).</summary>
        public string LivesLine() =>
            "FALLEN ACE \u00b7 " + new string('\u2588', Lives) + new string('\u2591', Tuning.Lives - Lives);
    }
}
