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
    // LOADOUT's airframe tiles and template bar: pick an airframe, pick a template, NEW · COPY · DELETE (two-press).
    internal sealed partial class WmcLoadout
    {
        private WmcAirframeGrid tiles;
        private int tilePage;
        private AvControl picker, create, copy, delete;
        private AvPopup popup;
        private readonly List<AvPopupEntry> entries = new List<AvPopupEntry>();
        private readonly List<string> entryIds = new List<string>();
        private ConfirmGate deleteGate = new ConfirmGate();

        private void BuildTiles(AvFlow f)
        {
            f.Section(AvIcon.Plane, "AIRFRAME");
            tiles = f.Add(new WmcAirframeGrid(f.Content, ids, BezelLayout.TileCols, BezelLayout.TileRows, BezelLayout.LoadoutTileH, false, "lo.tile",
                "lo.tiles.", PickTile, TurnTiles));
        }

        private void RefreshTiles()
        {
            int per = tiles.PerPage;
            tilePage = Pages.Clamp(tilePage, airframes.Count, per);
            tiles.SetPage(tilePage, Pages.Count(airframes.Count, per));
            tiles.ShowEmpty(airframes.Count > 0 ? null : "NO AIRFRAME · none with readable hardpoints is in this game");
            int first = Pages.First(tilePage, per);
            for (int s = 0; s < per; s++)
            {
                int k = first + s;
                if (k >= airframes.Count)
                {
                    tiles.Hide(s);
                    continue;
                }
                AircraftDefinition d = airframes[k];
                bool sel = ReferenceEquals(d, airframe);
                int count = WingLoadoutTemplates.CountFor(d);
                int key;
                unchecked
                {
                    key = d.GetHashCode() * 31 + (sel ? 1 : 0) + count * 7;
                }
                if (!tiles.NeedsBind(s, key)) continue;
                string n = AvNum.Fixed(count, 0);
                tiles.Bind(s, IconFactory.Aircraft(d), SupplyWords.Code(d.code, d.unitName), SupplyWords.Name(d.unitName, d.code), null, "",
                    sel ? "live" : count > 0 ? "info" : "inert", sel, true,
                    d.unitName + " · " + (count == 1 ? "1 template" : n + " templates"));
            }
        }

        private void PickTile(int slot)
        {
            int k = Pages.First(tilePage, tiles.PerPage) + slot;
            if (k < 0 || k >= airframes.Count) return;
            airframe = airframes[k];
            hpResetPending = true;
            deleteGate = new ConfirmGate();
            WmcPanel.Instance?.Refresh();
        }

        private void TurnTiles(int dir)
        {
            tilePage = Pages.Clamp(tilePage + dir, airframes.Count, tiles.PerPage);
            WmcPanel.Instance?.Refresh();
        }

        // ---------------------------------------------------------------- the template bar

        private void BuildTemplateBar(AvFlow f)
        {
            f.Section(AvIcon.ListDetails, "TEMPLATE");
            picker = f.Buttons(new AvControl.Spec(LoadoutWords.Picker(null), OpenPicker, AvButtonStyle.Default, AvIcon.ListDetails)).Controls[0];
            ids.Add("lo.template", picker);
            AvControl[] bar = f.Buttons(new AvControl.Spec("NEW", New, AvButtonStyle.Default, AvIcon.Plus),
                new AvControl.Spec("COPY", Copy, AvButtonStyle.Default, AvIcon.LayersSubtract),
                new AvControl.Spec("DELETE", Delete, AvButtonStyle.Danger, AvIcon.X)).Controls;
            create = bar[0];
            copy = bar[1];
            delete = bar[2];
            ids.Add("lo.new", create);
            ids.Add("lo.copy", copy);
            ids.Add("lo.delete", delete);
        }

        private void RefreshTemplateBar(bool asking)
        {
            int count = airframe != null ? WingLoadoutTemplates.CountFor(airframe) : 0;
            picker.Label = LoadoutWords.Picker(current?.Name);
            picker.Interactable = count > 0;
            picker.Help = count > 0 ? "Pick the template to edit." : airframe == null ? "Pick an airframe first." : "NEW starts a template.";
            string why = airframe == null ? "Pick an airframe first." : LoadoutWords.NewWhy(layout != null, count);
            create.Interactable = why == null;
            create.Help = why ?? "Start a template from this airframe's standard stores (its gun, radar and hook included).";
            why = LoadoutWords.CopyWhy(current != null, count);
            copy.Interactable = why == null;
            copy.Help = why ?? "Copy this template under the next free name.";
            why = LoadoutWords.DeleteWhy(current != null);
            delete.Interactable = why == null;
            delete.Help = why ?? "Delete this template (press twice); aircraft in the air keep their fit.";
            delete.Label = LoadoutWords.DeleteLabel(asking);
            delete.Latched = asking;
        }

        private void OpenPicker()
        {
            if (airframe == null) return;
            entries.Clear();
            entryIds.Clear();
            string fit = WingRequisition.FitOf(airframe);
            foreach (LoadoutTemplateRecord t in WingLoadoutTemplates.For(airframe))
            {
                entries.Add(new AvPopupEntry(t.Name, t.Id == fit ? "SUPPLY FIT" : "", current != null && t.Id == current.Id));
                entryIds.Add(t.Id);
            }
            if (entries.Count == 0) return;
            popup.Show(WmcPopup.Area(flow.Content, picker.Rect, entries.Count), entries, PickTemplate);
        }

        private void PickTemplate(int i)
        {
            if (airframe == null || i < 0 || i >= entryIds.Count) return;
            editing[airframe.jsonKey] = entryIds[i];
            deleteGate = new ConfirmGate();
            hpResetPending = true;
            WmcPanel.Instance?.Refresh();
        }

        private void New()
        {
            if (airframe == null || layout == null) return;
            // Review R5: a new template starts from the airframe's standard stores (an empty one dropped internal guns and radomes).
            var seed = new List<string>(layout.Sets);
            WingLoadoutCatalog.StandardKeys(airframe, seed);
            layout.Normalize(seed);
            LoadoutTemplateRecord t = WingLoadoutTemplates.Create(airframe, null, seed);
            if (t == null)
            {
                WingToast.Show(LoadoutWords.NewWhy(true, WingLoadoutTemplates.CountFor(airframe)) ?? "No template was made");
                return;
            }
            editing[airframe.jsonKey] = t.Id;
            WingToast.Show(t.Name + " started for " + airframe.unitName);
            WmcPanel.Instance?.Refresh();
        }

        private void Copy()
        {
            if (current == null) return;
            LoadoutTemplateRecord t = WingLoadoutTemplates.Duplicate(current);
            if (t == null)
            {
                WingToast.Show(LoadoutWords.CopyWhy(true, WingLoadoutTemplates.CountFor(airframe)) ?? "No copy was made");
                return;
            }
            editing[airframe.jsonKey] = t.Id;
            WingToast.Show(t.Name + " copied");
            WmcPanel.Instance?.Refresh();
        }

        /// <summary>Two presses; the second deletes and puts SUPPLY's fit back to AUTO wherever it named this template.</summary>
        private void Delete()
        {
            if (current == null) return;
            LoadoutTemplateRecord t = current;
            if (!deleteGate.Press(t.Id, Time.unscaledTime))
            {
                WingToast.Show(LoadoutWords.DeleteAsk(t.Name));
                WmcPanel.Instance?.Refresh();
                return;
            }
            WingLoadoutTemplates.Delete(t);
            bool wasFit = WingRequisition.DropFit(t.Id);
            editing.Remove(t.AirframeKey);
            WingToast.Show(LoadoutWords.Deleted(t.Name, wasFit));
            WmcPanel.Instance?.Refresh();
        }
    }
}
