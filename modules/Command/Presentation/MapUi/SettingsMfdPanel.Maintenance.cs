using System;
using BoscaliSummer.Core.Config;
using NOAvionics;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    internal sealed partial class SettingsMfdPanel
    {
        private enum MaintenanceAction { None, Cache, Factory }
        private MaintenanceAction maintenanceAction;
        private string maintenanceMessage;
        private AvState maintenanceState;
        private Action refreshMaintenance;

        private void BuildMaintenance(AvFlow flow, int page)
        {
            flow.Section(AvIcon.Refresh, "MAINTENANCE", "THIS PC");
            var note = flow.Add(new AvNote(flow.Content));
            AvButtons actions = null, confirmation = null;
            actions = flow.Buttons(
                new AvControl.Spec("CLEAR CACHE", () => Request(MaintenanceAction.Cache)),
                new AvControl.Spec("FACTORY RESET", () => Request(MaintenanceAction.Factory), AvButtonStyle.Danger));
            confirmation = flow.Buttons(
                new AvControl.Spec("CONFIRM CLEAR", Complete, AvButtonStyle.Danger),
                new AvControl.Spec("CANCEL", CancelMaintenance, AvButtonStyle.Quiet));
            actions.Controls[0].Help = "Clear Boscali's generated files and unused renderer stacks. Saves and settings stay.";
            confirmation.Controls[1].Help = "Cancel without changing files or settings.";

            void Refresh()
            {
                bool allowed = HostAuthority() && configFile != null;
                if (maintenanceAction == MaintenanceAction.Factory && !allowed)
                {
                    maintenanceAction = MaintenanceAction.None;
                    maintenanceMessage = "Factory reset locked while connected to another host.";
                    maintenanceState = AvState.Caution;
                }
                actions.Controls[1].Interactable = allowed;
                actions.Controls[1].Help = allowed
                    ? "Restore Boscali settings on this PC, including host rules. Saves and saved presets stay. Restart the game afterwards."
                    : "Factory reset is available when you host. Disconnect from this server first.";
                bool pending = maintenanceAction != MaintenanceAction.None;
                actions.SetShown(!pending);
                confirmation.SetShown(pending);
                confirmation.Controls[0].Label = maintenanceAction == MaintenanceAction.Factory ? "CONFIRM RESET" : "CONFIRM CLEAR";
                confirmation.Controls[0].Help = maintenanceAction == MaintenanceAction.Factory
                    ? "Restore factory settings now. Restart the game afterwards."
                    : "Clear generated cache files and unused renderer stacks now.";
                note.Set(maintenanceAction == MaintenanceAction.Cache
                        ? "Clear generated cache and unused renderer stacks? Saves, saved presets and settings stay."
                        : maintenanceAction == MaintenanceAction.Factory
                            ? "Restore all Boscali settings, including host rules? Saves and saved presets stay. Restart the game afterwards."
                            : maintenanceMessage ?? "Clear generated cache or restore factory settings. Saves and saved presets stay.",
                    pending ? AvState.Caution : maintenanceState);
            }

            void Request(MaintenanceAction action)
            {
                if (action == MaintenanceAction.Factory && (!HostAuthority() || configFile == null)) return;
                maintenanceAction = action;
                Refresh();
            }

            void Complete()
            {
                MaintenanceAction action = maintenanceAction;
                CancelMaintenance(); // A second click cannot repeat the operation.
                if (action == MaintenanceAction.None) return;
                if (action == MaintenanceAction.Factory && (!HostAuthority() || configFile == null))
                {
                    maintenanceMessage = "Factory reset locked while connected to another host.";
                    maintenanceState = AvState.Caution;
                    Refresh();
                    return;
                }
                try
                {
                    MaintenanceResult result = action == MaintenanceAction.Cache
                        ? SettingsMaintenance.ClearCache()
                        : SettingsMaintenance.FactoryReset(configFile);
                    maintenanceMessage = result.Message;
                    maintenanceState = result.Success ? AvState.Ready : AvState.Caution;
                    Echo(result.Success ? action == MaintenanceAction.Cache ? "Cache cleared." : "Factory settings restored. Restart the game." : "Maintenance incomplete. See the result above.");
                    if (!result.Success) logger?.LogWarning("SET maintenance: " + result.Message);
                    Changed();
                }
                catch (Exception error)
                {
                    maintenanceMessage = "Maintenance failed. See the BepInEx log for details.";
                    maintenanceState = AvState.Danger;
                    logger?.LogWarning("SET maintenance failed: " + error);
                }
                refreshMaintenance?.Invoke();
            }

            refreshMaintenance = Refresh;
            Refresh();
            flow.Ticker.Add(page, AvTickRate.Slow, Refresh);
        }

        private void CancelMaintenance()
        {
            maintenanceAction = MaintenanceAction.None;
            refreshMaintenance?.Invoke();
        }
    }
}
