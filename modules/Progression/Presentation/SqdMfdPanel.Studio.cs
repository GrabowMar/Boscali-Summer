using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Progression.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Progression.Presentation
{
    internal sealed partial class SqdMfdPanel
    {
        private sealed class StudioRow
        {
            public RectTransform Root;
            public Image Background;
            public TMP_Text Callsign;
            public TMP_Text Name;
            public TMP_Text Rank;
            public TMP_Text Status;
            public AvButton Select;
        }

        private const int StudioRowsPerPage = 2;

        /// <summary>How a studio message reads: a success, a caution, or a refusal.</summary>
        private enum StudioTone : byte
        {
            Info,
            Ok,
            Warn,
            Bad
        }

        private readonly List<WingPilotRecord> studioPilots = new List<WingPilotRecord>(64);
        private readonly StudioRow[] studioRows = new StudioRow[StudioRowsPerPage];
        private PilotDraft studioDraft = PilotDraft.New();
        private int studioPage;
        private int studioSelected = -1;
        private string studioSelectedCallsign;
        private bool studioDirty = true;
        private bool studioArtDirty = true;
        private string studioMessage = string.Empty;
        private StudioTone studioMessageTone = StudioTone.Info;
        private float studioMessageUntil;
        private string studioPortraitKey;
        private Sprite studioPortraitSprite;
        private string studioEditorKey;
        private string studioDeleteArmedCallsign;
        private float studioDeleteArmedUntil;

        private TMP_Text studioStatus;
        private TMP_Text studioPager;
        private AvButton studioPagerPrev;
        private AvButton studioPagerNext;
        private TMP_Text studioMessageText;
        private TMP_Text studioBodyValue;
        private TMP_Text studioFaceValue;
        private TMP_Text studioHairValue;
        private TMP_Text studioSuitValue;
        private TMP_Text studioBackValue;
        private TMP_Text studioStyleValue;
        private TMP_Text studioShapeValue;
        private TMP_Text studioChargeValue;
        private TMP_Text studioPaletteValue;
        private TMP_Text studioArtValue;
        private Image studioPortrait;
        private GameObject studioPortraitFallback;
        private Image studioEmblem;
        private TMP_Text studioEmblemFallback;
        private TMP_InputField studioCallsignField;
        private TMP_InputField studioNameField;
        private TMP_InputField studioBioField;
        private TMP_InputField studioSquadronField;
        private AvButton studioDeleteButton;
        private AvButton studioRecruitButton;
        private AvButton studioProfileButton;

        private void ResetStudioPage()
        {
            studioPilots.Clear();
            studioDraft = PilotDraft.New();
            studioPage = 0;
            studioSelected = -1;
            studioSelectedCallsign = null;
            studioDirty = true;
            studioArtDirty = true;
            studioMessage = string.Empty;
            studioMessageTone = StudioTone.Info;
            studioMessageUntil = 0f;
            studioPortraitKey = null;
            studioPortraitSprite = null;
            studioEditorKey = null;
            studioDeleteArmedCallsign = null;
            studioDeleteArmedUntil = 0f;
            studioStatus = null;
            studioPager = null;
            studioPagerPrev = null;
            studioPagerNext = null;
            studioMessageText = null;
            studioBodyValue = studioFaceValue = studioHairValue = studioSuitValue = studioBackValue = null;
            studioStyleValue = studioShapeValue = studioChargeValue = studioPaletteValue = studioArtValue = null;
            studioPortrait = null;
            studioPortraitFallback = null;
            studioEmblem = null;
            studioEmblemFallback = null;
            studioCallsignField = null;
            studioNameField = null;
            studioBioField = null;
            studioSquadronField = null;
            studioDeleteButton = null;
            studioRecruitButton = null;
            studioProfileButton = null;
            Array.Clear(studioRows, 0, studioRows.Length);
        }

        // ---- STUDIO page -----------------------------------------------------------------

        private void BuildStudioPage(RectTransform parent, Rect body)
        {
            if (!WingLink.PilotStudioAvailable)
            {
                PageRail(parent, new Rect(body.x, body.y, 3f, body.height));
                float px = body.x + SpineInset;
                float pw = body.width - SpineInset;
                AvKit.TacticalCard(parent, new Rect(px, body.y, pw, 106f), AvTheme.RailCaution);
                AvStyled.Label(parent, new Rect(px + 12f, body.y - 12f, pw - 24f, 20f),
                    "PILOT STUDIO UNAVAILABLE", "section-title");
                AvStyled.Label(parent, new Rect(px + 12f, body.y - 40f, pw - 24f, 54f),
                    WingLink.PilotStudioUnavailableReason, "row-sub");
                return;
            }

            RectTransform scrolled = AvScreen.Scroll(parent, body, StudioContentHeight(), out body);
            bool scrolls = !ReferenceEquals(scrolled, parent);
            parent = scrolled;
            PageRail(parent, new Rect(body.x, body.y, 3f, body.height));
            float x = body.x + SpineInset;
            float width = body.width - SpineInset;
            float y = body.y;

            y = DrawPageHeader(parent, x, y, width, "PILOT STUDIO",
                "LOCAL FILES + HOST ROSTER", SqdMark.Recon);
            y = DrawSectionTitle(parent, x, y, width, "CUSTOM PILOTS", "WING COMMAND CONNECTED", band: false);
            // Two lines: the ready line and Wing Command's own unavailable reason are both
            // sentences, and a wrapped sentence clipped to one line reads as a bug.
            studioStatus = PlainLabel(parent, new Rect(x, y + 2f, width, 26f), "", "row-sub");

            // ---- List actions and catalogue ---------------------------------------------
            float quarter = (width - AvTokens.Space2 * 3f) / 4f;
            AvStyled.Button(parent, new Rect(x, y - 32f, quarter, 26f), "+ NEW", "btn",
                NewStudioDraft, AvButtonStyle.Quiet).WithTooltip("Start a new custom pilot draft.");
            AvStyled.Button(parent, new Rect(x + quarter + AvTokens.Space2, y - 32f, quarter, 26f), "CLONE", "btn",
                CloneStudioDraft, AvButtonStyle.Quiet).WithTooltip("Copy the selected pilot into a new draft.");
            studioDeleteButton = AvStyled.Button(parent,
                new Rect(x + (quarter + AvTokens.Space2) * 2f, y - 32f, quarter, 26f), "DELETE", "btn",
                DeleteStudioDraft, AvButtonStyle.Danger);
            studioDeleteButton.WithTooltip("Delete the selected custom pilot file. Press twice to confirm.");
            AvStyled.Button(parent, new Rect(x + (quarter + AvTokens.Space2) * 3f, y - 32f, quarter, 26f),
                "IMPORT ALL", "btn", ImportAllCustomPilots, AvButtonStyle.Quiet)
                .WithTooltip("Recruit every custom pilot not already in the Wing Command squadron.");
            y -= 66f;

            float columnCallsign = x + 4f;
            float columnName = x + width * 0.18f;
            float columnRank = x + width * 0.56f;
            float columnStatus = x + width * 0.76f;
            PlainLabel(parent, new Rect(columnCallsign, y, width * 0.16f, 14f), "CALL", "section-title-note");
            PlainLabel(parent, new Rect(columnName, y, width * 0.36f, 14f), "NAME", "section-title-note");
            PlainLabel(parent, new Rect(columnRank, y, width * 0.18f, 14f), "RANK", "section-title-note");
            PlainLabel(parent, new Rect(columnStatus, y, width * 0.22f, 14f), "STATUS", "section-title-note");
            y -= 18f;
            for (int i = 0; i < StudioRowsPerPage; i++)
            {
                var rowObject = new GameObject("StudioRow_" + i, typeof(RectTransform));
                var root = (RectTransform)rowObject.transform;
                root.SetParent(parent, false);
                AvKit.Place(root, new Rect(x, y, width, AvTokens.RowPitch - 2f));
                var row = new StudioRow
                {
                    Root = root,
                    Background = AvKit.Panel(root, new Rect(0f, 0f, width, 26f), Color.clear),
                    Callsign = Fitted(PlainLabel(root, new Rect(4f, -2f, width * 0.16f, 22f), "", "row-name")),
                    Name = Fitted(PlainLabel(root, new Rect(width * 0.18f, -2f, width * 0.36f, 22f), "", "kv-value")),
                    Rank = Fitted(PlainLabel(root, new Rect(width * 0.56f, -2f, width * 0.18f, 22f), "", "kv-value")),
                    Status = Fitted(PlainLabel(root, new Rect(width * 0.76f, -2f, width * 0.22f, 22f), "", "kv-value")),
                };
                int index = i;
                row.Select = AvKit.HitButton(root, new Rect(0f, 0f, width, 26f), () => SelectStudio(index));
                row.Select.SetRowHighlight(row.Background, Color.clear, HoverFill());
                row.Select.WithTooltip("Open this custom pilot in the editor. Saving is local-only.");

                // Every slot keeps its rule, so an unfilled roster still has clear row rhythm.
                AvKit.Rule(root, new Rect(0f, -(AvTokens.RowPitch - 3f), width, 1f), AvTheme.Hairline.WithAlpha(0.10f));
                studioRows[i] = row;
                y -= AvTokens.RowPitch;
            }
            const float pagerArrow = 70f;
            studioPagerPrev = AvStyled.Button(parent, new Rect(x, y - 3f, pagerArrow, 22f), "PREVIOUS", "btn",
                () => ChangeStudioPage(-1), AvButtonStyle.Quiet).WithTooltip("Previous page of custom pilots.");
            studioPagerNext = AvStyled.Button(parent, new Rect(x + width - pagerArrow, y - 3f, pagerArrow, 22f),
                "NEXT", "btn", () => ChangeStudioPage(1), AvButtonStyle.Quiet)
                .WithTooltip("Next page of custom pilots.");
            studioPager = PlainLabel(parent, new Rect(x + pagerArrow + AvTokens.Space2, y - 2f,
                width - (pagerArrow + AvTokens.Space2) * 2f, 16f), "", "section-title-note");
            studioPager.alignment = TextAlignmentOptions.Center;
            y -= 30f;

            // ---- Editor -------------------------------------------------------------------
            y = DrawSectionTitle(parent, x, y, width, "PILOT EDITOR", "LOCAL FILE", band: true);

            // Sized off the editor's stepper block so the portrait is the height of the
            // record it illustrates, in the same 3:4 mount the pilot status page uses.
            Rect portraitFrame = new Rect(x, y - 2f, 92f, 138f);
            AvKit.Panel(parent, portraitFrame, AvTheme.SurfaceInert);
            AvKit.Outline(parent, portraitFrame, AvTheme.Frame);
            studioPortraitFallback = VisualPlaceholder(parent,
                new Rect(portraitFrame.x + 14f, portraitFrame.y - 35f, portraitFrame.width - 28f, 68f));
            studioPortrait = AvKit.Panel(parent,
                new Rect(portraitFrame.x + 3f, portraitFrame.y - 3f, portraitFrame.width - 6f, portraitFrame.height - 6f),
                Color.white);
            studioPortrait.type = Image.Type.Simple;
            studioPortrait.preserveAspect = true;
            studioPortrait.raycastTarget = false;
            studioPortrait.enabled = false;

            float stepperX = x + 100f;
            float stepperWidth = width - 100f;
            // The editor rows sit on the shared RowPitch grid the other three pages use,
            // so a stepper and a text field never stagger by four pixels.
            Stepper(parent, stepperX, y, stepperWidth, out studioBodyValue, "BODY",
                () => CycleDraft(d => d.CycleBody(-1, BodyCount())),
                () => CycleDraft(d => d.CycleBody(1, BodyCount())));
            Stepper(parent, stepperX, y - AvTokens.RowPitch, stepperWidth, out studioFaceValue, "FACE",
                () => CycleDraft(d => d.CycleFace(-1, FaceCount())),
                () => CycleDraft(d => d.CycleFace(1, FaceCount())));
            Stepper(parent, stepperX, y - AvTokens.RowPitch * 2f, stepperWidth, out studioHairValue, "HAIR",
                () => CycleDraft(d => d.CycleHair(-1, HairCount())),
                () => CycleDraft(d => d.CycleHair(1, HairCount())));
            Stepper(parent, stepperX, y - AvTokens.RowPitch * 3f, stepperWidth, out studioSuitValue, "SUIT",
                () => CycleDraft(d => d.CycleUniform(-1, UniformCount())),
                () => CycleDraft(d => d.CycleUniform(1, UniformCount())));
            Stepper(parent, stepperX, y - AvTokens.RowPitch * 4f, stepperWidth, out studioBackValue, "BACK",
                () => CycleDraft(d => d.CycleBackdrop(-1, BackdropCount())),
                () => CycleDraft(d => d.CycleBackdrop(1, BackdropCount())));
            y -= AvTokens.RowPitch * 5f + AvTokens.Space1;

            float fieldWidth = width - 52f - 66f;
            PlainLabel(parent, new Rect(x, y, 46f, AvTokens.RowHeight), "CALL", "form-key");
            studioCallsignField = AvKit.InputField(parent, new Rect(x + 48f, y, fieldWidth, 28f),
                PilotDraft.MaxCallsign, value => { studioDraft.Callsign = value; studioDraft.Normalize(); },
                placeholderText: "CALLSIGN", tooltip: "Pilot callsign, up to 14 characters.");
            AttachTooltip(studioCallsignField, "Pilot callsign, up to 14 characters.");
            AvStyled.Button(parent, new Rect(x + width - 64f, y, 64f, 28f), "RANDOM", "btn",
                RandomizeCallsign, AvButtonStyle.Quiet).WithTooltip("Generate a new random callsign.");
            y -= AvTokens.RowPitch;
            PlainLabel(parent, new Rect(x, y, 46f, AvTokens.RowHeight), "NAME", "form-key");
            studioNameField = AvKit.InputField(parent, new Rect(x + 48f, y, fieldWidth, 28f),
                PilotDraft.MaxName, value => { studioDraft.Name = value; studioDraft.Normalize(); },
                placeholderText: "NAME", tooltip: "Pilot name, up to 24 characters.");
            AttachTooltip(studioNameField, "Pilot name, up to 24 characters.");
            AvStyled.Button(parent, new Rect(x + width - 64f, y, 64f, 28f), "RANDOM", "btn",
                RandomizeName, AvButtonStyle.Quiet).WithTooltip("Generate a new random name.");
            y -= AvTokens.RowPitch;
            Stepper(parent, x, y, width, out studioStyleValue, "STYLE",
                () => CycleDraft(d => d.CyclePersona(-1)),
                () => CycleDraft(d => d.CyclePersona(1)));
            y -= AvTokens.RowPitch + AvTokens.Space1;

            PlainLabel(parent, new Rect(x, y, width - 100f, 14f), "BACKGROUND / LORE", "section-title-note");
            studioBioField = AvKit.InputField(parent, new Rect(x, y - 16f, width - 100f, 58f),
                PilotDraft.MaxBackground,
                value => { studioDraft.Background = value; studioDraft.Normalize(); },
                placeholderText: "Service background\u2026", multiline: true,
                tooltip: "Free-form service record shown on the pilot status page.");
            AttachTooltip(studioBioField, "Free-form service record shown on the pilot status page.");
            AvStyled.Button(parent, new Rect(x + width - 94f, y - 16f, 94f, 28f), "GENERATE", "btn",
                GenerateBio, AvButtonStyle.Quiet).WithTooltip("Generate a service background from the radio style.");
            y -= 84f;

            float actionWidth = (width - AvTokens.Space2 * 2f) / 3f;
            AvStyled.Button(parent, new Rect(x, y, actionWidth, 28f), "SAVE PILOT", "btn",
                SaveStudioDraft, AvButtonStyle.Primary).WithTooltip("Write this pilot to the Wing Command custom pilots folder.");
            studioProfileButton = AvStyled.Button(parent,
                new Rect(x + actionWidth + AvTokens.Space2, y, actionWidth, 28f), "SET AS MY PROFILE", "btn",
                SetAsLocalProfile, AvButtonStyle.Default);
            studioProfileButton.WithTooltip("Use this pilot's name, callsign, background and portrait as your local pilot profile.");
            studioRecruitButton = AvStyled.Button(parent,
                new Rect(x + (actionWidth + AvTokens.Space2) * 2f, y, actionWidth, 28f), "RECRUIT", "btn",
                ToggleRecruit, AvButtonStyle.Default);
            studioRecruitButton.WithTooltip("Add or remove this pilot from the Wing Command squadron.");
            y -= 38f;

            // ---- Squadron identity --------------------------------------------------------
            y = DrawSectionTitle(parent, x, y, width, "SQUADRON IDENTITY", "LOCAL COSMETIC", band: true);
            Rect emblemFrame = new Rect(x, y - 2f, 64f, 64f);
            studioEmblemFallback = PlainLabel(parent, new Rect(emblemFrame.x + 2f, emblemFrame.y - 22f, 60f, 20f),
                "NO ART", "row-sub");
            studioEmblemFallback.alignment = TextAlignmentOptions.Center;
            studioEmblem = AvKit.Panel(parent, emblemFrame, Color.white);
            studioEmblem.type = Image.Type.Simple;
            studioEmblem.preserveAspect = true;
            studioEmblem.raycastTarget = false;
            studioEmblem.enabled = false;

            float emblemStepperX = x + 76f;
            float emblemStepperWidth = width - 76f;
            Stepper(parent, emblemStepperX, y, emblemStepperWidth, out studioShapeValue, "SHAPE",
                () => CycleEmblem(-1, 0, 0), () => CycleEmblem(1, 0, 0));
            Stepper(parent, emblemStepperX, y - 28f, emblemStepperWidth, out studioChargeValue, "CHARGE",
                () => CycleEmblem(0, -1, 0), () => CycleEmblem(0, 1, 0));
            Stepper(parent, emblemStepperX, y - 56f, emblemStepperWidth, out studioPaletteValue, "PALETTE",
                () => CycleEmblem(0, 0, -1), () => CycleEmblem(0, 0, 1));
            AvStyled.Button(parent, new Rect(emblemStepperX, y - 92f, 108f, 26f), "RANDOMIZE", "btn",
                RandomizeEmblem, AvButtonStyle.Quiet).WithTooltip("Roll a new local emblem.");
            y -= 130f;

            PlainLabel(parent, new Rect(x, y, 96f, AvTokens.RowHeight), "SQUADRON", "form-key");
            studioSquadronField = AvKit.InputField(parent, new Rect(x + 98f, y, width - 98f, 28f), 24,
                value => { settings.SquadronName.Value = value ?? string.Empty; },
                placeholderText: "SQUADRON NAME", tooltip: "Local squadron name shown on the pilot status page.");
            AttachTooltip(studioSquadronField, "Local squadron name shown on the pilot status page.");
            y -= 34f;
            Stepper(parent, x, y, width - 96f, out studioArtValue, "ART",
                () => CycleArtFile(-1), () => CycleArtFile(1));
            AvStyled.Button(parent, new Rect(x + width - 92f, y, 92f, 30f), "NO ART", "btn",
                ClearArtFile, AvButtonStyle.Quiet).WithTooltip("Use the procedural emblem instead of a PNG.");

            y -= 40f;
            studioMessageText = PlainLabel(parent, new Rect(x, y, width, 30f),
                "Custom pilots are local files read by Wing Command; nothing is uploaded.", "row-sub");

            // The declared content height is an upper bound; trim the scroll's own content
            // to the depth that was actually drawn so a short editor never scrolls into
            // blank glass below the last control.
            if (scrolls)
            {
                float used = body.y - (y - 30f) + AvTokens.Space2;
                scrolled.sizeDelta = new Vector2(scrolled.sizeDelta.x, used);
            }
        }

        /// <summary>
        /// An upper bound for the editor's scroll content. The build trims the content to
        /// what it actually drew afterwards, so this only has to be generous enough that
        /// the viewport is created; it is never the depth a reader scrolls to.
        /// </summary>
        private static float StudioContentHeight() => 1040f;

        private void Stepper(RectTransform parent, float x, float y, float width,
            out TMP_Text value, string caption, Action previous, Action next)
        {
            PlainLabel(parent, new Rect(x, y, 48f, AvTokens.RowHeight), caption, "form-key");
            AvKit.Stepper(parent, x + 50f, y, width - 50f, out value, previous, next,
                caption + " — left/right steps the selection.");
        }

        /// <summary>
        /// The shared hover target, so an input field's help reaches the status strip like
        /// every button's does. The field's own widget takes the tooltip text but does not
        /// yet publish it, and this keeps the fix on this side of the avionics seam.
        /// </summary>
        private static void AttachTooltip(Component control, string text)
        {
            if (control == null) return;
            control.gameObject.AddComponent<AvTooltipTarget>().Initialise(text);
        }

        // ---- STUDIO refresh --------------------------------------------------------------

        private void RefreshStudioPage()
        {
            if (studioStatus == null || settings == null) return;

            if (studioArtDirty)
            {
                studioArtDirty = false;
                emblemFiles = WingLink.PilotStudioAvailable ? EmblemRenderer.ScanFiles() : Array.Empty<string>();
            }
            if (studioDirty)
            {
                studioDirty = false;
                ReloadStudioPilots();
            }

            bool available = WingLink.PilotStudioAvailable;
            studioStatus.text = available
                ? "Wing Command companion API ready · " + studioPilots.Count + " custom pilot file(s)."
                : WingLink.PilotStudioUnavailableReason;
            studioStatus.color = available ? AvTheme.Dim : AvTheme.Alert;

            int pageCount = Math.Max(1, (studioPilots.Count + StudioRowsPerPage - 1) / StudioRowsPerPage);
            studioPage = Mathf.Clamp(studioPage, 0, pageCount - 1);
            studioPagerPrev.SetEnabled(studioPage > 0);
            studioPagerPrev.WithTooltip(studioPage > 0
                ? "Previous page of custom pilots." : "Already on the first page.");
            studioPagerNext.SetEnabled(studioPage < pageCount - 1);
            studioPagerNext.WithTooltip(studioPage < pageCount - 1
                ? "Next page of custom pilots." : "Already on the last page.");
            int first = studioPage * StudioRowsPerPage;
            studioPager.text = studioPilots.Count == 0 ? "NO PILOTS — PRESS NEW OR IMPORT ALL"
                : (first + 1) + "–" + Math.Min(first + StudioRowsPerPage, studioPilots.Count) +
                  " OF " + studioPilots.Count + "   ·   PAGE " + (studioPage + 1) + "/" + pageCount;

            for (int i = 0; i < studioRows.Length; i++)
            {
                StudioRow row = studioRows[i];
                int index = first + i;
                bool visible = index < studioPilots.Count;
                row.Root.gameObject.SetActive(visible);
                if (!visible) continue;
                WingPilotRecord record = studioPilots[index];
                row.Callsign.text = record.Callsign;
                row.Name.text = record.Name;
                row.Rank.text = WingLink.RankNameForXp(record.Xp);
                bool inSquad = WingLink.IsPilotRecruited(record.Callsign);
                row.Status.text = inSquad ? "IN SQUADRON" : "READY";
                row.Status.color = inSquad ? AvTheme.RailReady : AvTheme.Dim;
                bool selected = studioSelected == index;
                row.Background.color = selected ? HoverFill() : Color.clear;
                row.Select.SetEnabled(true);
            }

            RefreshStudioEditor();
            if (studioMessageText != null)
            {
                bool quiet = string.IsNullOrEmpty(studioMessage);
                studioMessageText.text = quiet
                    ? "Custom pilots are local files read by Wing Command; nothing is uploaded."
                    : studioMessage;
                studioMessageText.color = quiet ? AvTheme.Dim
                    : studioMessageTone == StudioTone.Ok ? AvTheme.RailReady
                    : studioMessageTone == StudioTone.Warn ? AvTheme.Warning
                    : studioMessageTone == StudioTone.Bad ? AvTheme.Alert
                    : AvTheme.Dim;
            }
        }

        private void RefreshStudioEditor()
        {
            studioDraft.Normalize();

            string editorKey = studioDraft.Body + "|" + studioDraft.Face + "|" + studioDraft.Hair + "|" +
                studioDraft.Uniform + "|" + studioDraft.Backdrop + "|" + studioDraft.Accessory + "|" +
                (studioDraft.HasPortrait ? 1 : 0) + "|" + studioDraft.Persona + "|" +
                studioDraft.Name + "|" + studioDraft.Callsign;
            if (!string.Equals(editorKey, studioEditorKey, StringComparison.Ordinal))
            {
                studioEditorKey = editorKey;
                int faces = FaceCount(), hair = HairCount(), backdrops = BackdropCount();
                studioBodyValue.text = WingLink.PortraitBodyLabel(studioDraft.Body);
                studioFaceValue.text = "FACE " + (studioDraft.Face + 1) + "/" + faces;
                studioHairValue.text = studioDraft.Hair == 0 ? "BALD" : "HAIR " + studioDraft.Hair + "/" + (hair - 1);
                studioSuitValue.text = WingLink.PortraitUniformLabel(studioDraft.Uniform);
                studioBackValue.text = "BACK " + (studioDraft.Backdrop + 1) + "/" + backdrops;
                studioStyleValue.text = WingLink.PersonaLabel(studioDraft.Persona).ToUpperInvariant();

                string portraitKey = studioDraft.HasPortrait
                    ? "p|" + studioDraft.Body + "|" + studioDraft.Face + "|" + studioDraft.Hair + "|" +
                      studioDraft.Uniform + "|" + studioDraft.Accessory + "|" + studioDraft.Backdrop
                    : "i|" + studioDraft.Name + "|" + studioDraft.Callsign;
                if (!string.Equals(portraitKey, studioPortraitKey, StringComparison.Ordinal))
                {
                    studioPortraitKey = portraitKey;
                    studioPortraitSprite = studioDraft.HasPortrait
                        ? WingLink.PilotPortraitForSelection(studioDraft.Body, studioDraft.Face, studioDraft.Hair,
                            studioDraft.Uniform, studioDraft.Accessory, studioDraft.Backdrop)
                        : WingLink.PilotPortrait(studioDraft.Name, studioDraft.Callsign);
                }
                SetPortrait(studioPortrait, studioPortraitFallback, studioPortraitSprite);
            }

            if (!studioCallsignField.isFocused)
                studioCallsignField.SetTextWithoutNotify(studioDraft.Callsign);
            if (!studioNameField.isFocused)
                studioNameField.SetTextWithoutNotify(studioDraft.Name);
            if (!studioBioField.isFocused)
                studioBioField.SetTextWithoutNotify(studioDraft.Background);
            if (!studioSquadronField.isFocused)
                studioSquadronField.SetTextWithoutNotify(squadronName);

            if (studioEmblem != null)
            {
                studioEmblem.sprite = emblemSprite;
                studioEmblem.enabled = emblemSprite != null;
                studioEmblemFallback.gameObject.SetActive(emblemSprite == null);
            }
            studioShapeValue.text = EmblemDesign.ShapeNames[emblem.Shape];
            studioChargeValue.text = EmblemDesign.ChargeNames[emblem.Charge];
            studioPaletteValue.text = "PALETTE " + (emblem.Palette + 1) + "/" + EmblemDesign.PaletteCount;
            studioArtValue.text = emblemFileIndex >= 0 && emblemFileIndex < emblemFiles.Length
                ? System.IO.Path.GetFileNameWithoutExtension(emblemFiles[emblemFileIndex])
                : "NONE · PROCEDURAL";

            bool valid = studioDraft.IsValid;
            bool recruited = valid && WingLink.IsPilotRecruited(studioDraft.Callsign);
            // State is carried by the word on the button and the latched paint, never by a
            // solid accent plate; the stylesheet's latched wash is the strongest fill these
            // controls wear.
            studioRecruitButton.SetText(recruited ? "DISCHARGE" : "RECRUIT");
            studioRecruitButton.SetEnabled(valid);
            studioRecruitButton.SetLatched(recruited);
            studioRecruitButton.ClearCustomColors();

            bool profileActive = valid &&
                string.Equals(settings.PilotProfile.Value, studioDraft.Callsign, StringComparison.OrdinalIgnoreCase);
            studioProfileButton.SetLatched(profileActive);
            studioProfileButton.SetEnabled(valid);
            studioProfileButton.ClearCustomColors();

            bool deleteArmed = valid && studioSelected >= 0 && Time.unscaledTime < studioDeleteArmedUntil &&
                string.Equals(studioDeleteArmedCallsign, studioDraft.Callsign, StringComparison.OrdinalIgnoreCase);
            studioDeleteButton.SetEnabled(valid && studioSelected >= 0);
            studioDeleteButton.SetLatched(deleteArmed);
            studioDeleteButton.ClearCustomColors();
        }

        private void ReloadStudioPilots()
        {
            studioPilots.Clear();
            if (WingLink.TryListCustomPilots(out WingPilotRecord[] records))
            {
                studioPilots.AddRange(records);
                studioPilots.Sort((a, b) => string.Compare(a.Callsign, b.Callsign, StringComparison.OrdinalIgnoreCase));
            }
            studioSelected = -1;
            if (!string.IsNullOrEmpty(studioSelectedCallsign))
            {
                for (int i = 0; i < studioPilots.Count; i++)
                {
                    if (!string.Equals(studioPilots[i].Callsign, studioSelectedCallsign,
                        StringComparison.OrdinalIgnoreCase)) continue;
                    studioSelected = i;
                    studioPage = i / StudioRowsPerPage;
                    break;
                }
            }
        }

        // ---- STUDIO actions --------------------------------------------------------------

        private void ChangeStudioPage(int delta)
        {
            int pageCount = Math.Max(1, (studioPilots.Count + StudioRowsPerPage - 1) / StudioRowsPerPage);
            int wanted = Mathf.Clamp(studioPage + delta, 0, pageCount - 1);
            if (wanted == studioPage) return;
            studioPage = wanted;
            nextRefresh = 0f;
        }

        private void SelectStudio(int index)
        {
            int absolute = studioPage * StudioRowsPerPage + index;
            if (absolute < 0 || absolute >= studioPilots.Count) return;
            studioSelected = absolute;
            studioSelectedCallsign = studioPilots[absolute].Callsign;
            studioDraft = DraftFrom(studioPilots[absolute]);
            studioPortraitKey = null;
            studioEditorKey = null;
            studioDeleteArmedCallsign = null;
            nextRefresh = 0f;
        }

        private void NewStudioDraft()
        {
            studioSelected = -1;
            studioSelectedCallsign = null;
            studioDraft = PilotDraft.New();
            studioPortraitKey = null;
            studioEditorKey = null;
            RandomizeLooks();
            NextMessage("New pilot draft — set a callsign and save.", StudioTone.Info);
            nextRefresh = 0f;
        }

        private void CloneStudioDraft()
        {
            if (studioSelected < 0 || studioSelected >= studioPilots.Count)
            {
                NextMessage("Select a custom pilot to clone first.", StudioTone.Warn);
                return;
            }
            studioDraft = DraftFrom(studioPilots[studioSelected]);
            string baseCallsign = studioDraft.Callsign;
            int room = PilotDraft.MaxCallsign - 2;
            if (baseCallsign.Length > room) baseCallsign = baseCallsign.Substring(0, room);
            studioDraft.Callsign = baseCallsign + "-2";
            studioDraft.Normalize();
            studioSelected = -1;
            studioSelectedCallsign = null;
            studioPortraitKey = null;
            studioEditorKey = null;
            NextMessage("Cloned " + baseCallsign + " — adjust the callsign and save.", StudioTone.Ok);
            nextRefresh = 0f;
        }

        private void DeleteStudioDraft()
        {
            if (studioSelected < 0 || studioSelected >= studioPilots.Count)
            {
                NextMessage("Select a custom pilot file to delete first.", StudioTone.Warn);
                return;
            }
            string callsign = studioPilots[studioSelected].Callsign;
            if (!string.Equals(studioDeleteArmedCallsign, callsign, StringComparison.OrdinalIgnoreCase) ||
                Time.unscaledTime >= studioDeleteArmedUntil)
            {
                studioDeleteArmedCallsign = callsign;
                studioDeleteArmedUntil = Time.unscaledTime + 4f;
                NextMessage("Delete " + callsign + "? Press DELETE again within a moment to confirm.",
                    StudioTone.Warn);
                nextRefresh = 0f;
                return;
            }
            studioDeleteArmedCallsign = null;
            studioDeleteArmedUntil = 0f;
            if (WingLink.DeleteCustomPilot(callsign))
            {
                RemoveLocalProfileIf(callsign);
                if (string.Equals(studioSelectedCallsign, callsign, StringComparison.OrdinalIgnoreCase))
                    studioSelectedCallsign = null;
                studioDirty = true;
                NextMessage("Deleted custom pilot " + callsign + ".", StudioTone.Ok);
            }
            else NextMessage("Wing Command could not delete " + callsign + ".", StudioTone.Bad);
            nextRefresh = 0f;
        }

        private void ImportAllCustomPilots()
        {
            int imported = WingLink.ImportAllCustomPilots();
            studioDirty = true;
            NextMessage(imported > 0
                ? "Recruited " + imported + " custom pilot(s) to the squadron."
                : "No new custom pilots to recruit.",
                imported > 0 ? StudioTone.Ok : StudioTone.Info);
            nextRefresh = 0f;
        }

        private void SaveStudioDraft()
        {
            studioDraft.Normalize();
            if (!studioDraft.IsValid)
            {
                NextMessage("Enter a callsign before saving.", StudioTone.Warn);
                return;
            }
            if (WingLink.SaveCustomPilot(RecordFrom(studioDraft)))
            {
                studioSelectedCallsign = studioDraft.Callsign;
                studioDirty = true;
                localProfileKey = null; // reload the local profile from the saved file
                NextMessage("Saved " + studioDraft.Callsign + " to the custom pilots folder.", StudioTone.Ok);
            }
            else NextMessage("Wing Command rejected the save — check the BepInEx log.", StudioTone.Bad);
            nextRefresh = 0f;
        }

        private void SetAsLocalProfile()
        {
            studioDraft.Normalize();
            if (!studioDraft.IsValid)
            {
                NextMessage("Enter a callsign before setting a profile.", StudioTone.Warn);
                return;
            }
            settings.PilotProfile.Value = studioDraft.Callsign;
            NextMessage(studioDraft.Callsign + " is now your local pilot profile.", StudioTone.Ok);
            nextRefresh = 0f;
        }

        private void ToggleRecruit()
        {
            studioDraft.Normalize();
            if (!studioDraft.IsValid)
            {
                NextMessage("Select a pilot with a callsign first.", StudioTone.Warn);
                return;
            }
            bool recruited = WingLink.IsPilotRecruited(studioDraft.Callsign);
            bool changed = recruited
                ? WingLink.DischargeCustomPilot(studioDraft.Callsign)
                : WingLink.RecruitCustomPilot(studioDraft.Callsign);
            NextMessage(changed
                ? (recruited ? "Discharged " + studioDraft.Callsign + " from the squadron."
                             : "Recruited " + studioDraft.Callsign + " to the squadron.")
                : "Wing Command did not change the squadron roster.",
                changed ? StudioTone.Ok : StudioTone.Warn);
            nextRefresh = 0f;
        }

        private void RandomizeCallsign()
        {
            if (!WingLink.TryCreatePilot(NextSeed(), out _, out string callsign, out _, out _)) return;
            studioDraft.Callsign = callsign;
            studioDraft.Normalize();
            NextMessage("Callsign rolled: " + studioDraft.Callsign + ".", StudioTone.Ok);
            nextRefresh = 0f;
        }

        private void RandomizeName()
        {
            if (!WingLink.TryCreatePilot(NextSeed(), out string name, out _, out _, out _)) return;
            studioDraft.Name = name;
            studioDraft.Normalize();
            NextMessage("Name rolled: " + studioDraft.Name + ".", StudioTone.Ok);
            nextRefresh = 0f;
        }

        private void GenerateBio()
        {
            if (!WingLink.TryCreatePilot(NextSeed(), out _, out _, out string background, out int persona)) return;
            studioDraft.Background = background;
            studioDraft.Persona = persona;
            studioDraft.Normalize();
            NextMessage("Generated a service background.", StudioTone.Ok);
            nextRefresh = 0f;
        }

        private void RandomizeLooks()
        {
            studioDraft.HasPortrait = true;
            studioDraft.Body = UnityEngine.Random.Range(0, Math.Max(1, BodyCount()));
            studioDraft.Face = UnityEngine.Random.Range(0, Math.Max(1, FaceCount()));
            studioDraft.Hair = UnityEngine.Random.Range(0, Math.Max(1, HairCount()));
            studioDraft.Uniform = UnityEngine.Random.Range(0, Math.Max(1, UniformCount()));
            studioDraft.Backdrop = UnityEngine.Random.Range(0, Math.Max(1, BackdropCount()));
            studioDraft.Accessory = 0;
            studioPortraitKey = null;
            nextRefresh = 0f;
        }

        private void CycleDraft(Func<PilotDraft, PilotDraft> change)
        {
            studioDraft = change(studioDraft);
            studioDraft.Normalize();
            studioPortraitKey = null;
            nextRefresh = 0f;
        }

        private void CycleEmblem(int shape, int charge, int palette)
        {
            emblem = emblem.Cycle(shape, charge, palette);
            settings.Emblem.Value = emblem.Encode();
            NextMessage("Emblem updated: " + EmblemDesign.ShapeNames[emblem.Shape] + " / " +
                EmblemDesign.ChargeNames[emblem.Charge] + ".", StudioTone.Ok);
            nextRefresh = 0f;
        }

        private void RandomizeEmblem()
        {
            emblem = EmblemDesign.Random(NextSeed());
            settings.Emblem.Value = emblem.Encode();
            settings.EmblemFile.Value = string.Empty;
            NextMessage("Rolled a new emblem.", StudioTone.Ok);
            nextRefresh = 0f;
        }

        private void CycleArtFile(int direction)
        {
            if (emblemFiles.Length == 0)
            {
                NextMessage("No PNG files in the BoscaliSummer\\Emblems config folder.", StudioTone.Warn);
                return;
            }
            int index = emblemFileIndex;
            if (index < 0) index = direction > 0 ? 0 : emblemFiles.Length - 1;
            else index = ((index + direction) % emblemFiles.Length + emblemFiles.Length) % emblemFiles.Length;
            settings.EmblemFile.Value = emblemFiles[index];
            NextMessage("Custom emblem art: " + System.IO.Path.GetFileName(emblemFiles[index]), StudioTone.Ok);
            nextRefresh = 0f;
        }

        private void ClearArtFile()
        {
            settings.EmblemFile.Value = string.Empty;
            NextMessage("Using the procedural emblem.", StudioTone.Ok);
            nextRefresh = 0f;
        }

        private void RemoveLocalProfileIf(string callsign)
        {
            if (string.Equals(settings.PilotProfile.Value, callsign, StringComparison.OrdinalIgnoreCase))
                settings.PilotProfile.Value = string.Empty;
        }

        private void NextMessage(string message, StudioTone tone)
        {
            studioMessage = message ?? string.Empty;
            studioMessageTone = tone;
            studioMessageUntil = Time.unscaledTime + 8f;
        }

        private string StudioStatusLine()
        {
            if (!WingLink.PilotStudioAvailable) return WingLink.PilotStudioUnavailableReason;
            if (!string.IsNullOrEmpty(studioMessage) && Time.unscaledTime < studioMessageUntil)
                return studioMessage;
            return studioSelected >= 0 && studioSelected < studioPilots.Count
                ? "Editing " + studioPilots[studioSelected].Callsign + "."
                : "Select a custom pilot or start a new draft.";
        }

        private static WingPilotRecord RecordFrom(PilotDraft draft) => new WingPilotRecord
        {
            Name = draft.Name,
            Callsign = draft.Callsign,
            DialogueTag = draft.DialogueTag,
            Background = draft.Background,
            Persona = draft.Persona,
            Xp = draft.Xp,
            Kills = draft.Kills,
            Sorties = draft.Sorties,
            HasPortrait = draft.HasPortrait,
            Body = draft.Body,
            Face = draft.Face,
            Hair = draft.Hair,
            Uniform = draft.Uniform,
            Accessory = draft.Accessory,
            Backdrop = draft.Backdrop,
        };

        private static PilotDraft DraftFrom(WingPilotRecord record) => new PilotDraft
        {
            Name = record.Name,
            Callsign = record.Callsign,
            DialogueTag = record.DialogueTag,
            Background = record.Background,
            Persona = record.Persona,
            Xp = record.Xp,
            Kills = record.Kills,
            Sorties = record.Sorties,
            HasPortrait = record.HasPortrait,
            Body = record.Body,
            Face = record.Face,
            Hair = record.Hair,
            Uniform = record.Uniform,
            Accessory = record.Accessory,
            Backdrop = record.Backdrop,
        };

        private static int BodyCount() => Math.Max(2, WingLink.PortraitBodyCount);
        private static int FaceCount() => Math.Max(1, WingLink.PortraitFaceCount);
        private static int HairCount() => Math.Max(1, WingLink.PortraitHairCount);
        private static int UniformCount() => Math.Max(1, WingLink.PortraitUniformCount);
        private static int BackdropCount() => Math.Max(1, WingLink.PortraitBackdropCount);

        private static int NextSeed() =>
            unchecked(Environment.TickCount ^ (Time.frameCount * 7919) ^ (int)Time.unscaledTime);
    }
}
