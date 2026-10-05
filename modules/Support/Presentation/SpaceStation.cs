using System;
using BoscaliSummer.Modules.Support.Domain.C2;
using BoscaliSummer.Modules.Support.Presentation.C2;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation
{
    /// <summary>
    /// The full-screen tasking station as one part: the C2 chrome (banner 18, header 40, session line, no tabs), the SPACE feed in its
    /// wide layout (warning bar, sensor frame, control column) and the C2 footer that carries the enemy intent and the words.
    /// The window hosts it; the offline harness builds the same part, so what renders is what ships.
    /// </summary>
    internal sealed class SpaceStation : AvPart
    {
        private const float Gap = 6f;
        private readonly float height;
        private readonly C2Chrome chrome;
        private readonly C2Footer footer;
        private string chromeKey = "", footerKey = "";

        public SpaceFeedPanel Panel { get; }

        public SpaceStation(RectTransform parent, ISpaceFeedActions actions, float width, float height, Action<AvPart> register)
        {
            this.height = height;
            Rect = AvLay.Child(parent, "Station");
            AvLay.Place(Rect, 0f, 0f, width, height);
            chrome = new C2Chrome(Rect, null, station: true);
            chrome.Place(new AvSlot(0f, 0f, width, C2Chrome.StationHeight));
            float contentY = C2Chrome.StationHeight + Gap, contentH = height - contentY - C2Footer.Height - Gap;
            Panel = new SpaceFeedPanel(Rect, actions, width, contentH, true);
            AvLay.Place(Panel.Rect, 0f, contentY, width, contentH);
            footer = new C2Footer(Rect);
            footer.Place(new AvSlot(0f, height - C2Footer.Height, width, C2Footer.Height));
            register?.Invoke(chrome);
            register?.Invoke(Panel);
            register?.Invoke(footer);
        }

        public override float Measure(float width) => height;

        /// <summary>Paints the station: the shared chrome, the panel and the C2 footer. Cheap when nothing changed.</summary>
        public void Paint(SpaceFeedView view, C2ChromeView c)
        {
            Panel.Paint(view);
            string key = string.Concat(c.Faction, "|", c.Alert, "|", c.Credit, "|", c.Callsign, "|", c.Session, "|", c.KeyRot, "|", c.Uplinks, "|",
                (int)c.UplinkTone, "|", c.Space, "|", c.Link ? "1" : "0");
            if (key != chromeKey)
            {
                chromeKey = key;
                bool alert = c.Alert.Length > 0;
                chrome.SetBanner(C2Words.Banner(c.Faction, C2Area.Orbital), alert ? AvState.Danger : AvState.Caution);
                chrome.SetHeader("ORBITAL SUPPORT", "TASKING STATION", alert ? C2Words.Fit(c.Alert, 18) : null);
                chrome.SetLedger(c.Credit);
                chrome.SetSession(c.Callsign, c.Session, c.KeyRot, c.Uplinks, c.UplinkTone, c.Space, c.Link);
            }
            string slab = view.WordsTone == AvState.Danger ? "NEG" : view.WordsTone == AvState.Caution ? "WARN" : view.WordsTone == AvState.Ready ? "READY" : "INT";
            string fkey = slab + "|" + (int)view.WordsTone + "|" + view.Words;
            if (fkey != footerKey)
            {
                footerKey = fkey;
                footer.Set(slab, view.WordsTone, view.Words);
            }
        }
    }
}
