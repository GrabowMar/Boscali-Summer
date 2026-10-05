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
    // LOADOUT's AIRFRAME row (◂ word · n / N ▸) and TEMPLATE chips: pick an airframe, pick a template, NEW · COPY · DELETE (two-press).
    internal sealed partial class WmcLoadout
    {
        private WmcPickRow airRow;
        private WmcChipRow templateRow;
        private AvControl create, copy, delete;
        private readonly List<string> templateIds = new List<string>();
        private readonly List<string> templateNames = new List<string>();
        private ConfirmGate deleteGate = new ConfirmGate();

        private void BuildAirframeRow(AvFlow f)
        {
            airRow = f.Add(new WmcPickRow(f.Content, "AIRFRAME", true, StepAirframe, null));
            ids.Add("lo.airframe.prev", airRow.Prev);
            ids.Add("lo.airframe.next", airRow.Next);
            // The 0.9 tile pager's ids answer the same arrows.
            ids.Add("lo.tiles.prev", airRow.Prev);
            ids.Add("lo.tiles.next", airRow.Next);
        }

        private void RefreshAirframeRow()
        {
            int at = airframe != null ? airframes.IndexOf(airframe) : -1;
            if (airframe == null)
                airRow.Set(airframes.Count > 0 ? "NO AIRFRAME" : "NO AIRFRAME · none with readable hardpoints is in this game", "", null, Color.clear);
            else
                airRow.Set(SupplyWords.Code(airframe.code, airframe.unitName) + " " + SupplyWords.Name(airframe.unitName, airframe.code).ToUpperInvariant(),
                    at >= 0 ? "· " + (at + 1) + " / " + airframes.Count : "", IconFactory.Aircraft(airframe), Color.clear);
            airRow.SetEnabled(airframes.Count > 1, airframes.Count > 1 ? "Edit the previous or next airframe." : "Only one airframe has readable hardpoints.");
        }

        private void StepAirframe(int dir)
        {
            if (airframes.Count == 0) return;
            int at = airframe != null ? airframes.IndexOf(airframe) : -1;
            airframe = airframes[((at < 0 ? 0 : at + dir) % airframes.Count + airframes.Count) % airframes.Count];
            hpResetPending = true;
            deleteGate = new ConfirmGate();
            WmcPanel.Instance?.Refresh();
        }

        // ---------------------------------------------------------------- the template bar

        private void BuildTemplateBar(AvFlow f)
        {
            templateRow = f.Add(new WmcChipRow(f.Content, "TEMPLATE", TemplateNames.PerAirframe, PickTemplate,
                new AvControl.Spec("NEW", New, AvButtonStyle.Default, AvIcon.Plus),
                new AvControl.Spec("COPY", Copy, AvButtonStyle.Default, AvIcon.LayersSubtract),
                new AvControl.Spec("DELETE", Delete, AvButtonStyle.Danger, AvIcon.X)));
            create = templateRow.Action(0);
            copy = templateRow.Action(1);
            delete = templateRow.Action(2);
            ids.Add("lo.template", CycleTemplate);
            for (int i = 0; i < TemplateNames.PerAirframe; i++) ids.Add("lo.template" + i, templateRow.Choice(i));
            ids.Add("lo.new", create);
            ids.Add("lo.copy", copy);
            ids.Add("lo.delete", delete);
        }

        private void RefreshTemplateBar(bool asking)
        {
            templateIds.Clear();
            templateNames.Clear();
            int pick = -1;
            if (airframe != null)
                foreach (LoadoutTemplateRecord t in WingLoadoutTemplates.For(airframe))
                {
                    if (current != null && t.Id == current.Id) pick = templateIds.Count;
                    templateIds.Add(t.Id);
                    templateNames.Add(t.Name);
                }
            templateRow.SetChoices(templateNames, pick);
            string tip = airframe == null ? "Pick an airframe first." : "Pick the template to edit.";
            for (int i = 0; i < templateNames.Count; i++) templateRow.Choice(i).Help = tip;
            int count = templateIds.Count;
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

        private void PickTemplate(int i)
        {
            if (airframe == null || i < 0 || i >= templateIds.Count) return;
            editing[airframe.jsonKey] = templateIds[i];
            deleteGate = new ConfirmGate();
            hpResetPending = true;
            WmcPanel.Instance?.Refresh();
        }

        /// <summary>The next template of this airframe (for automation: the 0.9 picker's id).</summary>
        private void CycleTemplate()
        {
            if (templateIds.Count == 0) return;
            int at = current != null ? templateIds.IndexOf(current.Id) : -1;
            PickTemplate((at + 1) % templateIds.Count);
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
            hpResetPending = true;
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
            hpResetPending = true;
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
            hpResetPending = true;
            WingToast.Show(LoadoutWords.Deleted(t.Name, wasFit));
            WmcPanel.Instance?.Refresh();
        }
    }
}
