using NOAvionics;
using System.Collections.Generic;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    // SUPPLY step ④ LAUNCH BASE: a LAUNCH FROM row (NEAREST or ANY FIELD), and the friendly fields a page at a time (only the ON button
    // toggles one): a dot in the field's state colour, its name, its distance, READY / APRON / NO JETS / SHORT (runway too short for the
    // airframe at the chosen fuel) and ON / OFF.
    internal sealed partial class WmcSupply
    {
        private WmcTuneRow modeRow;
        private WmcHeadBar baseHead;
        private WmcStripBlock baseList;
        private WmcLines baseEmpty;
        private int basePage, baseChipKey = -1, baseKey = int.MinValue;
        private int modeKey = -1;
        /// <summary>The field each row shows, bound at refresh (the list refills and re-sorts by distance): a click acts on what was shown.</summary>
        private readonly Airbase[] baseDef = new Airbase[BezelLayout.BaseRows];

        private void BuildBase(AvFlow f)
        {
            baseHead = f.Add(new WmcHeadBar(f.Content, AvIcon.BuildingBank, "4 · " + SupplyWords.BaseTitle));
            modeRow = f.Add(new WmcTuneRow(f.Content, "LAUNCH FROM", new[] { "NEAREST", "ANY FIELD" },
                i => SetMode(i == 0 ? LaunchMode.Nearest : LaunchMode.Any)));
            baseList = f.Add(new WmcStripBlock(f.Content, "Fields", BezelLayout.BaseRows, TurnBases, (row, k) => ToggleBase(row),
                new AvControl.Spec("ON", null, AvButtonStyle.Default)));
            baseEmpty = f.Add(new WmcLines(f.Content, 1));
        }

        /// <summary>The mode, the step caption and this page of fields: each row's name, distance, status (READY, APRON, NO JETS, OFF; ON/OFF
        /// alone with no airframe picked) and ON toggle, the field the requisition will use marked.</summary>
        private void RefreshBase()
        {
            List<Airbase> fields = WingRequisition.Fields;
            modeRow.Show(WingRequisition.Mode == LaunchMode.Nearest ? 0 : 1, -1, false);
            if ((client ? 1 : 0) != modeKey)
            {
                modeKey = client ? 1 : 0;
                AvControl nearest = modeRow.Options[0], anyField = modeRow.Options[1];
                nearest.Interactable = !client;
                anyField.Interactable = !client;
                nearest.Help = client ? ClientWhy : "Launch from the field picked on the radial when it is ON, else the nearest ON field.";
                anyField.Help = client ? ClientWhy : "Launch from the nearest ON field with a hangar ready for this airframe, else as NEAREST.";
            }
            int per = BezelLayout.BaseRows, pages = Pages.Count(fields.Count, per);
            basePage = Pages.Clamp(basePage, fields.Count, per);
            int on = 0, key = fields.Count * 7919 + (client ? 1 : 0) + basePage * 17;
            unchecked
            {
                foreach (Airbase f in fields)
                {
                    if (f == null) continue;
                    bool isOn = WingRequisition.IsOn(f);
                    if (isOn) on++;
                    string status = selected == null ? (isOn ? "ON" : "OFF") : WingRequisition.Status(f, selected);
                    key = key * 31 + f.GetHashCode() + (isOn ? 3 : 0) + (ReferenceEquals(f, field) ? 5 : 0) + (status != null ? status.GetHashCode() * 7 : 0)
                        + (selected != null ? 11 : 0);
                    if (caller != null) key += Mathf.RoundToInt((f.transform.position - caller.transform.position).magnitude / 1000f) * 13;
                }
            }
            int chip = on * 1000 + fields.Count;
            if (chip != baseChipKey)
            {
                baseChipKey = chip;
                baseHead.SetCaption(StepCaption(SupplyWords.BaseChip(on, fields.Count), on > 0 ? "live" : "warn"));
                bool none = fields.Count == 0;
                baseEmpty.Set(0, none ? caller == null ? SupplyWords.NotFlyingBases : SupplyWords.NoField : "");
                relayout = true;
            }
            if (key == baseKey) return;
            baseKey = key;
            int first = basePage * per;
            for (int s = 0; s < per; s++)
            {
                Airbase f = first + s < fields.Count ? fields[first + s] : null;
                baseDef[s] = f;
                if (f == null) baseList.Hide(s);
                else BindBase(s, f);
            }
            baseList.SetPaging(basePage, pages, (first + 1) + "–" + Mathf.Min(fields.Count, first + per) + " OF " + fields.Count);
            relayout = true;
        }

        /// <summary>A field's strip: a dot in its state colour, its name, how far it is, READY / APRON / NO JETS / OFF (ON / OFF with no
        /// airframe picked) and an ON toggle; the field the requisition will use is marked.</summary>
        private void BindBase(int slot, Airbase f)
        {
            if (f == null)
            {
                baseList.Hide(slot);
                return;
            }
            bool isOn = WingRequisition.IsOn(f), picked = ReferenceEquals(f, field);
            string status = selected == null ? (isOn ? "ON" : "OFF") : WingRequisition.Status(f, selected);
            AvState state = status == "READY" ? AvState.Ready : status == "NO JETS" || status == "OFF" || status == "SHORT" ? AvState.Caution
                : isOn ? AvState.Info : AvState.Inert;
            string km = caller != null ? SupplyWords.FieldDistance((f.transform.position - caller.transform.position).magnitude / 1000f) : "";
            string sub = (km.Length > 0 ? km : "") + (picked ? (km.Length > 0 ? " · " : "") + "LAUNCH FIELD" : "");
            baseList.Set(slot, "●", state, BaseName.Short(WingRequisition.NameOf(f), 40), sub, -1f, AvStates.Glyph(state) + status, state,
                picked ? AvState.Ready : state);
            AvControl toggle = baseList.Button(slot, 0);
            toggle.Label = isOn ? "ON" : "OFF";
            toggle.Latched = isOn;
            toggle.Interactable = !client;
            toggle.Help = client ? ClientWhy : isOn ? "Turn this field OFF: requisitions will not launch from it." : "Turn this field ON.";
        }

        private void TurnBases(int dir)
        {
            basePage = Pages.Clamp(basePage + dir, WingRequisition.Fields.Count, BezelLayout.BaseRows);
            baseKey = int.MinValue;
            WmcPanel.Instance?.Refresh();
        }

        private void SetMode(LaunchMode mode)
        {
            if (client) return;
            WingRequisition.Mode = mode;
            WmcPanel.Instance?.Refresh();
        }

        private void ToggleBase(int slot)
        {
            Airbase f = slot >= 0 && slot < baseDef.Length ? baseDef[slot] : null;
            if (client || f == null) return;
            WingRequisition.SetOn(f, !WingRequisition.IsOn(f));
            WmcPanel.Instance?.Refresh();
        }
    }
}
