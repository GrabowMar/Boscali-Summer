using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.DynamicOperations.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.DynamicOperations.Runtime
{
    /// <summary>
    /// DynamicOperations' provider of the OBJECTIVE page of the TAC BOARD (<see cref="ObjectiveScope"/>): the pilot's selected accepted contract as the zone HUD
    /// already picks it (<see cref="ContractSelection"/>: the selected card, marked, within its vicinity), the host's phases, blocker and payout from the same
    /// snapshot every peer holds, the faction's known air defence from <see cref="IThreatPicture"/> (asked at 1 Hz, only while a contract is up: asking is what
    /// makes a client build its picture) and the local aircraft's position, heading and radar height. Answers from cache, once per frame, allocation-free in steady
    /// state. Host and client alike: the snapshot is the host's, sent to everyone.
    /// </summary>
    internal sealed class ObjectiveScopeFeed
    {
        private const float RingEvery = 1f, MultiplierEvery = 2f;

        private readonly OperationsManager manager;
        private readonly ContractCard[] cards = new ContractCard[OperationBoard.MaximumCards];
        private readonly ContractCard[] selected = new ContractCard[1];
        private readonly float[] distances = new float[1];
        private readonly AirDefenceRing[] all = new AirDefenceRing[ThreatPictureLimits.MaximumRings], rings = new AirDefenceRing[ObjectiveScopeRules.MaxRings];
        private readonly float[] rank = new float[ObjectiveScopeRules.MaxRings];
        private ObjectiveScope cache;
        private bool live;
        private int frame = -1, ringCount;
        private float nextRings, nextMultiplier, multiplier;
        private IThreatPicture picture;
        private IFactionMoraleView morale;

        public ObjectiveScopeFeed(OperationsManager manager) { this.manager = manager; }

        public void Register() => ScopeFeeds.Register<ObjectiveScope>(Read);

        public bool Read(out ObjectiveScope data)
        {
            if (frame != Time.frameCount)
            {
                frame = Time.frameCount;
                live = Build();
            }
            data = cache;
            return live;
        }

        private bool Build()
        {
            if (!manager.IsFresh || manager.SelectedForHud < 0) return false;
            if (!GameManager.GetLocalAircraft(out Aircraft me) || me == null || me.disabled || me.HasEjected()) return false;
            IReadOnlyList<SecondaryObjectiveView> views = manager.Objectives;
            int count = 0;
            for (int i = 0; views != null && i < views.Count && count < cards.Length; i++)
                if (ContractCard.TryRead(views[i], out ContractCard card)) cards[count++] = card;
            if (count == 0) return false;

            GlobalPosition at = me.transform.position.ToGlobalPosition();
            if (ContractSelection.Select(cards, count, at.x, at.z, selected, distances, 1, manager.SelectedForHud) == 0) return false;
            ContractCard c = selected[0];
            SecondaryObjectiveView view = null;
            for (int i = 0; i < views.Count; i++) if (views[i].Id == c.Id) view = views[i];
            if (view == null) return false;

            ObjectiveTasking tasking = view.Tasking;
            Vector3 forward = me.transform.forward;
            // A selected contract whose contact is lost has no place (HasMarker false): it stays, as a plate, so the pilot reads CONTACT LOST and the host's blocker instead of the page vanishing.
            bool marker = c.HasMarker;
            float agl = me.radarAlt, distance = marker ? ContractMarkerMath.Distance(at.x, at.z, c.X, c.Z) : 0f;
            ObjectivePhase[] phases = tasking != null ? tasking.Phases : null;
            int current = tasking != null ? tasking.CurrentPhase : -1;
            ObjectiveContact contact = tasking != null ? tasking.Contact : ObjectiveContact.Fixed;
            string blocker = tasking != null ? tasking.Blocker : "";
            bool gated = marker && phases != null && current >= 0 && current < phases.Length && ObjectiveScopeRules.GatedPhase(phases[current].Title);
            bool range = false, alt = false, los = false, track = false;
            if (marker)
            {
                ObjectiveScopeRules.Gates(distance, c.Radius, agl, contact, blocker, out range, out alt, out los, out track);
                Rings(me, c, at);
            }
            else ringCount = 0;
            cache = new ObjectiveScope
            {
                Id = c.Id, Title = c.Title, Target = view.Target, Contact = contact, HasMarker = marker,
                X = c.X, Z = c.Z, Radius = c.Radius, OwnX = at.x, OwnZ = at.z, OwnHeading = ObjectiveScopeRules.Heading(forward.x, forward.z), OwnAgl = agl,
                Left = c.Seconds, Total = Math.Max(c.Seconds, ObjectiveScopeRules.DeadlineSeconds(view.Title)), Money = view.Money, Xp = view.Xp, Multiplier = Multiplier(me),
                Phases = phases, PhaseCount = phases != null ? phases.Length : 0, Current = current,
                NextAction = c.NextAction, Blocker = blocker,
                HasGates = gated, GateRange = range, GateAlt = alt, GateLos = los, GateTrack = track,
                RingCount = ringCount, Rings = rings,
            };
            return true;
        }

        /// <summary>The faction's known air defence near the zone and the pilot, refreshed once a second.</summary>
        private void Rings(Aircraft me, in ContractCard c, GlobalPosition at)
        {
            float now = Time.unscaledTime;
            if (now < nextRings) return;
            nextRings = now + RingEvery;
            ringCount = 0;
            if (picture == null) ModuleServices.TryGet(out picture);
            FactionHQ hq = me.NetworkHQ;
            if (picture == null || hq == null) return;
            int observer = hq.GetInstanceID();
            if (!picture.IsReady(observer)) return;
            int n = picture.CopyAirDefence(observer, all);
            ringCount = ObjectiveScopeRules.PickRings(all, n, c.X, c.Z, at.x, at.z, rings, rank);
        }

        /// <summary>The world's contract multiplier for the pilot's faction (0 when the mood view is not there).</summary>
        private float Multiplier(Aircraft me)
        {
            float now = Time.unscaledTime;
            if (now < nextMultiplier) return multiplier;
            nextMultiplier = now + MultiplierEvery;
            if (morale == null) ModuleServices.TryGet(out morale);
            FactionHQ hq = me.NetworkHQ;
            multiplier = morale != null && hq != null && morale.TryGetContractMultiplier(hq.GetInstanceID(), out float m) ? m : 0f;
            return multiplier;
        }
    }
}
