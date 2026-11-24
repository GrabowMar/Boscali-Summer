using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.C2;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Ops;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Presentation.C2;
using BoscaliSummer.Modules.Support.Presentation.Ops;
using BoscaliSummer.Modules.Support.Runtime;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    // The SHADOW room mapping (SOF front). Inputs, the rail and the ORBIT room are in FrontViews.cs.
    internal static partial class FrontViews
    {
        // ---- SHADOW ---------------------------------------------------------------------------------------------------

        private static readonly MissionKind[] Missions = { MissionKind.Recon, MissionKind.Lase, MissionKind.Sabotage, MissionKind.Seize, MissionKind.Tap };

        public static MissionKind BestMission(in SofTargetRow t) =>
            t.Kind == TargetKind.Ground ? MissionKind.Lase : t.Kind == TargetKind.Building ? MissionKind.Seize : t.Kind == TargetKind.Relay ? MissionKind.Tap : MissionKind.Sabotage;

        private static string TargetName(in SofTargetRow t) => "T-" + t.Id + " " + SofWords.Target(t.Kind, t.Sub, t.Id).Replace(" #" + t.Id, "");

        /// <summary>The short time word of a team card (the card is narrow): a clock for the phase that ends, ETA for a march.</summary>
        private static string TeamEta(in SofTeamRow t, float now)
        {
            switch (t.State)
            {
                case TeamState.Raising: case TeamState.Recovering: return SpaceRules.Clock(t.EndsAt - now);
                case TeamState.OnSite: return t.Mission == MissionKind.Lase ? "HOLDING" : "ON SITE " + SpaceRules.Clock(t.EndsAt - now);
                case TeamState.Pinned: return "LOST IN " + SpaceRules.Clock(t.EndsAt - now);
                case TeamState.Moving: case TeamState.Returning:
                    if (t.Carried) return "IN HELO";
                    float speed = SofRules.SpeedMetresPerSecond * (t.Push ? SofRules.PushSpeedFactor : 1f);
                    return "ETA " + SpaceRules.Clock(SofRules.Distance(t.X, t.Z, t.DestX, t.DestZ) / speed);
                case TeamState.Ready: return t.LiftWaiting ? "LIFT ASKED" : "AT CAMP";
                default: return "";
            }
        }

        private static readonly string[] TeamWords = { "SABRE", "VIPER", "HALBERD", "KESTREL" };
        private static readonly string[] OpAdjectives = { "IRON", "PALE", "HOLLOW", "SILENT", "COLD", "AMBER", "BLIND", "CINDER" };
        private static readonly string[] OpNouns = { "HERON", "LANTERN", "ANVIL", "MARTEN", "SPINDLE", "WARDEN", "FALCONER", "THRESHOLD" };

        /// <summary>The dossier word of a team slot: fixed per slot, it carries no reading (the host's own words keep the A-1 designator).</summary>
        internal static string TeamCodename(int slot) => TeamWords[Math.Max(0, slot) % TeamWords.Length];

        /// <summary>The operation name of a mission: the same team, mission and target always give the same two words, so the quote and the card agree.</summary>
        internal static string OpName(int slot, MissionKind kind, int targetId)
        {
            int h = ((slot + 1) * 31 + (int)kind * 17 + Math.Max(0, targetId) * 7) & int.MaxValue;
            return OpAdjectives[h % OpAdjectives.Length] + " " + OpNouns[h / OpAdjectives.Length % OpNouns.Length];
        }

        /// <summary>One fixed rubber-stamp word per team state: STANDBY for a team at rest, TASKED once it marches on a mission.</summary>
        private static string StampWord(in SofTeamRow t) =>
            t.State == TeamState.Ready ? (t.LiftWaiting ? "LIFT ASKED" : "STANDBY") : t.State == TeamState.Moving && t.Mission != MissionKind.None ? "TASKED" : SofWords.State(t.State);

        /// <summary>What the host asks of a target for this mission, in the order words use.</summary>
        private static string NeedWord(MissionKind k) =>
            k == MissionKind.Lase ? "A GROUND CONTACT" : k == MissionKind.Sabotage ? "AN ENEMY ANCHOR" : k == MissionKind.Seize ? "A BUILDING" : k == MissionKind.Tap ? "A RELAY OR DATA CENTER" : "A TARGET";

        private static string TaskedOn(SofStateData s, int targetId)
        {
            string who = "";
            foreach (SofTeamRow t in s.Teams)
                if (t.TargetId == targetId && t.Mission != MissionKind.None && t.State != TeamState.Lost) who += (who.Length > 0 ? ", " : "") + SofRules.Callsign(t.Slot) + " " + SofWords.Kind(t.Mission);
            return who.Length > 0 ? "TASKED · " + who : "NO TEAM TASKED";
        }

        /// <summary>CAMP-1 is host camp slot 0.</summary>
        internal static string CampName(int slot) => "CAMP-" + (slot + 1);

        private static readonly int[] homes = new int[SofRules.MaxTeams];

        /// <summary>
        /// The SHADOW room (spec 7) from the SOF mirror (<paramref name="s"/>, null or unknown before the host speaks) and the SOF front row in <paramref name="v"/>:
        /// the TEAMS &amp; BASES tab (camps by host slot with the queued ones, the FOB, a card per team with its live verbs, each queued TRAIN row as a card), the map
        /// (camps, ghosts, held buildings, targets, teams, enemies) and the TARGETS tab (teams in the field, the target list, the aim's SEND card, hostiles near us).
        /// Picks follow ruling D-R5: a team or target that left, or a target id the host reused for another thing, is dropped with the reason, never swapped; a new
        /// aim (target or RECON point) pre-picks the nearest team that can go (spec 7.3); a mirror that stops running drops every pick.
        /// </summary>
        public static void Shadow(FrontRoomView v, in FrontInputs c, SofStateData s, bool known, CyberStateData cyber, int selectedSlot, int selectedTarget)
        {
            ShadowRoomView r = v.Shadow;
            MapFrame frame = c.Frame;
            float now = c.Now;
            r.Teams.Clear(); r.Targets.Clear(); r.Held.Clear(); r.Enemies.Clear(); r.EnemyMark.Clear(); r.Missions.Clear(); r.Crew.Clear(); r.Field.Clear();
            r.CanSweep = false; r.SweepLabel = "SWEEP"; r.QrfLine = ""; r.QrfTone = AvState.Inert; r.Near = 0;
            bool active = known && s != null && s.Active;
            r.Active = active;
            r.SelectedSlot = -1; r.SelectedTargetId = 0;
            r.TeamCap = active ? s.TeamCap : 0; r.TeamsUp = r.Afield = 0;
            r.FieldNote = ""; r.SendWhy = ""; r.GoIn = ""; r.Helo = "";
            r.FileTab = r.FileA = r.FileB = r.FileC = r.FileD = "";
            if (active) CampRules.Homes(s.Camps, s.Teams, homes);
            else for (int i = 0; i < homes.Length; i++) homes[i] = -1;
            Bases(v, c, active ? s : null);
            if (!active)
            {
                // Bug 9: no pick and no RECON point outlives a mirror that stopped running (a blink before the host speaks keeps them: nothing is written back then).
                r.Meta = known ? NoCamp : "WAITING FOR THE HOST";
                r.Reason = ""; r.HasPoint = false; r.AimSeen = 0; r.PickedId = 0;
                r.FieldNote = known ? "NO TEAM YET · BUILD A CAMP, THEN TRAIN ONE ON TEAMS & BASES" : "WAITING FOR THE HOST";
                return;
            }
            foreach (SofHeldRow h in s.Held)
                r.Held.Add(new SofHeldView { Id = h.Id, X = frame.U(h.X), Y = frame.V(h.Z), SecondsLeft = Math.Max(0f, h.Until - now), Name = "BUILDING #" + h.Id });
            int near = 0, hunting = 0;
            foreach (SofEnemyRow e in s.Enemies)
            {
                r.Enemies.Add(new NetPoint { X = frame.U(e.X), Y = frame.V(e.Z) });
                r.EnemyMark.Add(e.Hunted ? "Q" : e.Near ? "!" : "?");
                if (e.Near) near++;
                if (e.Hunted) hunting++;
            }
            // The other side: hostile teams near our sites and our QRF (protocol 42).
            bool rolling = s.SweepEndsAt > now, refit = !rolling && s.SweepReadyAt > now;
            r.Near = near;
            r.CanSweep = near > 0 && !rolling && !refit;
            r.SweepLabel = rolling ? "QRF " + (int)Math.Ceiling(s.SweepEndsAt - now) + " S" : refit ? "SWEEP " + FrontKit.Clock(s.SweepReadyAt - now) : "SWEEP · " + SofRules.SweepCost + " AP";
            r.QrfLine = rolling ? "QRF ROLLING · IT REACHES THEM IN " + (int)Math.Ceiling(s.SweepEndsAt - now) + " S"
                : hunting > 0 ? "QRF HUNTING " + hunting + " TEAM" + (hunting == 1 ? "" : "S") + " · THEIR EXPOSURE CLIMBS"
                : near > 0 ? "DETECTED WITHIN " + (int)(SofRules.DetectMetres / 1000f) + " KM OF OUR SITES" + (refit ? " · QRF REFITS " + FrontKit.Clock(s.SweepReadyAt - now) : " · SWEEP SENDS THE QRF")
                : refit ? "QUIET · QRF REFITS " + FrontKit.Clock(s.SweepReadyAt - now) : "QUIET · SWEEP HUNTS ENEMY TEAMS NEAR YOUR CAMPS";
            r.QrfTone = near > 0 || hunting > 0 ? AvState.Danger : AvState.Inert;

            // Picks (ruling D-R5). A picked team or target that left the snapshot is cleared with the reason, never swapped for another; a target id the host reused
            // (another kind, or far from where the pick last saw it: ids are recycled once a target is gone) is dropped too. Only "nothing picked yet" (-1) takes the first team.
            string gone = "";
            if (selectedSlot >= 0 && !s.Teams.Exists(t => t.Slot == selectedSlot)) { gone = SofRules.Callsign(selectedSlot) + " LEFT THE ROSTER"; selectedSlot = Gone; }
            else if (selectedSlot == -1 && s.Teams.Count > 0) selectedSlot = s.Teams[0].Slot;
            SofTargetRow tgt = default;
            bool haveTarget = false;
            if (selectedTarget != 0)
            {
                foreach (SofTargetRow t in s.Targets) if (t.Id == selectedTarget) { tgt = t; haveTarget = true; break; }
                if (!haveTarget) gone += (gone.Length > 0 ? " · " : "") + "T-" + selectedTarget + " IS NO LONGER REVEALED";
                else if (r.PickedId == tgt.Id && (tgt.Kind != r.PickedKind || tgt.Sub != r.PickedSub
                    || SofRules.Distance(tgt.X, tgt.Z, r.PickedX, r.PickedZ) > (tgt.Kind == TargetKind.Ground ? ReusedGroundMetres : ReusedSiteMetres)))
                {
                    gone += (gone.Length > 0 ? " · " : "") + "T-" + tgt.Id + " IS ANOTHER TARGET NOW";
                    haveTarget = false;
                }
                if (!haveTarget) selectedTarget = 0;
            }
            if (haveTarget) { r.PickedId = tgt.Id; r.PickedKind = tgt.Kind; r.PickedSub = tgt.Sub; r.PickedX = tgt.X; r.PickedZ = tgt.Z; r.HasPoint = false; }
            else r.PickedId = 0;
            if (gone.Length > 0) r.Reason = gone;
            bool point = !haveTarget && r.HasPoint;
            int aim = haveTarget ? tgt.Id : point ? r.PointId : 0;
            float ax = haveTarget ? tgt.X : frame.X(r.PointU), az = haveTarget ? tgt.Z : frame.Z(r.PointV);
            if (aim != r.AimSeen)
            {
                r.AimSeen = aim;
                int pre = aim != 0 ? SofAim.Nearest(s.Teams, ax, az) : -1;
                if (pre >= 0) selectedSlot = pre;
            }
            r.SelectedSlot = selectedSlot; r.SelectedTargetId = selectedTarget;

            // Teams by slot: every team on the roster, whatever the cap now (bug 7), and a queued TRAIN row in each free slot after them in queue order. The ones out
            // of camp also go in the TARGETS tab's field strip; every one not lost is a team the SEND card can pick.
            SofTeamRow cur = default;
            bool haveTeam = false, curLocked = false;
            string readyList = "";
            int qi = 0;
            for (int slot = 0; slot < SofRules.MaxTeams; slot++)
            {
                SofTeamRow t = default;
                bool present = false;
                foreach (SofTeamRow x in s.Teams) if (x.Slot == slot) { t = x; present = true; break; }
                if (!present)
                {
                    while (qi < v.Queue.Count && v.Row.Queue[qi].Id != ProgrammeId.TrainTeam) qi++;
                    if (qi >= v.Queue.Count) continue;
                    FrontQueueView q = v.Queue[qi++];
                    r.Teams.Add(new SofTeamView
                    {
                        Slot = slot, Training = true, Building = q.Building, Row = q.Row, Progress = q.Progress, BuildLeft = q.BuildLeft, Callsign = SofRules.Callsign(slot), Codename = TeamCodename(slot),
                        State = q.Building ? FrontPanels.Clocked("TRAINING ", q.BuildLeft) : "FUNDING " + Pct(q.Progress), Mission = "", Flags = "", Eta = "", Control = "", Stamp = "", Home = "", VerbNote = ""
                    });
                    continue;
                }
                bool locked = !t.Mine && !string.IsNullOrEmpty(t.Controller);
                AvState tone = t.State == TeamState.Pinned || t.State == TeamState.Lost ? AvState.Danger : t.Exposure >= 70 || (t.State != TeamState.Raising && t.Ammo < SofDesk.AmmoPerMission) || t.Hunted ? AvState.Caution
                    : t.State == TeamState.OnSite ? AvState.Info : AvState.Ready;
                bool afield = t.State == TeamState.Moving || t.State == TeamState.OnSite;
                string mission = t.Mission == MissionKind.None ? (t.State == TeamState.Ready ? "NO MISSION · SEND IT FROM TARGETS" : "NO MISSION")
                    : SofWords.Kind(t.Mission) + (t.TargetId > 0 ? " T-" + t.TargetId : "") + " · " + OpName(slot, t.Mission, t.TargetId) + (afield ? " · ODDS " + t.Odds + " %" : "");
                bool helo = t.Carried || t.Insert == Insertion.Helicopter;
                if (t.State != TeamState.Lost) r.TeamsUp++;
                if (afield || t.State == TeamState.Returning || t.State == TeamState.Pinned) r.Afield++; // FrontService.Sample's rule
                r.Teams.Add(new SofTeamView
                {
                    Slot = slot, Present = true, Selected = slot == selectedSlot, Callsign = SofRules.Callsign(slot), State = SofWords.State(t.State), Tone = tone,
                    Codename = TeamCodename(slot), Control = SofPageWords.Control(t), Stamp = StampWord(t), Locked = locked, Home = HomeWord(s, t, c),
                    Mission = mission, Flags = (helo ? "HELO" : "GROUND") + (t.Push ? " · PUSH" : "") + (t.Hold ? " · HOLD" : "") + (t.Exploit ? " · EXPLOIT" : "") + (t.Wounded ? " · WIA" : "") + (t.Hunted ? " · QRF ON US" : ""),
                    Eta = TeamEta(t, now), X = frame.U(t.X), Y = frame.V(t.Z), DestX = frame.U(t.DestX), DestY = frame.V(t.DestZ), TargetX = frame.U(t.TargetX), TargetY = frame.V(t.TargetZ),
                    HasDest = t.HasDest, Lasing = t.Lasing, Wounded = t.Wounded, Helicopter = helo, Exposure = t.Exposure, Ammo = t.Ammo, Odds = t.Odds,
                    VerbOn = locked ? (byte)0 : Verbs(s, t), VerbOnLatched = (byte)((t.Push ? 1 : 0) | (t.Hold ? 2 : 0) | (t.LiftWaiting ? 8 : 0)),
                    VerbNote = locked ? "VIEW ONLY · " + SofPageWords.Control(t) : VerbNote(t, now),
                });
                if (slot == selectedSlot) { cur = t; haveTeam = true; curLocked = locked; }
                string call = SofRules.Callsign(slot);
                if (t.State == TeamState.Moving || t.State == TeamState.OnSite || t.State == TeamState.Returning || t.State == TeamState.Pinned)
                    r.Field.Add(new SofFieldView
                    {
                        Slot = slot, Tone = tone,
                        Left = call + " " + TeamCodename(slot) + " › " + (t.Mission == MissionKind.None ? "HOME" : (t.TargetId > 0 ? "T-" + t.TargetId + " " : "") + SofWords.Kind(t.Mission)),
                        Right = (t.State == TeamState.Returning ? "EXFIL · " : t.State == TeamState.Pinned ? "PINNED · " : "") + TeamEta(t, now) + " · EXP " + t.Exposure + " %",
                    });
                bool go = SofAim.CanGo(t);
                if (go) readyList += (readyList.Length > 0 ? ", " : "") + call;
                if (t.State == TeamState.Lost || aim == 0) continue;
                float d = SofRules.Distance(t.X, t.Z, ax, az);
                string why = locked ? "CTRL " + t.Controller : t.State != TeamState.Ready ? SofWords.State(t.State) : "LOW AMMO";
                r.Crew.Add(new SofCrewView
                {
                    Slot = slot, Word = call + " · " + (go ? KmWord(d) : why),
                    Help = go ? call + " can go now: " + KmWord(d) + " from the aim, about " + FrontKit.Clock(d / SofRules.SpeedMetresPerSecond) + " on foot." + (t.Carried ? " It rides a helicopter: +20 odds." : "")
                        : call + " cannot go now: " + why.ToLowerInvariant() + ". Picking it shows why on SEND.",
                });
            }
            r.FieldNote = r.Field.Count > 0 ? "" : s.Teams.Count == 0 ? (r.CampsUp > 0 ? "NO TEAM YET · TRAIN ONE ON TEAMS & BASES" : "NO TEAM YET · BUILD A CAMP, THEN TRAIN ONE ON TEAMS & BASES")
                : "NO TEAM OUT · " + (readyList.Length > 0 ? readyList + " READY TO GO" : "NONE READY NOW");

            foreach (SofTargetRow t in s.Targets)
            {
                if (r.Targets.Count >= SofWire.MaxTargets) break;
                string on = OnIt(s, t.Id);
                r.Targets.Add(new SofTargetView
                {
                    Id = t.Id, Kind = t.Kind, Sub = t.Sub, Filter = SofAim.Filter(t.Kind, t.Sub), X = frame.U(t.X), Y = frame.V(t.Z), Exploit = t.Exploit, Resisted = t.Resisted,
                    Selected = t.Id == selectedTarget, Name = TargetName(t), Grid = OpsKit.Grid(t.X, t.Z),
                    Tag = on.Length > 0 ? on : t.Resisted ? "RESISTED" : (t.Exploit ? "EXPLOIT · " : "") + SofWords.Kind(BestMission(t)),
                    TagTone = on.Length > 0 ? AvState.Ready : t.Resisted ? AvState.Inert : t.Exploit ? AvState.Caution : AvState.Info,
                });
            }

            if (aim != 0)
            {
                // The aim's SEND card (spec 7.3): every mission, lit when the aim takes it, priced, and with its odds for the picked team (an estimate: zero armour, the
                // ring judged at the aim); why that team cannot go now ("" when it can: the room adds the price against the pilot's allocation); how it goes in (D-R2).
                bool ring = cyber != null && cyber.Nodes.Exists(nd => nd.Held && SofRules.Distance(nd.X, nd.Z, ax, az) <= SofRules.RingBoostMetres);
                foreach (MissionKind kind in Missions)
                {
                    bool recon = kind == MissionKind.Recon, valid = recon || (haveTarget && SofRules.Valid(kind, tgt.Kind, tgt.Sub)), exploit = !recon && haveTarget && tgt.Exploit;
                    r.Missions.Add(new SofMissionView
                    {
                        Kind = kind, Name = kind == MissionKind.Tap ? "TAP" : SofWords.Kind(kind), Ap = SofRules.CostOf(kind, exploit), Valid = valid, Exploit = exploit,
                        Best = haveTarget ? kind == BestMission(tgt) : recon,
                        Odds = !valid ? "" : kind == MissionKind.Lase ? "NO ROLL" : "EST ~" + SofRules.Odds(haveTeam ? cur.Exposure : 0f, 0, haveTeam && cur.Carried, exploit, ring) + " %",
                        Note = valid ? Does(kind) : point ? "A MAP POINT TAKES ONLY RECON" : "NEEDS " + NeedWord(kind),
                    });
                }
                string call = haveTeam ? SofRules.Callsign(cur.Slot) : "";
                int alt = SofAim.Nearest(s.Teams, ax, az);
                string other = alt >= 0 && alt != selectedSlot ? " · " + SofRules.Callsign(alt) + " CAN GO: PICK IT ABOVE" : "";
                bool ready = haveTeam && cur.State == TeamState.Ready;
                r.SendWhy = !haveTeam ? (s.Teams.Count == 0 ? (r.CampsUp > 0 ? "NO TEAM YET · TRAIN ONE ON TEAMS & BASES" : "NO TEAM YET · BUILD A CAMP, THEN TRAIN ONE")
                        : alt >= 0 ? "PICK A TEAM ABOVE" : "NO TEAM CAN GO NOW")
                    : curLocked ? "VIEW ONLY · " + call + " IS COMMANDED BY " + cur.Controller + other
                    : !ready ? call + " IS " + SofWords.State(cur.State) + (other.Length > 0 ? other : " · NO TEAM IS READY")
                    : cur.Ammo + 0.01f < SofDesk.AmmoPerMission ? call + " NEEDS " + (int)SofDesk.AmmoPerMission + " % AMMO · IT REARMS AT CAMP" + other
                    : "";
                if (haveTeam)
                {
                    float d = SofRules.Distance(cur.X, cur.Z, ax, az), sp = SofRules.SpeedMetresPerSecond;
                    r.GoIn = cur.Carried ? "HELO · " + call + " RIDES A PILOT'S HELICOPTER · +20 ODDS"
                        : "GROUND · " + KmWord(d) + " · ETA " + FrontKit.Clock(d / sp) + " · " + FrontKit.Clock(d / (sp * SofRules.PushSpeedFactor)) + " WITH PUSH";
                    r.Helo = cur.Carried ? "THE PILOT FLIES IT IN" : cur.LiftWaiting ? "HELO ASKED · A PILOT LANDING BESIDE " + call + " CARRIES IT: +20 ODDS"
                        : "HELO · LIFT ON TEAMS & BASES: A PILOT CARRIES IT, +20 ODDS";
                }
            }

            // The picked target's file card, or the RECON point's.
            if (haveTarget)
            {
                r.FileTab = "FILE T-" + tgt.Id;
                r.FileA = SofWords.Target(tgt.Kind, tgt.Sub, tgt.Id).Replace(" #" + tgt.Id, "") + (tgt.Exploit ? " · EXPLOIT" : tgt.Resisted ? " · RESISTED" : "");
                r.FileB = "GRID " + OpsKit.Grid(tgt.X, tgt.Z) + " · BEST " + SofWords.Kind(BestMission(tgt));
                r.FileC = TaskedOn(s, tgt.Id);
                r.FileD = tgt.Exploit ? "EXPLOIT · CUTS A TARGET MISSION'S COST" : tgt.Resisted ? "RESISTED · SPACE BEATS SOF" : "";
            }
            else if (point)
            {
                r.FileTab = "RECON POINT";
                r.FileA = "OPEN GROUND";
                r.FileB = "GRID " + OpsKit.Grid(ax, az);
                r.FileC = "ONLY RECON TAKES A POINT";
                r.FileD = Does(MissionKind.Recon);
            }

            r.Meta = r.TeamsUp + " TEAMS OUT · " + s.Enemies.Count + " ENEMY TEAMS SEEN";
        }

        /// <summary>A target id kept by a pick moved this far since the last build: the host reused the id for another target (a contact drives, a site never moves).</summary>
        private const float ReusedGroundMetres = 5000f, ReusedSiteMetres = 300f;

        private static string KmWord(float metres) =>
            (metres < 10000f ? (metres / 1000f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : Mathf.RoundToInt(metres / 1000f).ToString(System.Globalization.CultureInfo.InvariantCulture)) + " KM";

        /// <summary>What a mission does, in a few words (the SEND card's line under the missions).</summary>
        private static string Does(MissionKind k) =>
            k == MissionKind.Recon ? "REVEALS " + (SofRules.ReconRadius / 1000f).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " KM ROUND IT FOR " + FrontKit.Clock(SofRules.ReconRevealSeconds)
            : k == MissionKind.Lase ? "MARKS IT FOR YOUR STRIKES UNTIL STOPPED"
            : k == MissionKind.Sabotage ? "KNOCKS THE SITE OUT"
            : k == MissionKind.Seize ? "HOLDS IT " + FrontKit.Clock(SofRules.HeldSeconds) + " · A FOB SPOT"
            : "FEEDS CYBER THEIR NETWORK FOR " + FrontKit.Clock(SofRules.TapSeconds);

        /// <summary>The list row's tag for a target a team of ours works: "A-1 ON IT", "A-1 +1 ON IT"; "" when none.</summary>
        private static string OnIt(SofStateData s, int targetId)
        {
            string first = "";
            int n = 0;
            foreach (SofTeamRow t in s.Teams)
                if (t.TargetId == targetId && t.Mission != MissionKind.None && t.State != TeamState.Lost) { if (n++ == 0) first = SofRules.Callsign(t.Slot); }
            return n == 0 ? "" : first + (n > 1 ? " +" + (n - 1) : "") + " ON IT";
        }

        /// <summary>
        /// Camps by host slot (the mirror lists them in slot order: standing, damaged or lost; each queued CAMP row in the slot the host will build it into, taking over a
        /// lost card there), the FOB card (ruling D-R3), and a map ghost for each queued camp or FOB with a point. <paramref name="s"/> null: only the queue is known.
        /// </summary>
        private static void Bases(FrontRoomView v, in FrontInputs c, SofStateData s)
        {
            ShadowRoomView r = v.Shadow;
            MapFrame f = c.Frame;
            r.Camps.Clear(); r.Ghosts.Clear(); r.CampsUp = 0;
            int n = s != null ? Math.Min(s.Camps.Count, CampRules.MaxCamps) : 0, present = 0, down = 0, taken = 0;
            for (int i = 0; i < n; i++) { present |= 1 << i; if (s.Camps[i].Health == AnchorHealth.Down) down |= 1 << i; }
            var fob = new SofFobView();
            for (int i = 0; i < v.Queue.Count; i++)
            {
                FrontQueueRow q = v.Row.Queue[i];
                FrontQueueView qv = v.Queue[i];
                string clock = qv.Building ? FrontPanels.Clocked("", qv.BuildLeft) : Pct(qv.Progress);
                if (q.Id == ProgrammeId.Fob)
                {
                    fob = new SofFobView { Queued = true, Row = qv.Row, Progress = qv.Progress, State = qv.Building ? FrontPanels.Clocked("BUILDING ", qv.BuildLeft) : "FUNDING " + clock, Tone = qv.Building ? AvState.Caution : AvState.Info,
                        Note = qv.Building ? FrontPanels.Clocked("UP IN ", qv.BuildLeft, FrontPanels.AnyMoment) + " · REARM + REFUEL BY THE BUILDING" : "FRONT FUNDS PAY IT · CHIP IN SPEEDS IT UP" };
                    if (q.HasPoint) r.Ghosts.Add(new MapGhostView { U = f.U(q.X), V = f.V(q.Z), Kind = 1, Label = "FOB · " + clock });
                    continue;
                }
                if (q.Id != ProgrammeId.Camp) continue;
                int slot = FrontLadder.AnchorSlot(present, down, ref taken, CampRules.MaxCamps);
                if (slot < 0) continue;
                var cv = new SofCampView
                {
                    Slot = slot, Name = CampName(slot), State = InfraState.Building, X = q.HasPoint ? f.U(q.X) : -1f, Y = q.HasPoint ? f.V(q.Z) : -1f,
                    Place = q.HasPoint ? PlaceOf(q.X, q.Z, c) : "AUTO PLACE", Holds = "", Row = qv.Row, Progress = qv.Progress, BuildLeft = qv.BuildLeft
                };
                r.Camps.Add(cv);
                if (q.HasPoint) r.Ghosts.Add(new MapGhostView { U = cv.X, V = cv.Y, Label = cv.Name + " · " + cv.Place + " · " + clock });
            }
            for (int i = 0; i < n; i++)
            {
                if ((taken & (1 << i)) != 0) continue;
                SofCampRow cr = s.Camps[i];
                bool lost = cr.Health == AnchorHealth.Down;
                if (!lost) r.CampsUp++;
                r.Camps.Add(new SofCampView
                {
                    Slot = i, Name = CampName(i), Health = cr.Health, State = lost ? InfraState.Lost : cr.Health == AnchorHealth.Damaged ? InfraState.Damaged : InfraState.Up,
                    X = f.U(cr.X), Y = f.V(cr.Z), Place = PlaceOf(cr.X, cr.Z, c), Holds = lost ? "" : Holds(i)
                });
            }
            r.Camps.Sort((a, b) => a.Slot.CompareTo(b.Slot));

            if (!fob.Queued)
            {
                bool up = c.WorldKnown && c.World.FobUp, held = s != null && s.Held.Count > 0;
                fob = up ? new SofFobView { Up = true, Tone = AvState.Ready, State = c.FobUntil > c.Now ? "UP · " + FrontRules.Clock(c.FobUntil - c.Now) + " LEFT" : "UP",
                        Note = "REARM + REFUEL BY THE HELD BUILDING · ROOM FOR " + FrontRules.TeamsPerBase + " MORE TEAMS" }
                    : held ? new SofFobView { Tone = AvState.Inert, State = "NOT BUILT", Note = "PLACE IT ON A BUILDING YOUR TEAMS HOLD" }
                    : new SofFobView { Tone = AvState.Caution, State = "NEEDS A SEIZED BUILDING", Note = "SEIZE A BUILDING FROM TARGETS, THEN PLACE IT THERE" };
            }
            r.Fob = fob;
        }

        /// <summary>"HOLDS A-1, A-2 · FULL", "HOLDS A-1 · ROOM FOR 1", "ROOM FOR 2 TEAMS": the teams that call camp slot <paramref name="camp"/> home (<see cref="homes"/>).</summary>
        private static string Holds(int camp)
        {
            string who = "";
            int k = 0;
            for (int slot = 0; slot < homes.Length; slot++)
                if (homes[slot] == camp) { who += (who.Length > 0 ? ", " : "") + SofRules.Callsign(slot); k++; }
            int room = FrontRules.TeamsPerBase - k;
            return k == 0 ? "ROOM FOR " + room + " TEAMS" : "HOLDS " + who + (room <= 0 ? " · FULL" : " · ROOM FOR " + room);
        }

        /// <summary>Ruling D-R4: "AT CAMP-1" for a ready team standing by its home camp, "HOME CAMP-1" out in the field, the FOB, or NO CAMP.</summary>
        private static string HomeWord(SofStateData s, in SofTeamRow t, in FrontInputs c)
        {
            if (t.State == TeamState.Lost) return "NO CONTACT";
            int h = t.Slot < homes.Length ? homes[t.Slot] : -1;
            if (h < 0 || h >= s.Camps.Count) return c.WorldKnown && c.World.FobUp ? "HOME FOB" : "NO CAMP";
            bool at = t.State == TeamState.Ready && SofRules.Distance(t.X, t.Z, s.Camps[h].X, s.Camps[h].Z) <= SofDesk.BaseRadius;
            return (at ? "AT " : "HOME ") + CampName(h);
        }

        /// <summary>
        /// The verbs the host would take from this team now (bits: 1 PUSH, 2 HOLD, 4 EXFIL, 8 LIFT; SofDesk.Run's rules): PUSH while it marches, HOLD on the march or on
        /// site, EXFIL on the march (home again, by the nearest base), on site or when it stands away from every camp and held building, LIFT while ready on the ground.
        /// Pinned, training, wounded and lost: none.
        /// </summary>
        private static byte Verbs(SofStateData s, in SofTeamRow t)
        {
            bool moving = t.State == TeamState.Moving || t.State == TeamState.Returning;
            int on = (moving ? 1 : 0) | (moving || t.State == TeamState.OnSite ? 2 : 0) | (t.State == TeamState.Ready && !t.Carried ? 8 : 0);
            if (!t.Carried && (moving || t.State == TeamState.OnSite || (t.State == TeamState.Ready && Away(s, t)))) on |= 4;
            return (byte)on;
        }

        /// <summary>Farther than 50 m from every standing camp and held building: an EXFIL has somewhere to go (the host refuses one at home).</summary>
        private static bool Away(SofStateData s, in SofTeamRow t)
        {
            foreach (SofCampRow cr in s.Camps) if (cr.Health != AnchorHealth.Down && SofRules.Distance(t.X, t.Z, cr.X, cr.Z) <= 50f) return false;
            foreach (SofHeldRow h in s.Held) if (SofRules.Distance(t.X, t.Z, h.X, h.Z) <= 50f) return false;
            return true;
        }

        /// <summary>What the team's verbs mean now, or why it has none (ruling D-R2: LIFT is the helicopter insertion, a pilot picks the team up and it gains +20 odds).</summary>
        private static string VerbNote(in SofTeamRow t, float now)
        {
            if (t.Carried) return "IN A HELO · +20 ODDS";
            switch (t.State)
            {
                case TeamState.Raising: return "TRAINING · READY IN " + SpaceRules.Clock(t.EndsAt - now);
                case TeamState.Recovering: return "WIA · RECOVERS IN " + SpaceRules.Clock(t.EndsAt - now);
                case TeamState.Pinned: return "PINNED · TAKES NO ORDERS";
                case TeamState.Lost: return "LOST · NO CONTACT";
                case TeamState.OnSite: return "HOLD HIDES · EXFIL ABANDONS";
                case TeamState.Moving: return "PUSH 1.5x FASTER · HOLD HIDES";
                case TeamState.Returning: return "HEADING HOME · PUSH SPEEDS";
                default: return t.LiftWaiting ? "HELO ASKED · LAND BESIDE IT" : "LIFT = HELO PICKUP · +20 ODDS";
            }
        }
    }
}
