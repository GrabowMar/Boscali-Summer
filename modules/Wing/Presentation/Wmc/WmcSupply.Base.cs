using NOAvionics;
using System.Collections.Generic;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    // SUPPLY step 4 LAUNCH BASE: NEAREST or ANY FIELD, and the friendly fields a page at a time (only the ON button toggles one).
    internal sealed partial class WmcSupply
    {
        private sealed class BaseRow
        {
            public int Item;
            public AvControl Toggle;
        }

        private readonly Dictionary<AvRow, BaseRow> baseRows = new Dictionary<AvRow, BaseRow>(BezelLayout.BaseRows);
        private AvControl nearest, anyField;
        private AvSection baseSection;
        private AvList baseList;
        private WmcLines baseEmpty;
        private int baseChipKey = -1, baseKey = int.MinValue;
        private int modeKey = -1;

        private void BuildBase(AvFlow f)
        {
            baseSection = f.Section(AvIcon.BuildingBank, "4 · " + SupplyWords.BaseTitle, "");
            AvControl[] mode = f.Buttons(new AvControl.Spec("NEAREST", () => SetMode(LaunchMode.Nearest)),
                new AvControl.Spec("ANY FIELD", () => SetMode(LaunchMode.Any))).Controls;
            nearest = mode[0];
            anyField = mode[1];
            ids.Add("sup.base.nearest", nearest);
            ids.Add("sup.base.any", anyField);
            baseList = f.Add(new AvList(f.Content, ticker, BezelLayout.BaseRows, BindBase));
            WmcListPaging.Register(ids, "sup.bases.", baseList, BezelLayout.BaseRows);
            baseEmpty = f.Add(new WmcLines(f.Content, 1));
        }

        /// <summary>The mode, the step chip and this page of fields: each row's name, ON/OFF and status (READY, APRON, NO JETS,
        /// OFF; ON/OFF alone with no airframe picked), the field the requisition will use highlighted.</summary>
        private void RefreshBase()
        {
            List<Airbase> fields = WingRequisition.Fields;
            nearest.Latched = WingRequisition.Mode == LaunchMode.Nearest;
            anyField.Latched = WingRequisition.Mode == LaunchMode.Any;
            if ((client ? 1 : 0) != modeKey)
            {
                modeKey = client ? 1 : 0;
                nearest.Interactable = !client;
                anyField.Interactable = !client;
                nearest.Help = client ? ClientWhy : "Launch from the field picked on the radial when it is ON, else the nearest ON field.";
                anyField.Help = client ? ClientWhy : "Launch from the nearest ON field with a hangar ready for this airframe, else as NEAREST.";
            }
            int on = 0, key = fields.Count * 7919 + (client ? 1 : 0);
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
                }
            }
            int chip = on * 1000 + fields.Count;
            if (chip != baseChipKey)
            {
                baseChipKey = chip;
                StepCaption(baseSection, SupplyWords.BaseChip(on, fields.Count), on > 0 ? "live" : "warn");
                bool none = fields.Count == 0;
                baseEmpty.Set(0, none ? caller == null ? SupplyWords.NotFlyingBases : SupplyWords.NoField : "");
                relayout = true;
            }
            if (key == baseKey) return;
            baseKey = key;
            baseList.SetCount(fields.Count);
            relayout = true;
        }

        /// <summary>A field's row: its name, READY / APRON / NO JETS / OFF (ON / OFF with no airframe picked) and an ON toggle; the field
        /// the requisition will use is armed.</summary>
        private void BindBase(int item, AvRow row)
        {
            if (!baseRows.TryGetValue(row, out BaseRow r))
            {
                r = new BaseRow();
                BaseRow made = r;
                made.Toggle = row.AddTrailing(new AvControl.Spec("ON", () => ToggleBase(made.Item)));
                baseRows[row] = made;
            }
            r.Item = item;
            List<Airbase> fields = WingRequisition.Fields;
            Airbase f = item < fields.Count ? fields[item] : null;
            if (f == null) return;
            bool isOn = WingRequisition.IsOn(f), picked = ReferenceEquals(f, field);
            string status = selected == null ? (isOn ? "ON" : "OFF") : WingRequisition.Status(f, selected);
            AvState state = status == "READY" ? AvState.Ready : status == "NO JETS" || status == "OFF" ? AvState.Caution : isOn ? AvState.Info : AvState.Inert;
            row.Set(BaseName.Short(WingRequisition.NameOf(f), 40), picked ? "LAUNCH FIELD" : "", AvStates.Glyph(state) + status, state);
            row.Armed = picked;
            row.Help = picked ? "The field the requisition will launch from." : null;
            r.Toggle.Label = isOn ? "ON" : "OFF";
            r.Toggle.Latched = isOn;
            r.Toggle.Interactable = !client;
            r.Toggle.Help = client ? ClientWhy : isOn ? "Turn this field OFF: requisitions will not launch from it." : "Turn this field ON.";
            ids.Add("sup.base" + item % BezelLayout.BaseRows, r.Toggle);
        }

        private void SetMode(LaunchMode mode)
        {
            if (client) return;
            WingRequisition.Mode = mode;
            WmcPanel.Instance?.Refresh();
        }

        private void ToggleBase(int item)
        {
            List<Airbase> fields = WingRequisition.Fields;
            Airbase f = item >= 0 && item < fields.Count ? fields[item] : null;
            if (client || f == null) return;
            WingRequisition.SetOn(f, !WingRequisition.IsOn(f));
            WmcPanel.Instance?.Refresh();
        }
    }
}
