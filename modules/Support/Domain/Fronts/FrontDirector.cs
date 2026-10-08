using System;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Modules.Support.Domain.Fronts
{
    /// <summary>
    /// How a front's directive (and focus pin) bends the always-on director of that front (spec 3.4). The watch brains keep their own rules and weights; this
    /// only supplies the numbers they scale by. A default value is the neutral posture. Pure.
    /// </summary>
    internal readonly struct DirectorBias
    {
        public const float FocusMeters = 15000f, FocusBoost = 1.5f;

        public readonly FrontDirective Directive;
        public readonly bool HasFocus;
        public readonly float FocusX, FocusZ;

        public DirectorBias(FrontDirective directive, bool hasFocus, float focusX, float focusZ)
        { Directive = directive; HasFocus = hasFocus && float.IsFinite(focusX) && float.IsFinite(focusZ); FocusX = focusX; FocusZ = focusZ; }

        public static DirectorBias Neutral(Front front) => new DirectorBias(FrontRules.DefaultDirective(front), false, 0f, 0f);

        /// <summary>The target lies within 15 km of the focus pin.</summary>
        public bool Near(float x, float z)
        {
            if (!HasFocus) return false;
            float dx = x - FocusX, dz = z - FocusZ;
            return dx * dx + dz * dz <= FocusMeters * FocusMeters;
        }

        /// <summary>Score multiplier: 1.5 near the pin, 1 elsewhere.</summary>
        public float Focus(float x, float z) => Near(x, z) ? FocusBoost : 1f;

        // ---- SPACE: RECON / STRIKE / DEFEND -------------------------------------------------------------

        /// <summary>RECON keeps the neutral rule (a look after 90 s of silence), STRIKE and DEFEND wait longer (x1.5, x2): the silence before the officer asks the bird for a look.</summary>
        public float NoContactScale => Directive == FrontDirective.Strike ? 1.5f : Directive == FrontDirective.Defend ? 2f : 1f;

        /// <summary>Posts the officer may keep on the board at once: STRIKE 3, DEFEND 1, RECON 2.</summary>
        public int MaxPosts => Directive == FrontDirective.Strike ? 3 : Directive == FrontDirective.Defend ? 1 : 2;

        /// <summary>STRIKE keeps the kinetic bird in view for twice as long before a rod is ready (x2 on the window).</summary>
        public float RodWindowScale => Directive == FrontDirective.Strike ? 2f : 1f;

        /// <summary>STRIKE prefers high-value air defence and armour (x1.25 on their score).</summary>
        public float HighValue(WatchKind kind) => Directive == FrontDirective.Strike && kind != WatchKind.Other ? 1.25f : 1f;

        // ---- CYBER: DEFEND / BALANCED / ATTACK ----------------------------------------------------------

        /// <summary>The rest after a burn or drop: DEFEND x2, ATTACK x0.5.</summary>
        public float RestScale => Directive == FrontDirective.Defend ? 2f : Directive == FrontDirective.Attack ? 0.5f : 1f;

        /// <summary>Nodes the officer holds at once: DEFEND keeps one, the others two.</summary>
        public int MaxHeld => Directive == FrontDirective.Defend ? 1 : 2;

        /// <summary>ATTACK and BALANCED hunt SAM C2 over RADAR over UPLINK over RELAY over DATA CENTER; DEFEND turns it round to UPLINK, RELAY, DATA CENTER first (cut the enemy's reach).</summary>
        public int NodePriority(NodeKind kind)
        {
            if (Directive == FrontDirective.Defend)
            {
                switch (kind)
                {
                    case NodeKind.Uplink: return 5;
                    case NodeKind.Relay: return 4;
                    case NodeKind.DataCenter: return 3;
                    case NodeKind.Radar: return 2;
                    default: return 1;
                }
            }
            switch (kind)
            {
                case NodeKind.SamC2: return 5;
                case NodeKind.Radar: return 4;
                case NodeKind.Uplink: return 3;
                case NodeKind.Relay: return 2;
                default: return 1;
            }
        }

        // ---- SOF: RECON / SABOTAGE / HOLD ---------------------------------------------------------------

        /// <summary>The odds a sabotage needs: SABOTAGE 45, HOLD 65, RECON the neutral 55.</summary>
        public float MinSabotageOdds => Directive == FrontDirective.Sabotage ? 45f : Directive == FrontDirective.Hold ? 65f : 55f;

        /// <summary>The gap between sabotage missions: SABOTAGE x0.5, HOLD x1.5, RECON the neutral x1.</summary>
        public float SabotageCooldownScale => Directive == FrontDirective.Sabotage ? 0.5f : Directive == FrontDirective.Hold ? 1.5f : 1f;

        /// <summary>HOLD keeps the teams close: contacts count only within half the usual distance of the front.</summary>
        public float NearFrontScale => Directive == FrontDirective.Hold ? 0.5f : 1f;

        /// <summary>SABOTAGE sends the team to targets, never to stand off and look.</summary>
        public bool AllowRecon => Directive != FrontDirective.Sabotage;

        /// <summary>Odds-equivalent bonus a target near the pin gets when sabotage targets are ranked (the threshold itself is not bent).</summary>
        public int OddsBonus(float x, float z) => Near(x, z) ? 10 : 0;
    }

    /// <summary>The counter triangle (spec 3.3, R8): a front that leads by 40 or more presses the front it beats in the enemy. Pure multipliers.</summary>
    internal readonly struct CounterPressure
    {
        public readonly float SofExposure, SpaceCooldown, CyberTrace;
        public CounterPressure(float sofExposure, float spaceCooldown, float cyberTrace) { SofExposure = sofExposure; SpaceCooldown = spaceCooldown; CyberTrace = cyberTrace; }
        public static readonly CounterPressure None = new CounterPressure(1f, 1f, 1f);
        public bool Any => SofExposure > 1f || SpaceCooldown > 1f || CyberTrace > 1f;
    }

    internal static class FrontCounters
    {
        public const float SofExposureFactor = 1.5f, SpaceCooldownFactor = 1.5f, CyberTraceFactor = 1.3f;

        /// <summary>The pressure on one faction when an enemy's SPACE / CYBER / SOF front leads by 40 or more: SPACE beats SOF, CYBER beats SPACE, SOF beats CYBER.</summary>
        public static CounterPressure Against(bool enemySpaceLeads, bool enemyCyberLeads, bool enemySofLeads) =>
            new CounterPressure(enemySpaceLeads ? SofExposureFactor : 1f, enemyCyberLeads ? SpaceCooldownFactor : 1f, enemySofLeads ? CyberTraceFactor : 1f);
    }

    /// <summary>What a faction with no humans queues by itself when its front has nothing building (the AI faction raises its fronts without a pilot).</summary>
    internal static class FrontAi
    {
        public static ProgrammeId? Next(Front front, int readiness, in FrontWorld w, bool samKnown)
        {
            switch (front)
            {
                case Front.Cyber:
                    if (samKnown && readiness >= 3) return ProgrammeId.ZeroDay;
                    break;
                case Front.Sof:
                    if (w.Teams < 2) return ProgrammeId.TrainTeam;
                    if (w.HasHeldBuilding && readiness >= 3) return ProgrammeId.Fob;
                    break;
            }
            return readiness < FrontRules.MaxReadiness ? ProgrammeId.Readiness : (ProgrammeId?)null;
        }
    }
}
