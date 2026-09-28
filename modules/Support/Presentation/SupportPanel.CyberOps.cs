using System.Collections.Generic;
using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Cyber;
using BoscaliSummer.Features.Support.Presentation.Viz;
using BoscaliSummer.Features.Support.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// CYBER › ACTIONS — live map effects, grouped by one-use access lease. A completed breach
    /// opens a 75-second window at one site; the first accepted effect consumes it.
    /// ARM, then right-click the map.
    /// </summary>
    internal sealed partial class SupportPanel
    {
        private sealed class AbilityActionRow
        {
            public SupportActionDefinition Action;
            public AvRow Row;
            public AvControl Button;
        }

        private HintLine cyberOpsHint;
        private readonly List<AbilityActionRow> cyberAbilityRows = new List<AbilityActionRow>(12);

        private void ResetCyberOpsPage()
        {
            cyberOpsHint = null;
            cyberAbilityRows.Clear();
        }

        private void BuildCyberOpsPage(AvFlow actions)
        {
            var page = new List<SupportActionDefinition>(8);
            foreach (SupportActionDefinition action in support.Actions)
                if (HomeTab(action) == TabCyber) page.Add(action);
            page.Sort((a, b) => AbilityTier(a).CompareTo(AbilityTier(b)));

            actions.Section(AvIcon.Bolt, "MAP ABILITIES", "");
            cyberOpsHint = actions.Add(new HintLine(actions.Content));

            if (page.Count == 0)
            {
                actions.Add(new NoteText(actions.Content)).Set("This server resolves none of the catalogue.", AvState.Inert);
                return;
            }

            int lastTier = -1;
            foreach (SupportActionDefinition action in page)
            {
                if (AbilityTier(action) != lastTier)
                {
                    lastTier = AbilityTier(action);
                    actions.Section(AvIcon.ListDetails, AbilityGroup(lastTier));
                }
                SupportActionId id = action.Id;
                var row = new AbilityActionRow { Action = action, Row = actions.Add(new AvRow(actions.Content)) };
                row.Button = row.Row.AddTrailing(new AvControl.Spec("ARM", () =>
                {
                    support.Arm(id);
                    nextRefresh = 0f;
                }));
                cyberAbilityRows.Add(row);
            }
        }

        private void RefreshCyberOpsPage(bool bypass, CyberNetwork network, double now)
        {
            if (cyberOpsHint == null) return;
            int count = CountActions(TabCyber);
            string hint;
            AvState tone = AvState.Inert;
            if (network == null || !network.HasCommand) hint = "HOLD AN AIRBASE · THE NETWORK COMES UP ON IT BY ITSELF";
            else if (network.CommandCompromised) { hint = "C2 BREACHED · ABILITIES OFFLINE UNTIL PATCHED"; tone = AvState.Danger; }
            else
            {
                float access = network.AccessRemaining(now);
                hint = "INTEL " + Mathf.FloorToInt(network.Intel) + "/" + Mathf.RoundToInt(network.IntelCapacity()) +
                       (access > 0f ? " · ACCESS " + CyberWords.Seconds(access) + " · ONE USE" : " · NO ACCESS") + " · ARM MAP";
            }
            cyberOpsHint.Set(count + " ABILITIES · " + hint, tone);
            foreach (AbilityActionRow row in cyberAbilityRows) PaintAbilityRow(row, network, now, bypass);
        }

        private void PaintAbilityRow(AbilityActionRow row, CyberNetwork network, double now, bool bypass)
        {
            // One presenter for every surface (M5): cost and readiness are the words the other lists use.
            SupportActionDefinition action = row.Action;
            AbilityFacts facts = AbilityStatus.For(support, action, bypass);
            string where = network != null && network.HasCommand ? Coverage(action, network, now) : action.Description ?? "";
            string sub = where.Length > 0 ? facts.Readiness + " — " + where : facts.Readiness;
            row.Row.Set(action.Name, sub, facts.CostText, ToState(facts.Tone));
            row.Button.Interactable = facts.Enabled;
            row.Button.Latched = facts.Armed;
            row.Button.Label = facts.Armed ? "ABORT" : "ARM";
        }

        /// <summary>How many locations can run this ability, and how far their radius reaches.
        /// The page never claims a coverage the host would refuse.</summary>
        private string Coverage(SupportActionDefinition action, CyberNetwork network, double now)
        {
            if (network == null || !network.HasCommand) return "NO NETWORK YET · IT COMES UP ON YOUR CENTRAL AIRBASE";
            if (!action.Hack.HasValue && !action.Cap.HasValue) return "ANYWHERE ON THE MAP · PAID IN ALLOCATION";
            float lease = network.AccessRemaining(now);
            if (lease <= 0f) return action.IsCapstone
                ? "NO LIVE PAYLOAD · BREACH A SITE, THEN SELECT A PAYLOAD"
                : "NO LIVE ACCESS · BREACH A REAL SITE TO OPEN ONE 75S EFFECT WINDOW";
            if (action.Cap.HasValue)
            {
                int armed = network.CapstoneCount(action.Cap.Value);
                return armed > 0
                    ? "PAYLOAD SELECTED · " + CyberWords.Seconds(lease) + " LEFT · ONE USE"
                    : "PICK THIS PAYLOAD IN THE CONSOLE · ACCESS " + CyberWords.Seconds(lease);
            }
            int tier = CyberLocations.Tier(CyberCatalog.RequiredStage(action.Hack.Value));
            int count = 0;
            float best = 0f;
            for (int i = 0; i < CyberNetwork.SlotCount; i++)
            {
                if (!network.Working(i) || !network.IsHacked(i) || network.Tier(i) < tier) continue;
                count++;
                best = Mathf.Max(best, network.RadiusOf(i, now));
            }
            return count == 0
                ? "SITE OFFLINE · ACCESS EXPIRED OR DEFENDER ISOLATED IT"
                : "LEASE " + CyberWords.Seconds(lease) + " · ONE USE · RADIUS " + AvNum.Fixed(best / 1000f, 0) + " KM";
        }

        /// <summary>Stage that unlocks the row; support rows sort last.</summary>
        private static int AbilityTier(SupportActionDefinition action) =>
            action.Hack.HasValue ? 2
            : action.Cap.HasValue ? 3
            : 4;

        private static string AbilityGroup(int tier)
        {
            switch (tier)
            {
                case 2: return "LIVE LEASE · BASIC EFFECTS · ONE USE";
                case 3: return "OPTIONAL PAYLOAD · ONE USE";
                default: return "BASE SUPPORT · PAID IN ALLOCATION";
            }
        }
    }
}
