using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Domain.Pure;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>What the aircrew ID card shows (plain words; the card holds no game types).</summary>
    internal sealed class IdCardFace
    {
        public string Callsign = "", Name = "", RankWord = "", Radio = "", Number = "", Bio = "";
        public WingRank Rank;
        public Sprite Portrait;
    }

    /// <summary>STUDIO's live aircrew identification card: the squadron insignia, the file number, the portrait, CALLSIGN · NAME · RANK · RADIO
    /// and a barcode drawn from the pilot's identity, and the bio's first lines. It repaints as the draft is edited, so the pilot reads as a
    /// person while it is made. Words and bars only; nothing is stored.</summary>
    internal sealed class WingIdCard : AvPart
    {
        private const float PadX = 10f, PortraitW = 72f, PortraitH = 96f, HeadH = 40f, FieldsX = 96f, KeyW = 62f, RowH = 16f, Pitch = 20f;
        private static readonly string[] Keys = { "CALLSIGN", "NAME", "RANK", "RADIO" };
        private readonly AvFrame frame, portraitBox;
        private readonly Image portrait;
        private readonly AvVector art;
        private readonly TMP_Text title, subtitle, number, bio;
        private readonly TMP_Text[] key = new TMP_Text[4], value = new TMP_Text[4];
        private readonly IdCardFace face = new IdCardFace();
        private readonly int[] bars = new int[PersonnelFile.Bars];
        private float width = 300f;

        public WingIdCard(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "IdCard");
            frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(8f));
            AvLay.Fill(frame.rectTransform);
            art = AvVector.Create(Rect, "Art", 200);
            title = AvText.Make(Rect, "Title", AvTextRole.Label, SquadronWords.SquadronTitle);
            AvText.Fit(title, false);
            subtitle = AvText.Make(Rect, "Subtitle", AvTextRole.Micro, "AIRCREW IDENTIFICATION · NOT TRANSFERABLE");
            AvText.Fit(subtitle, false);
            number = AvText.Make(Rect, "Number", AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineRight);
            AvText.Fit(number, false);
            portraitBox = AvFrame.Add(Rect, "PortraitBox", default(AvChamfer));
            portrait = AvLay.Solid(Rect, "Portrait", Color.white);
            portrait.preserveAspect = true;
            for (int i = 0; i < 4; i++)
            {
                key[i] = AvText.Make(Rect, "Key" + Keys[i], AvTextRole.Micro, Keys[i]);
                AvText.Fit(key[i], false);
                value[i] = AvText.Make(Rect, "Value" + Keys[i], AvTextRole.Label);
                AvText.Fit(value[i], false);
            }
            bio = AvText.Make(Rect, "Bio", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
            Restyle();
        }

        public void Show(IdCardFace f)
        {
            bool bioMoved = bio.text != f.Bio;
            face.Callsign = f.Callsign;
            face.Name = f.Name;
            face.Rank = f.Rank;
            face.RankWord = f.RankWord;
            face.Radio = f.Radio;
            face.Number = f.Number;
            face.Bio = f.Bio;
            face.Portrait = f.Portrait;
            WmcKit.Set(number, f.Number);
            WmcKit.Set(value[0], f.Callsign.Length > 0 ? "\"" + f.Callsign + "\"" : WmcText.Unknown);
            WmcKit.Set(value[1], f.Name.Length > 0 ? f.Name.ToUpperInvariant() : WmcText.Unknown);
            WmcKit.Set(value[2], f.RankWord);
            WmcKit.Set(value[3], f.Radio);
            WmcKit.Set(bio, f.Bio);
            portrait.sprite = f.Portrait;
            portrait.enabled = f.Portrait != null;
            PersonnelFile.Pattern(f.Callsign, f.Name, bars);
            Draw();
            if (bioMoved) Changed();
        }

        private float BioW(float w) => w - 2f * PadX;

        private float BioHeight(float w) => bio.text.Length == 0 ? 0f : AvText.Height(bio, BioW(w)) + 4f;

        public override float Measure(float w) => HeadH + PortraitH + 16f + BioHeight(w) + 6f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            width = s.W;
            AvLay.Place(title.rectTransform, PadX + 40f, 6f, s.W - PadX - 40f - 96f, 16f);
            AvLay.Place(subtitle.rectTransform, PadX + 40f, 22f, s.W - PadX - 40f - 10f, 16f);
            AvLay.Place(number.rectTransform, s.W - PadX - 90f, 6f, 90f, 16f);
            float py = HeadH + 4f;
            AvLay.Place(portraitBox.rectTransform, PadX, py, PortraitW, PortraitH);
            AvLay.Place(portrait.rectTransform, PadX + 1f, py + 1f, PortraitW - 2f, PortraitH - 2f);
            float vw = s.W - FieldsX - PadX - KeyW;
            for (int i = 0; i < 4; i++)
            {
                float y = py + i * Pitch;
                AvLay.Place(key[i].rectTransform, FieldsX, y, KeyW, RowH);
                // The rank's value leaves room for its chevrons.
                float dx = i == 2 ? 22f : 0f;
                AvLay.Place(value[i].rectTransform, FieldsX + KeyW + dx, y, vw - dx, RowH);
            }
            float by = py + PortraitH + 6f;
            AvLay.Place(bio.rectTransform, PadX, by, BioW(s.W), BioHeight(s.W) > 0f ? BioHeight(s.W) - 4f : 0f);
            Draw();
        }

        /// <summary>The insignia, the rank chevrons by the rank word and the barcode under the fields.</summary>
        private void Draw()
        {
            AvQuadBuffer b = art.Buffer;
            b.Clear();
            float h = Measure(width);
            var ink = new WingInk(b, h);
            ink.Insignia(PadX, 4f, 28f, 32f, AvStyleHost.FuiColor("frame", AvTheme.Frame), WmcState.Color("warn"));
            ink.Rect(PadX, HeadH - 2f, width - 2f * PadX, 1f, AvStyleHost.FuiColor("hairline", AvTheme.Hairline));
            float py = HeadH + 4f;
            ink.Rank(FieldsX + KeyW + 8f, py + 2 * Pitch + RowH * 0.5f, 14f, face.Rank, WingRankColor.Of(face.Rank));
            ink.Barcode(FieldsX, py + 4 * Pitch + 2f, 12f, bars, bars.Length, 1.6f, WingRankColor.Dim);
            art.Commit();
        }

        public override void Restyle()
        {
            AvStyle c = AvStyleHost.FuiStyle("card");
            frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.Surface), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
            portraitBox.Paint(AvStyleHost.FuiColor("surface-inert", AvTheme.SurfaceInert), AvStyleHost.FuiColor("frame", AvTheme.Frame));
            title.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary);
            subtitle.color = WingRankColor.Dim;
            number.color = WingRankColor.Dim;
            bio.color = WingRankColor.Dim;
            for (int i = 0; i < 4; i++)
            {
                key[i].color = WingRankColor.Dim;
                value[i].color = WingRankColor.Ink;
            }
            Draw();
        }
    }
}
