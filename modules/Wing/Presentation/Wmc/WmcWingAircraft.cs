using NOAvionics;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Core.Game;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>What the aircraft card shows: plain words and fractions (NaN is unknown), plus the parts for the damage map.</summary>
    internal sealed class AircraftFace
    {
        public string Title = "", Sub = "", FuelText = "", FuelSub = "", AmmoText = "", AmmoSub = "", HullText = "", HullSub = "";
        public string Radar = "", Alt = "", Task = "", Target = "", MapNote = "";
        public float Fuel = float.NaN, Ammo = float.NaN, Hull = float.NaN;
        public string FuelRail = "inert", AmmoRail = "inert", HullRail = "inert";
        public readonly PartDot[] Parts = new PartDot[PartMap.Max];
        public int PartCount;
    }

    /// <summary>The AIRCRAFT page's card: the aircraft's title, a top-view damage map (the real parts as dots over a drawn outline: whole,
    /// hit, lost), the FUEL (with bingo), AMMO and HULL gauges, and the RADAR · ALT · TASK · TARGET values.</summary>
    internal sealed class WingAircraftCard : AvPart
    {
        public const float H = 228f;
        private const float PadX = 10f, MapW = 140f, MapH = 130f, MapY = 30f, GaugeH = 38f;
        private static readonly string[] GaugeKeys = { "FUEL", "AMMO", "HULL" };
        private static readonly string[] ValueKeys = { "RADAR", "ALT", "TASK", "TARGET" };
        private readonly AvFrame frame;
        private readonly AvVector map;
        private readonly TMP_Text title, sub, mapNote;
        private readonly TMP_Text[] gaugeKey = new TMP_Text[3], gaugeValue = new TMP_Text[3], gaugeSub = new TMP_Text[3];
        private readonly Image[] track = new Image[3], fill = new Image[3];
        private readonly TMP_Text[] valueKey = new TMP_Text[4], value = new TMP_Text[4];
        private readonly AircraftFace face = new AircraftFace();
        private float width = 300f;

        public WingAircraftCard(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "AircraftCard");
            frame = AvFrame.Add(Rect, "Frame", default(AvChamfer));
            AvLay.Fill(frame.rectTransform);
            title = AvText.Make(Rect, "Title", AvTextRole.Label);
            AvText.Fit(title, false);
            sub = AvText.Make(Rect, "Sub", AvTextRole.Micro, "", TextAlignmentOptions.MidlineRight);
            AvText.Fit(sub, false);
            map = AvVector.Create(Rect, "DamageMap", 400);
            mapNote = AvText.Make(Rect, "MapNote", AvTextRole.Micro, "", TextAlignmentOptions.Center);
            AvText.Fit(mapNote, false);
            for (int i = 0; i < 3; i++)
            {
                gaugeKey[i] = AvText.Make(Rect, "GaugeKey" + i, AvTextRole.Micro, GaugeKeys[i]);
                AvText.Fit(gaugeKey[i], false);
                gaugeValue[i] = AvText.Make(Rect, "GaugeValue" + i, AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineRight);
                AvText.Fit(gaugeValue[i], false);
                track[i] = AvLay.Solid(Rect, "Track" + i, Color.clear);
                fill[i] = AvLay.Solid(Rect, "Fill" + i, Color.clear);
                gaugeSub[i] = AvText.Make(Rect, "GaugeSub" + i, AvTextRole.Micro);
                AvText.Fit(gaugeSub[i], false);
            }
            for (int i = 0; i < 4; i++)
            {
                valueKey[i] = AvText.Make(Rect, "ValueKey" + i, AvTextRole.Micro, ValueKeys[i]);
                AvText.Fit(valueKey[i], false);
                value[i] = AvText.Make(Rect, "Value" + i, AvTextRole.DataSmall);
                AvText.Fit(value[i], false);
            }
            Restyle();
        }

        public void Show(AircraftFace f)
        {
            face.Title = f.Title;
            face.Sub = f.Sub;
            face.Fuel = f.Fuel;
            face.Ammo = f.Ammo;
            face.Hull = f.Hull;
            face.FuelRail = f.FuelRail;
            face.AmmoRail = f.AmmoRail;
            face.HullRail = f.HullRail;
            face.PartCount = f.PartCount;
            Array.Copy(f.Parts, face.Parts, f.PartCount);
            AvText.Set(title, f.Title);
            AvText.Set(sub, f.Sub);
            AvText.Set(mapNote, f.MapNote);
            AvText.Set(gaugeValue[0], f.FuelText);
            AvText.Set(gaugeSub[0], f.FuelSub);
            AvText.Set(gaugeValue[1], f.AmmoText);
            AvText.Set(gaugeSub[1], f.AmmoSub);
            AvText.Set(gaugeValue[2], f.HullText);
            AvText.Set(gaugeSub[2], f.HullSub);
            AvText.Set(value[0], f.Radar);
            AvText.Set(value[1], f.Alt);
            AvText.Set(value[2], f.Task);
            AvText.Set(value[3], f.Target);
            PlaceFills();
            Restyle();
            DrawMap();
        }

        public override float Measure(float w) => H;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            width = s.W;
            AvLay.Place(title.rectTransform, PadX, 6f, s.W * 0.5f - PadX, 18f);
            AvLay.Place(sub.rectTransform, s.W * 0.5f, 6f, s.W * 0.5f - PadX, 18f);
            AvLay.Place(mapNote.rectTransform, PadX, MapY + MapH + 4f, MapW, 16f);
            float gx = PadX + MapW + 14f, gw = s.W - gx - PadX;
            for (int i = 0; i < 3; i++)
            {
                float y = MapY + i * (GaugeH + 4f);
                AvLay.Place(gaugeKey[i].rectTransform, gx, y, 44f, 16f);
                AvLay.Place(gaugeValue[i].rectTransform, gx + 44f, y, gw - 44f, 16f);
                AvLay.Place(track[i].rectTransform, gx, y + 17f, gw, 5f);
                AvLay.Place(gaugeSub[i].rectTransform, gx, y + 22f, gw, 16f);
            }
            float vy = MapY + MapH + 22f, cw = (s.W - 2f * PadX) * 0.5f;
            for (int i = 0; i < 4; i++)
            {
                float x = PadX + (i % 2) * cw, y = vy + (i / 2) * 18f;
                AvLay.Place(valueKey[i].rectTransform, x, y, 44f, 16f);
                AvLay.Place(value[i].rectTransform, x + 44f, y, cw - 48f, 16f);
            }
            PlaceFills();
            DrawMap();
        }

        private void PlaceFills()
        {
            float gx = PadX + MapW + 14f, gw = Mathf.Max(0f, width - gx - PadX);
            float[] f = { face.Fuel, face.Ammo, face.Hull };
            for (int i = 0; i < 3; i++)
            {
                float y = MapY + i * (GaugeH + 4f);
                float v = float.IsNaN(f[i]) ? 0f : Mathf.Clamp01(f[i]);
                AvLay.Place(fill[i].rectTransform, gx, y + 17f, gw * v, 5f);
            }
        }

        /// <summary>The outline, then every known part: a small dim tick when whole, an amber block when hit, a red cross when lost.</summary>
        private void DrawMap()
        {
            AvQuadBuffer b = map.Buffer;
            b.Clear();
            var ink = new WingInk(b, H);
            Color dim = AvStyleHost.FuiColor("frame", AvTheme.Frame), whole = WmcState.Color("live").WithAlpha(0.55f);
            float mx = PadX + 2f, my = MapY + 2f;
            WingSilhouette.Draw(ink, mx, my, MapW - 4f, MapH - 4f, dim);
            Color warn = WmcState.Color("warn"), danger = WmcState.Color("danger");
            for (int i = 0; i < face.PartCount; i++)
            {
                PartDot p = face.Parts[i];
                Vector2 at = WingSilhouette.Place(mx, my, MapW - 4f, MapH - 4f, p.X, p.Z);
                if (p.State == 0) ink.Rect(at.x - 1.5f, at.y - 1.5f, 3f, 3f, whole);
                else if (p.State == 1) ink.Rect(at.x - 3f, at.y - 3f, 6f, 6f, warn);
                else
                {
                    ink.Line(at.x - 4f, at.y - 4f, at.x + 4f, at.y + 4f, 1.6f, danger);
                    ink.Line(at.x - 4f, at.y + 4f, at.x + 4f, at.y - 4f, 1.6f, danger);
                }
            }
            map.Commit();
        }

        public override void Restyle()
        {
            AvStyle c = AvStyleHost.FuiStyle("card");
            frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.Surface), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
            title.color = AvStyleHost.FuiInk("row-name", AvTheme.TextPrimary);
            sub.color = AvInk.Dim;
            mapNote.color = AvInk.Dim;
            string[] rails = { face.FuelRail, face.AmmoRail, face.HullRail };
            Color trackColor = AvStyleHost.FuiColor("surface-inert", AvTheme.SurfaceInert);
            for (int i = 0; i < 3; i++)
            {
                gaugeKey[i].color = AvInk.Dim;
                gaugeValue[i].color = AvInk.Ink;
                gaugeSub[i].color = AvInk.Dim;
                track[i].color = trackColor;
                fill[i].color = WmcState.Color(rails[i]);
            }
            for (int i = 0; i < 4; i++)
            {
                valueKey[i].color = AvInk.Dim;
                value[i].color = AvInk.Ink;
            }
            DrawMap();
        }
    }

    /// <summary>The stores by station: a class swatch, the station and store name, and the count; two columns of up to four.</summary>
    internal sealed class WingStoresList : AvPart
    {
        public const int Rows = 8;
        private const float RowH = 20f, Gap = 3f;
        private readonly Image[] swatch = new Image[Rows];
        private readonly TMP_Text[] station = new TMP_Text[Rows], store = new TMP_Text[Rows], count = new TMP_Text[Rows];
        private readonly StoreClass[] classes = new StoreClass[Rows];
        private readonly TMP_Text none;
        private int shown;

        public WingStoresList(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "StoresList");
            for (int i = 0; i < Rows; i++)
            {
                swatch[i] = AvLay.Solid(Rect, "Swatch" + i, Color.clear);
                station[i] = AvText.Make(Rect, "St" + i, AvTextRole.Micro);
                AvText.Fit(station[i], false);
                store[i] = AvText.Make(Rect, "Store" + i, AvTextRole.Label);
                AvText.Fit(store[i], false);
                count[i] = AvText.Make(Rect, "Count" + i, AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineRight);
                AvText.Fit(count[i], false);
            }
            none = AvText.Make(Rect, "None", AvTextRole.ProseSmall, "NO STORES");
            Restyle();
        }

        public int Shown => shown;

        public void Show(in MemberDetail d)
        {
            int n = d.Stores == null ? 0 : Math.Min(Math.Min(d.StoreCount, d.Stores.Length), Rows);
            shown = n;
            for (int i = 0; i < Rows; i++)
            {
                bool on = i < n;
                swatch[i].gameObject.SetActive(on);
                station[i].gameObject.SetActive(on);
                store[i].gameObject.SetActive(on);
                count[i].gameObject.SetActive(on);
                if (!on) continue;
                StoreLine s = d.Stores[i];
                classes[i] = s.Class;
                AvText.Set(station[i], AvNum.Fixed(i + 1, 0));
                AvText.Set(store[i], WmcText.Cut(s.Name, 16));
                AvText.Set(count[i], s.Full > 0 && s.Ammo <= 0 ? "EMPTY" : AvNum.Fixed(s.Ammo, 0) + " / " + AvNum.Fixed(s.Full, 0));
            }
            none.gameObject.SetActive(n == 0);
            Restyle();
            Changed();
        }

        public static string ClassRail(StoreClass c)
        {
            switch (c)
            {
                case StoreClass.AirMissile: return "info";
                case StoreClass.StrikeMissile: return "warn";
                case StoreClass.Bomb: return "danger";
                case StoreClass.Gun: return "live";
                case StoreClass.Ecm: return "live";
                default: return "inert";
            }
        }

        public override float Measure(float width)
        {
            if (shown == 0) return 20f;
            int rows = (Math.Min(shown, Rows) + 1) / 2;
            return rows * (RowH + Gap) - Gap;
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            AvLay.Place(none.rectTransform, 0f, 0f, s.W, 20f);
            float cw = (s.W - 8f) * 0.5f;
            for (int i = 0; i < shown; i++)
            {
                int per = (shown + 1) / 2, col = i / per, row = i % per;
                float x = col * (cw + 8f), y = row * (RowH + Gap);
                AvLay.Place(swatch[i].rectTransform, x, y + 4f, 4f, 12f);
                AvLay.Place(station[i].rectTransform, x + 8f, y, 12f, RowH);
                AvLay.Place(store[i].rectTransform, x + 22f, y, cw - 22f - 64f, RowH);
                AvLay.Place(count[i].rectTransform, x + cw - 64f, y, 64f, RowH);
            }
        }

        public override void Restyle()
        {
            for (int i = 0; i < Rows; i++)
            {
                swatch[i].color = WmcState.Color(ClassRail(classes[i]));
                station[i].color = AvInk.Dim;
                store[i].color = AvInk.Ink;
                count[i].color = AvInk.Ink;
            }
            none.color = AvInk.Dim;
        }
    }

    /// <summary>The aircraft's pilot: portrait, callsign, rank · kills · status, and DOSSIER ›.</summary>
    internal sealed class WingPilotLine : AvPart
    {
        private const float PortraitW = 28f, H = 40f, ButtonW = 108f;
        private readonly AvFrame frame;
        private readonly Image portrait;
        private readonly TMP_Text callsign, sub;
        private WingPilot shown;
        private int look = int.MinValue;
        private int faction = int.MinValue;
        private bool set;

        public readonly AvControl Dossier;

        public WingPilotLine(RectTransform parent, Action open)
        {
            Rect = AvLay.Child(parent, "PilotLine");
            frame = AvFrame.Add(Rect, "Frame", default(AvChamfer));
            AvLay.Fill(frame.rectTransform);
            portrait = AvLay.Solid(Rect, "Portrait", Color.white);
            portrait.preserveAspect = true;
            callsign = AvText.Make(Rect, "Callsign", AvTextRole.Label);
            AvText.Fit(callsign, false);
            sub = AvText.Make(Rect, "Sub", AvTextRole.Micro);
            AvText.Fit(sub, false);
            Dossier = AvControl.Make(Rect, new AvControl.Spec("DOSSIER", open, AvButtonStyle.Quiet, AvIcon.ChevronRight, true));
            Restyle();
        }

        public void SetPilot(WingPilot p, string callsignText, string subText)
        {
            int currentFaction = p != null && p.PortraitFaction >= 0 ? p.PortraitFaction : PortraitFactions.Local;
            if (!set || !ReferenceEquals(p, shown) || look != WingPilotRoster.LookVersion || faction != currentFaction)
            {
                set = true;
                shown = p;
                look = WingPilotRoster.LookVersion;
                faction = currentFaction;
                portrait.sprite = p != null ? PilotPortrait.For(p) : null;
                portrait.enabled = portrait.sprite != null;
            }
            AvText.Set(callsign, callsignText);
            AvText.Set(sub, subText);
        }

        public override float Measure(float width) => H;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            AvLay.Place(portrait.rectTransform, 6f, 4f, PortraitW, H - 8f);
            float tx = PortraitW + 14f, tw = s.W - tx - ButtonW - 10f;
            AvLay.Place(callsign.rectTransform, tx, 3f, tw, 18f);
            AvLay.Place(sub.rectTransform, tx, 21f, tw, 16f);
            AvLay.Place(Dossier.Rect, s.W - ButtonW - 6f, 5f, ButtonW, H - 10f);
        }

        public override void Restyle()
        {
            AvStyle c = AvStyleHost.FuiStyle("card");
            frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.Surface), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
            callsign.color = AvStyleHost.FuiInk("row-name", AvTheme.TextPrimary);
            sub.color = AvInk.Dim;
            Dossier.Restyle();
        }
    }
}
