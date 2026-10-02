using NOAvionics;
using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Orbital;
using BoscaliSummer.Modules.Support.Domain.SpecOps;
using BoscaliSummer.Modules.Support.Presentation.Viz;
using BoscaliSummer.Modules.Support.Presentation.Window;
using BoscaliSummer.Modules.Support.Runtime;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Views
{
    internal sealed partial class CrewOperationsView
    {
        private readonly Key[] fitting = new Key[3];
        private readonly AvStepProgress[] fittingBars = new AvStepProgress[3];
        private Key serviceCargo;
        private TMP_Text serviceLedger, serviceNotice;

        private void BuildSystems(RectTransform root, float w, float h)
        {
            float lw = w * .36f, sx = lw + 14, sw = w - sx;
            Instrument(root, 0, 0, lw, h, Domain == OpsDomain.Space ? "BASTION / EQUIPMENT REGISTER" : Domain == OpsDomain.Cyber ? "AEGIS / NETWORK HARDENING" : "DETACHMENT / FORWARD ASSETS", Domain == OpsDomain.Space ? AvIcon.Satellite : Domain == OpsDomain.Cyber ? AvIcon.ShieldLock : AvIcon.UsersGroup);
            if (Domain == OpsDomain.Space)
            {
                OpsArtwork.Draw(root, new Rect(16, -46, lw - 32, 270), 0);
                Mono(root, "ASSEMBLY REFERENCE / FITTED HARDWARE BELOW", 18, 319, lw - 36, 24, 15, BranchDim);
                serviceLedger = Mono(root, "", 18, 361, lw - 36, h - 378, 16, BranchInk);
                Instrument(root, sx, 0, sw, 271, "MISSION FIT / NATIVE LAUNCH QUEUE", AvIcon.Stack2);
                float cw = (sw - 40) / 3;
                for (int i = 0; i < 3; i++)
                {
                    int mission = i; float cx = sx + 12 + i * (cw + 8);
                    Text(root, PlatformMissions.Name((PlatformMission)i), cx, 46, cw, 27, 19, BranchInk);
                    fittingBars[i] = new AvStepProgress(root, "LoadoutCompletion");
                    Chrome.Place(fittingBars[i].Rect, new Rect(cx, -83, cw, 7)); fittingBars[i].Paint(BranchAccent, BranchEdge.WithAlpha(.4f));
                    Text(root, PlatformMissions.Brief((PlatformMission)i), cx, 103, cw - 4, 69, 16, BranchDim);
                    fitting[i] = Button(root, cx, 186, cw, 65, "QUEUE NEXT MODULE", () => LaunchMissionFit((PlatformMission)mission));
                }
                Instrument(root, sx, 283, sw, 151, "FLIGHT SERVICES / SHARED STATION", AvIcon.Bolt);
                Button(root, sx + 12, 328, (sw - 32) / 2, 49, "OPEN ENGINEERING / MODULE PORTS", () => engineering?.Invoke(), false, 17);
                Button(root, sx + (sw + 8) / 2, 328, (sw - 32) / 2, 49, "OPEN SENSOR FEED / IMAGERY", () => sensors?.Invoke(), false, 17);
                serviceCargo = Button(root, sx + 12, 385, sw - 28, 38, "CARGO RESUPPLY", () => support?.RequestResupply(), false, 17);
                serviceNotice = Mono(root, "", sx + 14, 457, sw - 28, 52, 17, BranchAccent);
                Instrument(root, sx, 523, sw, h - 523, "FLIGHT NET / ORDER READBACK", AvIcon.Radio);
                logText = Text(root, "", sx + 16, 564, sw - 32, h - 580, 18, BranchInk);
            }
            else if (Domain == OpsDomain.Cyber)
            {
                for (int i = 0; i < 4; i++)
                {
                    var upgrade = (CyberUpgrade)i;
                    upgrades[i] = Button(root, 12, 47 + i * 75, lw - 28, 64, CyberLocations.UpgradeName(upgrade), () => support?.RequestCyberUpgrade(upgrade), false, 17);
                }
                Mono(root, "HOME NODE REGISTER / LIVE STATUS", 18, 365, lw - 36, 25, 17, BranchAccent);
                serviceLedger = Mono(root, "", 18, 410, lw - 36, h - 430, 17, BranchInk);
                Instrument(root, sx, 0, sw, 392, "COUNTER-INTRUSION / HOME RESPONSE", AvIcon.Lock);
                for (int i = 0; i < 5; i++)
                {
                    var verb = (CyberVerb)i;
                    defenses[i] = Button(root, sx + 12, 46 + i * 65, sw - 28, 57, verb.ToString().ToUpperInvariant(), () => support?.RequestCyberVerb(verb, defenseTargets[(int)verb]), false, 18);
                }
                serviceNotice = Mono(root, "", sx + 16, 417, sw - 32, 60, 17, BranchAccent);
                Instrument(root, sx, 497, sw, h - 497, "SIGNALS WATCH / INCIDENT JOURNAL", AvIcon.Radio);
                logText = Mono(root, "", sx + 16, 542, sw - 32, h - 558, 16, BranchInk);
            }
            else
            {
                OpsArtwork.Draw(root, new Rect(18, -49, lw - 36, 239), 3);
                Mono(root, "FORWARD POSTS / FINITE SHARED STOCK", 18, 308, lw - 36, 27, 17, BranchAccent);
                serviceLedger = Mono(root, "", 18, 353, lw - 36, h - 431, 17, BranchInk);
                Button(root, 14, h - 63, lw - 32, 44, "RETURN TO FIELD TASKING", () => SelectPage(0), false, 17);
                Instrument(root, sx, 0, sw, 329, "ESPIONAGE / SUPPLIER MANIFEST", AvIcon.Stack2);
                marketText = Mono(root, "", sx + 16, 47, sw - 32, 76, 20, BranchInk);
                marketSignal = Button(root, sx + 12, 142, sw - 28, 74, "SIGNALS KIT", () => support?.RequestBlackMarket(0));
                marketCargo = Button(root, sx + 12, 228, sw - 28, 74, "ORBITAL CARGO", () => support?.RequestBlackMarket(1));
                serviceNotice = Text(root, "", sx + 16, 355, sw - 32, 113, 18, BranchAccent);
                Instrument(root, sx, 497, sw, h - 497, "FIELD NET / SUPPLY READBACK", AvIcon.Radio);
                logText = Text(root, "", sx + 16, 542, sw - 32, h - 558, 18, BranchInk);
            }
        }

        private void LaunchMissionFit(PlatformMission mission)
        {
            if (support == null || !CanSend) return;
            OrbitalPlatform p = support.LocalPlatform;
            PlatformFitStep step = PlatformMissions.Next(p, mission, support.OrbitNow);
            if (step.Complete || step.Failure != PlacementFailure.None || !Affordable(support.LaunchCost(step.Module))) return;
            if (step.Module == ModuleKind.Core) support.RequestCoreLaunch(OrbitRegimes.Standard);
            else support.RequestModuleLaunch(step.Module, step.Cell);
        }

        private void PaintServiceInventory(OrbitalPlatform p, CyberNetwork c, SpecOpsDetachment d, double now)
        {
            if (serviceLedger == null) return;
            if (Domain == OpsDomain.Space)
            {
                string inventory = "CELL   MODULE            STATE\n";
                int count = 0;
                if (p != null)
                    for (int cell = 0; cell < OrbitalPlatform.CellCount; cell++)
                    {
                        ModuleKind kind = p.Cell(cell); if (kind == ModuleKind.None) continue;
                        inventory += "\n" + OrbitalPlatform.CellName(cell) + " / " + PlatformModules.Info(kind).Code + " / " + (p.IsOnline(cell, now) ? "ON" : "OFF"); count++;
                    }
                serviceLedger.text = count == 0 ? "NO ASSEMBLY IN ORBIT\n\nSelect a mission fit to commission the core, then queue its real modules one launch at a time." : inventory;
                for (int i = 0; i < fitting.Length; i++)
                {
                    PlatformFitStep step = PlatformMissions.Next(p, (PlatformMission)i, now);
                    fittingBars[i].SetProgress(step.Fitted, step.Total);
                    float price = step.Complete ? 0 : support?.LaunchCost(step.Module) ?? PlatformModules.LaunchPrice(step.Module);
                    bool valid = !step.Complete && step.Failure == PlacementFailure.None;
                    string state = step.Complete ? "FIT COMPLETE / " + step.Fitted + "/" + step.Total : !valid ? PlatformWords.Placement(step.Failure) : !Affordable(price) ? "LOW OPS RESERVE" : "QUEUE " + PlatformModules.Info(step.Module).Code + " / " + price + " OPS";
                    fitting[i].Set(PlatformMissions.Name((PlatformMission)i) + " / " + step.Fitted + "/" + step.Total + "\n" + state, CanSend && valid && Affordable(price), step.Complete, step.Complete ? "Loadout fitted. Inspect actual module power and neighbours in engineering." : state + " / " + PlatformModules.Info(step.Module).Summary);
                }
                float cargoPrice = support?.LaunchCost(ModuleKind.Cargo) ?? PlatformModules.LaunchPrice(ModuleKind.Cargo);
                PlatformStats stats = p != null && p.Exists ? p.Stats(now) : default;
                string refusal = p == null || !p.Exists ? "NO STATION" : p.Pending != ModuleKind.None ? "DELIVERY IN FLIGHT" : p.Rods >= stats.RodCapacity && p.Fuel >= stats.FuelCapacity - .001f ? "FUEL / MAGAZINE FULL" : !Affordable(cargoPrice) ? "LOW OPS RESERVE" : null;
                serviceCargo.Set("CARGO / " + cargoPrice + " OPS / " + (refusal ?? "RESUPPLY FITTED TANKS + MAGAZINES"), CanSend && refusal == null);
                serviceNotice.text = p == null || !p.Exists ? "FLIGHT / CORE AWAITING LAUNCH" : p.Pending != ModuleKind.None ? "FLIGHT / " + PlatformModules.Info(p.Pending).Name + " / DELIVERY IN PROGRESS" : "FLIGHT / BUS CLEAR / " + Mathf.FloorToInt(p.Energy) + " KJ / " + stats.Online + " ONLINE MODULES";
            }
            else if (Domain == OpsDomain.Cyber)
            {
                string nodes = "";
                if (c != null)
                    for (int i = 0; i < CyberNetwork.TargetBase; i++)
                    {
                        if (!c.Exists(i)) continue;
                        nodes += (nodes.Length == 0 ? "" : "\n\n") + CyberWords.Callsign(c, i) + " / " + CyberWords.NodeState(c, i, now);
                    }
                serviceLedger.text = nodes.Length == 0 ? "NO HOME NETWORK" : nodes;
                int threat = support?.LocalCyberThreatSlot ?? -1;
                serviceNotice.text = threat >= 0 && c != null && c.Exists(threat) ? "LIVE ENEMY INGRESS / " + CyberWords.Callsign(c, threat) + "\nHONEYPOT CAN INTERRUPT THE ROUTE" : "Q / E SELECTS A HOME NODE\nISOLATION COSTS UPTIME / REJOIN IS FREE";
            }
            else
            {
                string posts = ""; int charges = 0;
                if (d != null)
                    for (int i = 0; i < SpecOpsDetachment.TeamCount; i++)
                    {
                        FieldTeam team = d.Team(i); if (team.State != TeamState.Holding || team.Charges <= 0 || team.PhaseEnd <= now ||
                            !OpsSectors.TryLocate(team.X, team.Z, out _)) continue;
                        charges += team.Charges;
                        posts += (posts.Length == 0 ? "" : "\n\n") + FieldWords.Callsign(i) + " / " + FieldWords.Post(team.Mission) + " / " + OpsSectors.Code(team.X, team.Z) + "\nQ" + team.Quality + " / " + team.Charges + " CHARGES / " + FieldWords.Clock(SpecOpsDetachment.PostPressureWindow(team, now));
                    }
                serviceLedger.text = posts.Length == 0 ? "NO FORWARD POSTS\n\nRECON / SPOT + SKYWATCH\nSABOTAGE / SUPPRESS + HUNT\nESPIONAGE / EAVESDROP + SUPPLY\nSAFEHOUSE / FORTIFY SECTOR" : posts;
                serviceNotice.text = "ALL FORWARD POSTS / " + charges + " CHARGES\n\nSignal kits top up each cyber resource independently. Covert cargo resupplies the fitted station. Every accepted shipment spends one espionage-post charge.";
            }
        }
    }
}
