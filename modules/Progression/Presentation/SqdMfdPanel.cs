using NOAvionics;
using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Modules.Progression.Configuration;
using BoscaliSummer.Modules.Progression.Runtime;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Game;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Progression.Presentation
{
    /// <summary>
    /// "PILOT" (registry id SQD) — pilot status with the enemy ace watch, the shared OPS skill board (SPACE, CYBER, SOF),
    /// and STUDIO: the squadron (roster and pilot studio, built by Wing through <see cref="IWingSquad"/>) above the
    /// emblem paint shop. Reads encounter snapshots only; Wing owns aircraft orders, custom-pilot files and pilot
    /// generation. Emblems and the local pilot profile are client-local cosmetics.
    /// Built entirely from kit v2 (spec 2026-09-28-fui-panel-polish-design.md): one
    /// <see cref="AvConsole"/> with three icon tabs over a paged <see cref="AvFlow"/> body.
    /// </summary>
    internal sealed partial class SqdMfdPanel : MonoBehaviour, ISceneService
    {
        private const float Width = AvTokens.PanelWidth;
        private const float RefreshInterval = 0.20f;

        private const int TabPilot = 0;
        private const int TabSkills = 1;
        private const int TabStudio = 2;
        private const int WingRowsPerPage = 2;

        private ProgressionManager progression;
        private ProgressionSettings settings;
        private ISquadView squad;

        // The registry owns SQD; the cockpit-facing screen name is PILOT.
        private readonly MfdPanelInstaller installer =
            new MfdPanelInstaller(MfdSlots.Sqd, "SQD", "BoscaliSquadron.Screen", preferLeft: true)
            { Wrap = true, Plate = true, ShortName = "PILOT" };
        private MFDScreen screen => installer.Screen;
        private AvConsole console;

        // ---- Cockpit cosmetics (client-local) ---------------------------------------------

        private EmblemDesign emblem = EmblemDesign.Default;
        private string squadronName = string.Empty;
        private Sprite emblemSprite;
        private readonly EmblemCanvas emblemCanvas = new EmblemCanvas();
        private EmblemDesign emblemDrawn;
        private string emblemDrawnFile;
        private string[] emblemFiles = Array.Empty<string>();
        private int emblemFileIndex = -1;
        private string cosmeticNameText;
        private string cosmeticEmblemText;
        private string cosmeticFileText;

        private WingPilotRecord localProfile;
        private bool hasLocalProfile;
        private string localProfileKey;
        private Sprite profilePortrait;
        private string profilePortraitKey;

        private float nextRefresh;
        private bool viewOpen;

        public void Configure(ProgressionManager manager, ISquadView squadView,
            ProgressionSettings progressionSettings, ManualLogSource log)
        {
            progression = manager;
            squad = squadView;
            settings = progressionSettings;
            installer.Log = log;
            installer.Builder = BuildScreen;
            emblem = EmblemDesign.Parse(settings?.Emblem?.Value, EmblemDesign.Default);
            squadronName = settings?.SquadronName?.Value ?? "BOSCALI SUMMER";
        }

        public void ResetForScene()
        {
            installer.Reset();
            console = null;

            ResetSkillRows();
            ResetPilotPage();
            ResetWingsPage();
            ResetStudioPage();

            emblemSprite = null;
            emblemDrawnFile = null;
            emblemFiles = Array.Empty<string>();
            emblemFileIndex = -1;
            cosmeticNameText = null;
            cosmeticEmblemText = null;
            cosmeticFileText = null;
            profilePortrait = null;
            profilePortraitKey = null;
            hasLocalProfile = false;
            localProfileKey = null;

            nextRefresh = 0f;
            SetViewOpen(false);
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (installer.Failed || progression == null) return;
            if (!installer.Tick()) return;

            bool visible = screen.isActive && SceneSingleton<DynamicMap>.i?.maximizedMapCanvas?.isActiveAndEnabled == true;
            SetViewOpen(visible);
            if (visible && Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + RefreshInterval;
                Refresh();
            }
        }

        private void SetViewOpen(bool open)
        {
            if (viewOpen == open) return;
            viewOpen = open;
            ((IProgressionView)progression)?.SetViewOpen(open);
        }

        private IProgressionView Progress => progression;

        // ---- Installation ----------------------------------------------------------------

        private RectTransform BuildScreen(RectTransform content, float height)
        {
            console = AvConsole.Build(content, "PILOT", "PILOT", 3, Width, height);
            console.PageChanged += OnTabChanged;
            // No header tiles: SCORE, RANK and PICKS are on the PILOT page (ID card, PICK LEDGER) and SKILLS says the picks in hand.
            AvTabBar tabBar = console.Tabs(
                (AvIcon.User, "PILOT"),
                (AvIcon.Star, "SKILLS"),
                (AvIcon.Pencil, "STUDIO"));
            AttachTabHelp(tabBar);

            BuildPilotPage(console.Page(TabPilot));
            BuildSkillsPage(console.Page(TabSkills));
            studioPageFlow = console.Page(TabStudio);
            SetStandingOrders();
            console.Finish();
            return null;
        }

        /// <summary>Each page's standing orders, shown one at a time in the console footer: usage doctrine, most important first.</summary>
        private void SetStandingOrders()
        {
            console.Orders(TabPilot, "Free picks are spent on SKILLS. Tier sets an ace wing's threat; its badges say what it can do to you.");
            console.Orders(TabSkills, "Tap an open grade to read what it buys, then commit it. One pick, no undo.");
            console.Orders(TabStudio, "Roll fresh proofs and tap one to adopt it; FLY AS picks which saved pilot the dossier shows.");
        }

        /// <summary>Every tab carries the hover sentence the pre-kit-v2 console gave it.</summary>
        private static void AttachTabHelp(AvTabBar bar)
        {
            string[] hints =
            {
                "PILOT: your dossier. ID card, this sortie, the pick ledger and the known hostile ace wings with their threat, skills and who they are hunting.",
                "SKILLS: the OPS skill tree, three lanes of six grades. Tap an open grade, read what it buys, then unlock it. One pick, no undo.",
                "STUDIO: the squadron's pilots and their files, the pilot studio, the squadron emblem and name. Saved pilots and art stay on this machine.",
            };
            AvControl[] tabs = bar.Rect.GetComponentsInChildren<AvControl>(true);
            for (int i = 0; i < tabs.Length && i < hints.Length; i++) tabs[i].Help = hints[i];
        }

        private void OnTabChanged(int page)
        {
            CancelSkillConfirmation();
            skillSigSet = aceSigSet = false;
            nextRefresh = 0f;
            if (page == TabStudio)
            {
                if (!studioPageBuilt && studioPageFlow != null)
                {
                    BuildStudioPage(studioPageFlow);
                    studioPageBuilt = true;
                }
                studioArtDirty = true;
            }
        }

        // ---- Refresh ---------------------------------------------------------------------

        private void Refresh()
        {
            if (console == null || progression == null) return;

            bool bypass = progression.BypassRequirements;
            int score = Progress.Score;
            int bonus = 0;
            if (squad != null && GameManager.GetLocalPlayer<Player>(out Player local) && local != null)
            {
                ulong id = PlayerIdentity.Of(local);
                score = Math.Max(0, score - squad.GetScoreOrigin(id));
                bonus = squad.GetBonusPoints(id);
            }

            RefreshCosmetics();

            switch (console.CurrentPage)
            {
                case TabPilot: RefreshPilotPage(bypass, score, bonus); break;
                case TabSkills: RefreshSkillsPage(); break;
                case TabStudio: RefreshStudioPage(); break;
            }

            UpdateFooter();
        }

        private string huntBase, huntLine;

        private void UpdateFooter()
        {
            if (console == null) return;
            string baseLine = console.CurrentPage == TabStudio ? StudioStatusLine()
                : progression.BypassRequirements ? "DEBUG BYPASS — EVERY GRADE OPEN"
                : progression.LastResult;
            // A running ace hunt is the one alert every tab must carry; it used to live in a header chip.
            bool hunted = squad != null && squad.HuntActive;
            if (hunted && (!ReferenceEquals(baseLine, huntBase) || huntLine == null))
            {
                huntBase = baseLine;
                huntLine = "ACE HUNT ACTIVE · " + (string.IsNullOrEmpty(baseLine) ? "an ace is after you." : baseLine);
            }
            AvState state = hunted ? AvState.Danger : progression.BypassRequirements ? AvState.Caution : AvState.Inert;
            if (!hunted && console.CurrentPage == TabStudio && StudioMessageLive)
                state = studioMessageTone == StudioTone.Ok ? AvState.Ready
                    : studioMessageTone == StudioTone.Warn ? AvState.Caution
                    : studioMessageTone == StudioTone.Bad ? AvState.Danger : state;
            console.Footer.Set(hunted ? huntLine : baseLine, state);
        }

        // ---- Local cosmetics -------------------------------------------------------------

        private void RefreshCosmetics()
        {
            string wantedName = settings?.SquadronName?.Value ?? string.Empty;
            if (!string.Equals(cosmeticNameText, wantedName, StringComparison.Ordinal))
            {
                cosmeticNameText = wantedName;
                if (wantedName.Length > 24) wantedName = wantedName.Substring(0, 24);
                squadronName = wantedName;
            }

            string wantedEmblem = settings?.Emblem?.Value ?? string.Empty;
            if (!string.Equals(cosmeticEmblemText, wantedEmblem, StringComparison.Ordinal))
            {
                cosmeticEmblemText = wantedEmblem;
                EmblemDesign wanted = EmblemDesign.Parse(wantedEmblem, EmblemDesign.Default);
                if (wanted != emblem)
                {
                    emblem = wanted;
                    emblemSprite = null;
                }
            }

            string wantedFile = settings?.EmblemFile?.Value ?? string.Empty;
            if (!string.Equals(cosmeticFileText, wantedFile, StringComparison.Ordinal))
            {
                cosmeticFileText = wantedFile;
                emblemSprite = null;
            }
            if (emblemFileIndex < 0 || emblemFileIndex >= emblemFiles.Length ||
                !string.Equals(emblemFiles[emblemFileIndex], wantedFile, StringComparison.OrdinalIgnoreCase))
            {
                emblemFileIndex = -1;
                for (int i = 0; i < emblemFiles.Length; i++)
                    if (string.Equals(emblemFiles[i], wantedFile, StringComparison.OrdinalIgnoreCase))
                    {
                        emblemFileIndex = i;
                        break;
                    }
            }
            if (emblemSprite == null || emblemDrawn != emblem || emblemDrawnFile != wantedFile)
            {
                emblemDrawn = emblem;
                emblemDrawnFile = wantedFile;
                emblemSprite = (string.IsNullOrEmpty(wantedFile) ? null : EmblemRenderer.File(wantedFile))
                    ?? emblemCanvas.Draw(emblem);
            }

            // Read every refresh: the WMC studio edits (and renames) the profile pilot while this panel is open.
            string profileKey = settings?.PilotProfile?.Value ?? string.Empty;
            if (!string.Equals(localProfileKey, profileKey, StringComparison.OrdinalIgnoreCase))
            {
                localProfileKey = profileKey;
                profilePortrait = null;
                profilePortraitKey = null;
            }
            hasLocalProfile = !string.IsNullOrEmpty(profileKey) && WingLink.TryGetCustomPilot(profileKey, out localProfile);
        }

        private Sprite PlayerPortrait(string name, string callsign)
        {
            if (hasLocalProfile && localProfile.File != null) return localProfile.File;
            if (hasLocalProfile && localProfile.HasPortrait)
            {
                string key = "p|" + localProfile.Body + "|" + localProfile.Face + "|" + localProfile.Hair + "|" +
                    localProfile.Uniform + "|" + localProfile.Accessory + "|" + localProfile.Backdrop;
                if (profilePortraitKey != key)
                {
                    profilePortraitKey = key;
                    profilePortrait = WingLink.PilotPortraitForSelection(localProfile.Body,
                        localProfile.Face, localProfile.Hair, localProfile.Uniform,
                        localProfile.Accessory, localProfile.Backdrop);
                }
                if (profilePortrait != null) return profilePortrait;
            }
            // The identity portrait goes through the same cache: Wing Command's companion API
            // is a reflection seam, and a refresh must not re-enter it for an unchanged face.
            int faction = PortraitFactions.Local;
            string identityKey = "i|" + name + "|" + callsign + "|" + faction;
            if (profilePortraitKey != identityKey)
            {
                profilePortraitKey = identityKey;
                profilePortrait = WingLink.PersonnelPortrait(name, callsign, PortraitRole.Pilot, faction);
            }
            return profilePortrait;
        }

        // ---- Board rows ------------------------------------------------------------------

        private sealed class SkillRow
        {
            public byte Id;
            public SkillNode Node;
        }

        /// <summary>
        /// One lane's header: its name, a held-count bar and state word ("HELD", "OPEN",
        /// "CLOSED"), repainted as grades are taken. Every text owns a fixed slot.
        /// </summary>
        private sealed class SkillBranchRow
        {
            private const float HeaderName = 18f, BarY = 21f, StatusY = 27f;
            private readonly TMP_Text name, count, status;
            private readonly SqdBar bar;
            private AvState state = AvState.Info;
            private float fraction;
            public readonly List<byte> Ids = new List<byte>(PerkCatalog.MaximumDepth);

            public SkillBranchRow(RectTransform parent, string laneName)
            {
                name = AvText.Make(parent, "Lane " + laneName, AvTextRole.Head, laneName);
                AvText.Fit(name, false);
                count = AvText.Make(parent, "LaneCount " + laneName, AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineRight);
                AvText.Fit(count, false);
                status = AvText.Make(parent, "LaneState " + laneName, AvTextRole.Micro, "");
                AvText.Fit(status, false);
                bar = new SqdBar(parent, "Lane " + laneName);
            }

            public void Place(float x, float y, float width)
            {
                AvLay.Place(name.rectTransform, x, y, width, HeaderName);
                bar.Place(x, y + BarY, width, 3f);
                AvLay.Place(status.rectTransform, x, y + StatusY, Mathf.Max(0f, width - 34f), 14f);
                AvLay.Place(count.rectTransform, x + width - 34f, y + StatusY, 34f, 14f);
            }

            public void Paint(int taken, int total, string word, AvState next)
            {
                count.text = AvNum.Thousands(taken) + "/" + AvNum.Thousands(total);
                status.text = word;
                state = next;
                fraction = total <= 0 ? 0f : taken / (float)total;
                Restyle();
            }

            public void Restyle()
            {
                bool closed = state == AvState.Inert;
                name.color = closed ? AvTheme.Disabled : AvInk.Ink;
                count.color = closed ? AvTheme.Disabled : AvInk.Ink;
                status.color = closed ? AvTheme.Disabled : SqdTone.Text(state);
                bar.Restyle();
                bar.Set(fraction, closed ? AvTheme.RailInert : SqdTone.Rail(state));
            }
        }
    }
}
