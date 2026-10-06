using System;
using BoscaliSummer.Modules.Support.Domain.C2;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.C2
{
    /// <summary>
    /// The [5] BOARD tab: every live TASKED post as a claimable row, and the QUIET MODE switch for the post and intent notices.
    /// The page owns no policy: a row press is handed to <c>press</c> (wired to <c>CallsController.PressTasked</c>, the same path as
    /// the ORBIT TASKED rows) and the toggle to <c>toggleQuiet</c> (the existing QuietNotices setting). Painted from the same
    /// <see cref="SpaceFeedView"/> the ORBIT page paints. There is no contributions box: the client holds no per-player income or
    /// contribution figure (only the credit balance and its changes).
    /// </summary>
    internal sealed class BoardPage : C2PageBase
    {
        private const float Gap = 6f, MinRowH = 46f, MaxRowH = 58f, NoticeBody = 44f;
        private static readonly string[] HowTo =
        {
            C2Board.Empty,
            "An operator with MARKs presses TRANSMIT on the ORBIT page.",
            "OVERLORD posts from revealed contacts, and CYBER / SOF help, when no one works them.",
            "CLAIM arms a post, EXECUTE fires it: the first claim wins."
        };

        private readonly int rowCount;
        private readonly float rowH;
        private readonly Action<int> press;
        private readonly Action toggleQuiet;
        private readonly C2Box board, notices;
        private readonly C2Row[] rows;
        private readonly int[] ids;
        private readonly TMP_Text[] howTo = new TMP_Text[4];
        private readonly Image[] ghost;
        private readonly TMP_Text[] ghostText;
        private readonly TMP_Text quietWord, quietSub;
        private readonly AvControl quietButton;
        private bool quietShown, quietKnown, emptyShown;
        private int titleLive = -1, titleStale = -1, titleHidden = -1;

        public BoardPage(RectTransform parent, float width, float height, Action<int> press, Action toggleQuiet, Action<AvPart> register)
            : base(width, height, register)
        {
            this.press = press;
            this.toggleQuiet = toggleQuiet;
            rowCount = full ? C2Board.Rows896 : C2Board.Rows596;
            rows = new C2Row[rowCount];
            ids = new int[rowCount];
            ghost = new Image[rowCount];
            ghostText = new TMP_Text[rowCount];

            float gap = full ? Gap : 4f;
            float noticeH = full ? C2Box.HeaderH + NoticeBody : 0f;
            // Budget: gap, console (at least 2 lines), gap, posts box, gap, [notices box, gap]. The rows grow first (up to MaxRowH);
            // what is left goes to the console, and the notices box is anchored to the bottom of the page, so no void is left under it.
            float chrome = gap + gap + gap + (full ? noticeH + gap : 0f) + C2Box.HeaderH + 2f;
            float minConsole = C2ConsoleView.HeightFor(full ? 6 : 2);
            rowH = Mathf.Clamp(Mathf.Floor((height - chrome - minConsole) / rowCount) - 2f, MinRowH, full ? MaxRowH : MinRowH);
            float boardBody = rowCount * (rowH + 2f) + 2f;
            float boardH = C2Box.HeaderH + boardBody;
            float spare = height - chrome - boardBody - minConsole;
            int lines = Mathf.Clamp((full ? 6 : 2) + Mathf.FloorToInt(spare / C2ConsoleView.LineH), 2, full ? 14 : 10);
            float consoleH = C2ConsoleView.HeightFor(lines);

            console = Make(new C2ConsoleView(parent, lines));
            float y = gap;
            console.Place(new AvSlot(0f, y, width, consoleH));
            y += consoleH + gap;

            board = Make(new C2Box(parent, "LIVE POSTS"));
            board.BodyHeight = boardBody;
            board.Place(new AvSlot(0f, y, width, boardH));
            y += boardH + gap;
            for (int i = 0; i < rowCount; i++)
            {
                int slot = i;
                rows[i] = Make(new C2Row(board.Body, rowH, true));
                rows[i].Place(new AvSlot(1f, 1f + i * (rowH + 2f), width - 4f, rowH));
                rows[i].Primary.Clicked += () => this.press?.Invoke(ids[slot]);
                rows[i].Rect.gameObject.SetActive(false);
                // An empty slot is drawn as a ghost row, so the box is always full and never leaves an empty band under the posts.
                ghost[i] = AvLay.Solid(board.Body, "Ghost" + i, Color.clear);
                ghost[i].raycastTarget = false;
                AvLay.Place(ghost[i].rectTransform, 1f, 1f + i * (rowH + 2f), width - 4f, rowH);
                ghostText[i] = C2Kit.Mono(board.Body, "GhostText" + i, 10f, TextAlignmentOptions.MidlineLeft, false, 2f);
                C2Kit.Place(ghostText[i], 14f, 1f + i * (rowH + 2f), width - 28f, rowH);
                OpsText.Set(ghostText[i], "OPEN SLOT");
                ghost[i].gameObject.SetActive(false);
                ghostText[i].gameObject.SetActive(false);
            }
            for (int i = 0; i < howTo.Length; i++)
            {
                howTo[i] = C2Kit.Mono(board.Body, "HowTo" + i, 10.5f, TextAlignmentOptions.MidlineLeft);
                C2Kit.Place(howTo[i], 10f, Mathf.Floor((boardBody - howTo.Length * 20f) * 0.5f) + i * 20f, width - 24f, 18f);
                howTo[i].gameObject.SetActive(false);
            }

            if (full)
            {
                notices = Make(new C2Box(parent, "NOTICES"));
                notices.BodyHeight = NoticeBody;
                notices.SetMeta("TASKED · INTENT · OPERATIONS");
                notices.Place(new AvSlot(0f, height - gap - noticeH, width, noticeH));
                quietWord = C2Kit.Mono(notices.Body, "QuietWord", 12f, TextAlignmentOptions.MidlineLeft, true, 1f);
                quietSub = C2Kit.Mono(notices.Body, "QuietSub", 10f, TextAlignmentOptions.MidlineLeft);
                C2Kit.Place(quietWord, 8f, 3f, width - 130f, 18f);
                C2Kit.Place(quietSub, 8f, 21f, width - 130f, 16f);
                quietButton = AvControl.Make(notices.Body, new AvControl.Spec("QUIET", () => this.toggleQuiet?.Invoke(), AvButtonStyle.Default, AvIcon.X));
                quietButton.SingleLine();
                AvLay.Place(quietButton.Rect, width - 2f - 8f - 104f, 7f, 104f, 26f);
            }
            Restyle();
        }

        /// <summary>The words the footer shows for this page.</summary>
        public string Words { get; private set; } = "";
        public AvState WordsTone { get; private set; } = AvState.Ready;

        public void Paint(SpaceFeedView v, bool quiet)
        {
            console.Show(v.Console);
            int shown = Mathf.Min(v.CardCount, rowCount);
            int hidden = Mathf.Max(0, v.CardCount - shown);
            if (v.PostsLive != titleLive || v.PostsStale != titleStale || hidden != titleHidden)
            {
                titleLive = v.PostsLive; titleStale = v.PostsStale; titleHidden = hidden;
                board.SetTitle(C2Board.Title(v.PostsLive, v.PostsStale));
                board.SetMeta(C2Board.Meta(hidden));
            }
            for (int i = 0; i < rowCount; i++)
            {
                FeedCardView c = i < shown ? v.Cards[i] : default;
                bool on = c.Present;
                if (rows[i].Rect.gameObject.activeSelf != on) rows[i].Rect.gameObject.SetActive(on);
                bool slot = !on && shown > 0;
                if (ghost[i].gameObject.activeSelf != slot) { ghost[i].gameObject.SetActive(slot); ghostText[i].gameObject.SetActive(slot); }
                if (!on) continue;
                ids[i] = c.PostId;
                rows[i].Armed = c.Armed;
                rows[i].Set(string.IsNullOrEmpty(c.Slab) ? "SPC" : c.Slab, c.Title, c.Chip, AvState.Ready, c.Sub, c.Price, c.State, c.Tone, c.Button,
                    c.Armed ? AvButtonStyle.Danger : c.Enabled && c.Tone == AvState.Ready ? AvButtonStyle.Primary : AvButtonStyle.Default, c.Enabled);
                rows[i].SetHelp(c.Detail, "");
                if (rows[i].Primary.Help != c.Detail) rows[i].Primary.Help = c.Detail;
            }
            PaintEmpty(shown == 0);
            PaintQuiet(quiet);

            string said = v.Words ?? "";
            bool ready = said.Length == 0 || said.StartsWith("READY", StringComparison.Ordinal);
            Words = ready ? C2Board.Ready(v.CardCount > 0) : said;
            WordsTone = ready || v.WordsTone == AvState.Inert ? AvState.Ready : v.WordsTone;
        }

        private void PaintEmpty(bool none)
        {
            if (none == emptyShown && none == howTo[0].gameObject.activeSelf) return;
            emptyShown = none;
            for (int i = 0; i < howTo.Length; i++)
            {
                howTo[i].gameObject.SetActive(none);
                if (none) OpsText.Set(howTo[i], C2Kit.FitTo(howTo[i], HowTo[i], width - 24f));
            }
            RestyleEmpty();
        }

        private void PaintQuiet(bool quiet)
        {
            if (quietButton == null || (quietKnown && quiet == quietShown)) return;
            quietKnown = true;
            quietShown = quiet;
            OpsText.Set(quietWord, C2Kit.FitTo(quietWord, C2Board.QuietWord(quiet), width - 130f));
            OpsText.Set(quietSub, C2Kit.FitTo(quietSub, quiet ? "POSTS + INTENT SILENT · WARNINGS STAY AUDIBLE" : "A NEW POST OR INTENT SHOWS A LINE AND A CHIME", width - 130f));
            quietButton.Label = C2Board.QuietButton(quiet);
            quietButton.SetIcon(quiet ? AvIcon.CircleCheck : AvIcon.X);
            quietButton.Latched = quiet;
            quietButton.Help = C2Board.QuietHelp(quiet);
            Restyle();
        }

        private void RestyleEmpty()
        {
            for (int i = 0; i < howTo.Length; i++) if (howTo[i] != null) howTo[i].color = OpsInk.Muted;
            if (ghost != null)
                for (int i = 0; i < ghost.Length; i++)
                {
                    if (ghost[i] != null) ghost[i].color = OpsInk.Inert;
                    if (ghostText[i] != null) ghostText[i].color = OpsInk.Dim;
                }
        }

        public void Restyle()
        {
            RestyleEmpty();
            if (quietWord != null) quietWord.color = quietShown ? OpsInk.Word(AvState.Caution) : OpsInk.Ink;
            if (quietSub != null) quietSub.color = OpsInk.Dim;
            quietButton?.Restyle();
        }
    }
}
