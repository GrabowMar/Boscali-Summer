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
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Progression.Presentation
{
    /// <summary>
    /// "PILOT" (registry id SQD) — pilot status, shared skill board with support authorisations,
    /// enemy ace roster, and the squadron emblem page with a launcher for the Wing Command pilot studio. Reads encounter
    /// snapshots only; Wing Command owns aircraft orders, custom-pilot files and pilot
    /// generation. Emblems and the local pilot profile are client-local cosmetics.
    /// Built entirely from kit v2 (spec 2026-09-28-fui-panel-polish-design.md): one
    /// <see cref="AvConsole"/> with five icon tabs over a paged <see cref="AvFlow"/> body.
    /// </summary>
    internal sealed partial class SqdMfdPanel : MonoBehaviour, ISceneService
    {
        private const float Width = AvTokens.PanelWidth;
        private const float RefreshInterval = 0.20f;

        private const int TabPilot = 0;
        private const int TabSkills = 1;
        private const int TabWings = 2;
        private const int TabStudio = 3;
        private const int TabPlane = 4;
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
        private AvMetric[] metrics;

        // ---- Cockpit cosmetics (client-local) ---------------------------------------------

        private EmblemDesign emblem = EmblemDesign.Default;
        private string squadronName = string.Empty;
        private Sprite emblemSprite;
        private string emblemSpriteKey;
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
            metrics = null;

            ResetSkillRows();
            ResetPilotPage();
            ResetWingsPage();
            ResetStudioPage();
            ResetPlanePage();

            emblemSprite = null;
            emblemSpriteKey = null;
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
            console = AvConsole.Build(content, "PILOT", "SQUADRON DOSSIER", 5, Width, height);
            console.PageChanged += OnTabChanged;
            metrics = console.Metrics("SCORE", "RANK", "PICKS");
            AvTabBar tabBar = console.Tabs(
                (AvIcon.User, "PILOT"),
                (AvIcon.Star, "SKILLS"),
                (AvIcon.Skull, "ACES"),
                (AvIcon.Pencil, "STUDIO"),
                (AvIcon.Plane, "PLANE"));
            AttachTabHelp(tabBar);

            BuildPilotPage(console.Page(TabPilot));
            BuildSkillsPage(console.Page(TabSkills));
            BuildWingsPage(console.Page(TabWings));
            studioPageFlow = console.Page(TabStudio);
            BuildPlanePage(console.Page(TabPlane));
            console.Finish();
            return null;
        }

        /// <summary>Every tab carries the hover sentence the pre-kit-v2 console gave it.</summary>
        private static void AttachTabHelp(AvTabBar bar)
        {
            string[] hints =
            {
                "PILOT: your dossier. ID card, this sortie, the pick rings and every qualification lane at a glance.",
                "SKILLS: the qualification tree, four lanes of six grades. Tap an open grade, read what it buys, then unlock it. One pick, no undo.",
                "ACES: your flight and the known hostile ace wings, with threat, skills and who they are hunting. Wing management stays in Wing Command.",
                "STUDIO: design the squadron emblem and name, or open the Wing Command pilot studio to write custom pilots. Everything stays on this machine.",
                "PLANE: a live dossier of the aircraft you fly. Damage, engine map, flight data, systems, stores and the worst parts.",
            };
            AvControl[] tabs = bar.Rect.GetComponentsInChildren<AvControl>(true);
            for (int i = 0; i < tabs.Length && i < hints.Length; i++) tabs[i].Help = hints[i];
        }

        private void OnTabChanged(int page)
        {
            CancelSkillConfirmation();
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
            if (metrics == null || progression == null) return;

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
            RefreshMetrics(bypass, score, bonus);

            switch (console.CurrentPage)
            {
                case TabPilot: RefreshPilotPage(bypass, score, bonus); break;
                case TabSkills: RefreshSkillsPage(); break;
                case TabWings: RefreshWingsPage(); break;
                case TabStudio: RefreshStudioPage(); break;
                case TabPlane: RefreshPlanePage(); break;
            }

            UpdateFooter();
        }

        private void RefreshMetrics(bool bypass, int score, int bonus)
        {
            IProgressionView view = Progress;
            int ceiling = Mathf.Max(1, view.MaximumPoints);
            int available = view.AvailablePoints;
            bool capped = view.EarnedPoints >= ceiling;
            // The same ramp the host pays out, so the header never divides score itself.
            int remaining = PerkPoints.RemainingToNext(score, view.ScorePerPoint);

            metrics[0].Set(
                bypass ? "BYPASS" : AvNum.Thousands(score),
                bypass ? "OPEN" : capped ? "MAX" : remaining < 0 ? "DONE" : "NEXT " + AvNum.Thousands(remaining),
                bypass || capped || remaining < 0 ? 1f
                    : 1f - remaining / (float)Mathf.Max(1, view.ScorePerPoint * Mathf.Max(1, PerkCatalog.MaximumDepth)),
                bypass ? AvState.Caution : AvState.Ready);

            PilotView pilot = squad != null ? squad.Pilot : default;
            metrics[1].Set(AvNum.Thousands(view.Rank), "GEN " + AvNum.Thousands(pilot.Generation), 1f, AvState.Info);

            int earned = view.EarnedPoints;
            metrics[2].Set(
                bypass ? "FREE" : AvNum.Thousands(available),
                bypass ? "ALL" : earned + "/" + ceiling,
                bypass ? 1f : earned / (float)ceiling,
                available > 0 || bypass ? AvState.Ready : AvState.Info);
        }

        private void UpdateFooter()
        {
            if (console == null) return;
            string baseLine = console.CurrentPage == TabWings
                ? (squad != null ? squad.Status : "Enemy wing reports are unavailable.")
                : console.CurrentPage == TabStudio ? StudioStatusLine()
                : console.CurrentPage == TabPlane ? planeStatus
                : progression.BypassRequirements ? "DEBUG BYPASS — EVERY GRADE OPEN"
                : progression.LastResult;
            // A running ace hunt is the one alert every tab must carry; it used to live in a header chip.
            bool hunted = squad != null && squad.HuntActive && console.CurrentPage != TabWings;
            if (hunted) baseLine = "ACE HUNT ACTIVE · " + (string.IsNullOrEmpty(baseLine) ? "an ace is after you." : baseLine);
            console.Footer.Set(baseLine, hunted ? AvState.Danger : progression.BypassRequirements ? AvState.Caution : AvState.Inert);
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
            string key = emblem.Encode() + "|" + wantedFile;
            if (emblemSpriteKey != key || emblemSprite == null)
            {
                emblemSpriteKey = key;
                emblemSprite = string.IsNullOrEmpty(wantedFile)
                    ? EmblemRenderer.Procedural(emblem)
                    : EmblemRenderer.File(wantedFile) ?? EmblemRenderer.Procedural(emblem);
            }

            string profileKey = settings?.PilotProfile?.Value ?? string.Empty;
            if (!string.Equals(localProfileKey, profileKey, StringComparison.OrdinalIgnoreCase))
            {
                localProfileKey = profileKey;
                hasLocalProfile = !string.IsNullOrEmpty(profileKey) &&
                    WingLink.TryGetCustomPilot(profileKey, out localProfile);
                profilePortrait = null;
                profilePortraitKey = null;
            }
        }

        private Sprite PlayerPortrait(string name, string callsign)
        {
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
                name.color = closed ? AvTheme.Disabled : SqdTone.Ink;
                count.color = closed ? AvTheme.Disabled : SqdTone.Ink;
                status.color = closed ? AvTheme.Disabled : SqdTone.Text(state);
                bar.Restyle();
                bar.Set(fraction, closed ? AvTheme.RailInert : SqdTone.Rail(state));
            }
        }
    }
}
