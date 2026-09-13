using System.Collections.Generic;
using BoscaliSummer.Features.DynamicOperations.Domain;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using NuclearOption.MissionEditorScripts;
using NuclearOption.Networking;
using NuclearOption.SavedMission;
using UnityEngine;

namespace BoscaliSummer.Features.DynamicOperations.Runtime
{
    /// <summary>
    /// A vanilla-shaped objective that exists only so the native map marker and cockpit
    /// overlay can render it. Never registered with MissionRunner.activeByFaction: vanilla
    /// AI reads that dictionary (ShipAI, AIPilotCombatModes, GroundVehicle, ...), so a
    /// synthetic entry there would become a flight-plan waypoint.
    /// </summary>
    internal sealed class OperationObjective : Objective, IObjectiveWithPosition
    {
        private readonly List<ObjectivePosition> positions = new List<ObjectivePosition>(1);
        private float progress;

        public OperationObjective(SavedObjective saved) : base(saved) { }

        public override float CompletePercent => progress;

        public IReadOnlyList<ObjectivePosition> Positions => positions;

        public void Set(float x, float z, float radius, float progress)
        {
            this.progress = Mathf.Clamp01(Operation.Finite(progress) ? progress : 0f);
            if (!Operation.Finite(x) || !Operation.Finite(z)) return;
            var position = new ObjectivePosition(new GlobalPosition(x, 0f, z), radius > 0f ? radius : (float?)null);
            if (positions.Count == 0) positions.Add(position);
            else positions[0] = position;
        }

        public override void OnStart() { }
        public override bool UpdateAndCheck() => false;
        public override void ClientOnlyUpdate() { }
        public override void DrawData(DataDrawer drawer) { }
        protected override void DataReferenceDestroyed(ISaveableReference reference) { }
    }

    /// <summary>
    /// Feeds accepted contracts into the native objective UI path — MissionPosition.
    /// GetAllPositionsResults (map markers, cockpit pointer, distance and area ring).
    /// That method is UI-only; every AI consumer goes through TryGetClosest*/DistanceTo,
    /// which never sees these instances.
    /// </summary>
    internal sealed class OperationMarkerBridge : MonoBehaviour, ISceneService
    {
        private const int MaxMarkers = OperationBoard.MaximumCards;
        private const float RefreshSeconds = 1.5f;
        private readonly OperationObjective[] slots = new OperationObjective[MaxMarkers];
        private readonly int[] slotIds = new int[MaxMarkers];
        private OperationsManager manager;
        private float nextRefresh;

        internal static OperationMarkerBridge Active { get; private set; }

        internal void Configure(OperationsManager owner)
        {
            manager = owner;
            Active = this;
        }

        private void OnDestroy()
        {
            if (Active == this) Active = null;
            ResetForScene();
        }

        public void ResetForScene()
        {
            for (int i = 0; i < slots.Length; i++) { slots[i] = null; slotIds[i] = 0; }
            nextRefresh = 0f;
        }

        internal static void Append(FactionHQ factionHQ, GlobalPosition from, List<MissionPosition.PositionResult> results)
        {
            OperationMarkerBridge bridge = Active;
            if (bridge == null || bridge.manager == null || results == null || factionHQ == null) return;
            if (!GameManager.GetLocalPlayer<Player>(out Player local) || local == null || local.HQ != factionHQ) return;
            // The objective UI queries on its own cadence; ask the host for fresh state at a
            // bounded rate instead of allocating a snapshot per render frame.
            float now = Time.unscaledTime;
            if (now >= bridge.nextRefresh) { bridge.nextRefresh = now + RefreshSeconds; bridge.manager.Refresh(); }
            IReadOnlyList<SecondaryObjectiveView> cards = bridge.manager.Objectives;
            if (cards == null || cards.Count == 0) return;
            bridge.Sync(factionHQ, cards);
            for (int i = 0; i < bridge.slots.Length; i++)
                if (bridge.slots[i] != null && MissionPosition.DistanceTo(bridge.slots[i], from, out MissionPosition.PositionResult result))
                    results.Add(result);
        }

        private void Sync(FactionHQ factionHQ, IReadOnlyList<SecondaryObjectiveView> cards)
        {
            for (int i = 0; i < slotIds.Length; i++)
                if (slotIds[i] != 0 && !IsMarked(cards, slotIds[i])) { slots[i] = null; slotIds[i] = 0; }
            for (int c = 0; c < cards.Count; c++)
            {
                SecondaryObjectiveView card = cards[c];
                if (card == null || !card.IsActive || !card.HasMarker) continue;
                int slot = IndexOf(card.Id);
                if (slot < 0)
                {
                    slot = FreeSlot();
                    slots[slot] = Create(factionHQ, card);
                    slotIds[slot] = card.Id;
                }
                slots[slot].Set(card.X, card.Z, card.Radius, card.Progress);
            }
        }

        private static bool IsMarked(IReadOnlyList<SecondaryObjectiveView> cards, int id)
        {
            for (int i = 0; i < cards.Count; i++)
            {
                SecondaryObjectiveView card = cards[i];
                if (card != null && card.Id == id) return card.IsActive && card.HasMarker;
            }
            return false;
        }

        private int IndexOf(int id)
        {
            for (int i = 0; i < slotIds.Length; i++) if (slotIds[i] == id) return i;
            return -1;
        }

        private int FreeSlot()
        {
            for (int i = 0; i < slotIds.Length; i++) if (slotIds[i] == 0) return i;
            return 0;
        }

        private static OperationObjective Create(FactionHQ factionHQ, SecondaryObjectiveView card)
        {
            SavedObjective saved = SavedObjective.CreateSavedObjective(IconFor(card.Title), "BS-OPS-" + card.Id);
            saved.DisplayName = "#" + card.Id + " " + card.Title;
            saved.Hidden = false;
            if (factionHQ.faction != null) saved.Faction = factionHQ.faction.factionName;
            return new OperationObjective(saved);
        }

        /// <summary>Vanilla marker sprites have four meanings; contracts borrow the closest one.</summary>
        private static ObjectiveType IconFor(string title)
        {
            if (!OperationTitles.TryKind(title, out OperationKind kind)) return ObjectiveType.DestroyUnits;
            switch (kind)
            {
                case OperationKind.Capture: return ObjectiveType.CaptureAirbase;
                case OperationKind.Defend:
                case OperationKind.DamageAssessment:
                case OperationKind.Rappel:
                case OperationKind.Rooftop:
                    return ObjectiveType.ReachWaypoints;
                case OperationKind.Recon:
                case OperationKind.SortieReport:
                case OperationKind.BattlefieldSurvey:
                case OperationKind.SupplyEscort:
                case OperationKind.RepairCover:
                case OperationKind.Patrol:
                    return ObjectiveType.SpotUnit;
                default: return ObjectiveType.DestroyUnits;
            }
        }
    }
}
