using System.Collections.Generic;
using BoscaliSummer.Features.DynamicOperations.Domain;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NuclearOption.Networking;
using UnityEngine;
namespace BoscaliSummer.Features.DynamicOperations.Runtime
{
    /// <summary>
    /// Accepted contracts on the common HUD element: one line each, the contracted title above
    /// the distance and clock, a thin vanilla-style bar under the text, and a notice the moment
    /// the pilot enters or leaves the objective area. Colours come from the live vanilla theme.
    /// A contract whose contact the host lost stays listed as CONTACT LOST, because that is the
    /// state the pilot most needs to see.
    ///
    /// <para>Lines are held per slot rather than per contract, so a mission that issues a hundred
    /// contracts still costs three lines. The board's own visibility rule keeps the element off
    /// the screen while the map is open or the pilot is not flying.</para>
    /// </summary>
    internal sealed class OperationZoneHud : MonoBehaviour, ISceneService
    {
        private const string Owner = "dynamic-operations";
        private const string Channel = "contracts";

        private const int MaxRows = OperationBoard.MaximumCards;
        private const float ContentSeconds = 0.25f;
        private const float ServerRefreshSeconds = 2f;

        private OperationsManager manager;
        private IHudBoard board;
        private readonly IHudLine[] lines = new IHudLine[MaxRows];
        private readonly int[] shownIds = new int[MaxRows];
        private readonly bool[] inside = new bool[MaxRows];
        private readonly ContractCard[] cards = new ContractCard[MaxRows];
        private readonly ContractCard[] selected = new ContractCard[MaxRows];
        private readonly float[] distances = new float[MaxRows];
        private float nextContent, nextServer;
        private bool wanted;

        internal void Configure(OperationsManager owner)
        {
            manager = owner;
            Clear();
        }

        public void ResetForScene()
        {
            ReleaseLines();
            Clear();
            nextContent = 0f;
            nextServer = 0f;
            wanted = false;
            VanillaHudStyle.Invalidate();
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (manager == null)
            {
                if (wanted) ResetForScene();
                return;
            }
            if (Time.unscaledTime >= nextContent)
            {
                nextContent = Time.unscaledTime + ContentSeconds;
                Refresh();
            }
            if (wanted && Time.unscaledTime >= nextServer)
            {
                nextServer = Time.unscaledTime + ServerRefreshSeconds;
                manager.Refresh();
            }
        }

        private void Refresh()
        {
            if (!GameManager.GetLocalAircraft(out Aircraft aircraft) || aircraft == null ||
                aircraft.disabled || aircraft.HasEjected())
            {
                ReleaseLines();
                wanted = false;
                return;
            }

            IReadOnlyList<SecondaryObjectiveView> views = manager.Objectives;
            int count = 0;
            if (views != null)
                for (int i = 0; i < views.Count && count < cards.Length; i++)
                    if (ContractCard.TryRead(views[i], out ContractCard card)) cards[count++] = card;
            if (count == 0)
            {
                ReleaseLines();
                wanted = false;
                return;
            }

            if (!ModServices.TryGet(out board)) return;
            board.DeclareChannel(Channel, "CONTRACTS");

            var self = aircraft.transform.position.ToGlobalPosition().AsVector3();
            int shown = ContractSelection.Select(cards, count, self.x, self.z, selected, distances, MaxRows);

            bool metric = VanillaHudStyle.Metric;

            for (int row = 0; row < shown; row++)
            {
                ContractCard card = selected[row];
                float distance = distances[row];
                bool isInside = card.HasMarker && card.Inside(distance);
                Watch(row, card, isInside, distance);
                Write(row, card, distance, isInside, metric);
            }
            for (int row = shown; row < MaxRows; row++)
                if (lines[row] != null) lines[row].Release();

            wanted = shown > 0;
        }

        private void Write(int row, in ContractCard card, float distance, bool isInside, bool metric)
        {
            if (lines[row] == null)
            {
                lines[row] = board.Acquire(Owner, Channel, "card" + row);
                if (lines[row] == null) return;
            }

            if (!card.HasMarker)
            {
                lines[row].Set(HudTone.Warning, card.TitleLine, "CONTACT LOST", 0f);
                return;
            }

            string clock = OperationMarkerCopy.Clock(card.Seconds);
            string detail = OperationMarkerCopy.Distance(distance, metric);
            if (!string.IsNullOrEmpty(clock)) detail += "  ·  " + clock;

            lines[row].Set(
                card.Tone == MarkerTone.Caution ? HudTone.Caution : HudTone.Info,
                card.TitleLine, detail,
                OperationMarkerCopy.Bar(distance, card.Radius, isInside, card.Progress));
        }

        /// <summary>
        /// Announce the area boundary once per crossing. The notice is the pilot's cue; the line
        /// above already carries the standing state, so nothing is said twice on the same line.
        /// </summary>
        private void Watch(int row, in ContractCard card, bool isInside, float distance)
        {
            if (shownIds[row] != card.Id)
            {
                shownIds[row] = card.Id;
                inside[row] = isInside;
                return;
            }

            HudTone tone = card.Tone == MarkerTone.Caution ? HudTone.Caution : HudTone.Info;
            if (isInside && !inside[row])
            {
                inside[row] = true;
                board.Notice(Channel, tone, "ENTERING AREA  ·  " + card.TitleLine);
            }
            else if (!isInside && inside[row] && card.Radius > 0f && distance > card.Radius * 1.1f)
            {
                inside[row] = false;
                board.Notice(Channel, HudTone.Info, "LEAVING AREA  ·  " + card.TitleLine);
            }
        }

        private void ReleaseLines()
        {
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i] == null) continue;
                lines[i].Release();
                lines[i] = null;
            }
        }

        private void Clear()
        {
            for (int i = 0; i < MaxRows; i++)
            {
                shownIds[i] = 0;
                inside[i] = false;
                cards[i] = default;
                selected[i] = default;
                distances[i] = 0f;
            }
        }
    }
}
