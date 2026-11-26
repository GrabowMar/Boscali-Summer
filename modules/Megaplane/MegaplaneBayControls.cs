using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    internal enum FighterBayPhase : byte
    {
        Empty, Ready, Servicing, Launching, Airborne, Marshal, LineUp, Final, SoftCapture, Clearing, Stowing,
    }

    /// <summary>Host-derived operation states for the menu and HUD, independent of the bay's animation states.</summary>
    internal sealed class MegaplaneBayOperations : MonoBehaviour
    {
        private readonly byte[] phases = { byte.MaxValue, byte.MaxValue };

        internal FighterBayPhase Phase(MegaplaneBay bay, int slot)
        {
            if (bay.Carrier.IsServer) return ReadHost(bay, slot);
            if (phases[slot] != byte.MaxValue) return (FighterBayPhase)phases[slot];
            return bay.State[slot] switch
            {
                0 => FighterBayPhase.Ready,
                1 => FighterBayPhase.Launching,
                3 => FighterBayPhase.Marshal,
                4 => FighterBayPhase.Stowing,
                _ => FighterBayPhase.Empty,
            };
        }

        private static FighterBayPhase ReadHost(MegaplaneBay bay, int slot)
        {
            Aircraft fighter = bay.Released[slot];
            switch (bay.State[slot])
            {
                case 0: return !bay.HasMount(slot) ? FighterBayPhase.Empty :
                    Time.time < bay.ReadyAt[slot] ? FighterBayPhase.Servicing : FighterBayPhase.Ready;
                case 1: return FighterBayPhase.Launching;
                case 2:
                    if (fighter != null && fighter.TryGetComponent(out MegaplaneCapture departure) && departure.Departing)
                        return FighterBayPhase.Launching;
                    return MegaplaneCapture.CanGuide(fighter) && fighter.NetworkHQ == bay.Carrier.NetworkHQ && bay.HasMount(slot)
                        ? FighterBayPhase.Airborne : FighterBayPhase.Empty;
                case 3:
                    if (fighter == null || !fighter.TryGetComponent(out MegaplaneCapture capture)) return FighterBayPhase.Marshal;
                    return capture.Phase switch
                    {
                        1 => FighterBayPhase.LineUp,
                        2 => FighterBayPhase.Final,
                        3 => FighterBayPhase.SoftCapture,
                        4 => FighterBayPhase.Clearing,
                        _ => FighterBayPhase.Marshal,
                    };
                case 4: return FighterBayPhase.Stowing;
                default: return FighterBayPhase.Empty;
            }
        }

        internal void Remote(int slot, byte value) => phases[slot] = value;

        internal void Publish(MegaplaneBay bay, bool refresh)
        {
            if (!bay.Carrier.persistentID.IsValid) return;
            for (int slot = 0; slot < phases.Length; slot++)
            {
                byte value = (byte)ReadHost(bay, slot);
                if (!refresh && phases[slot] == value) continue;
                phases[slot] = value;
                MegaplaneNet.Broadcast(bay.Carrier.persistentID.Id, MegaplaneWire.OperationsIndex + slot, value);
            }
        }
    }

    internal sealed class MegaplaneBayControls : ICarrierBayControl
    {
        public bool TryRead(out CarrierBayStatus status)
        {
            status = default;
            if (!GameManager.GetLocalAircraft(out Aircraft carrier) || !TryBay(carrier, out var bay, out var operations)) return false;
            status = Read(bay, operations);
            return true;
        }

        public void Request(CarrierBayAction action)
        {
            if (GameManager.GetLocalAircraft(out Aircraft carrier)) Request(carrier, action);
        }

        internal static bool Request(Aircraft carrier, CarrierBayAction action)
        {
            if (!TryBay(carrier, out var bay, out var operations)) return false;
            CarrierBayStatus status = Read(bay, operations);
            string blocked = action switch
            {
                CarrierBayAction.Launch => status.LaunchBlocked,
                CarrierBayAction.Recall => status.RecallBlocked,
                _ => status.WaveOffBlocked,
            };
            if (blocked != null)
            {
                Notice(HudTone.Caution, blocked);
                return false;
            }

            bool sent = carrier.IsServer ? Execute(bay, action) : MegaplaneNet.Ask(carrier, action switch
            {
                CarrierBayAction.Launch => MegaplaneWire.DropVerb,
                CarrierBayAction.Recall => MegaplaneWire.RecallVerb,
                _ => MegaplaneWire.WaveOffVerb,
            });
            if (!sent) Notice(HudTone.Caution, "BAY COMMAND UNAVAILABLE");
            else if (action == CarrierBayAction.WaveOff) Notice(HudTone.Info, "WAVE OFF REQUESTED");
            return sent;
        }

        internal static bool Execute(MegaplaneBay bay, CarrierBayAction action) => action switch
        {
            CarrierBayAction.Launch => bay.Drop(),
            CarrierBayAction.Recall => bay.Recall() > 0,
            CarrierBayAction.WaveOff => bay.WaveOff() > 0,
            _ => false,
        };

        internal static bool TryBay(Aircraft carrier, out MegaplaneBay bay, out MegaplaneBayOperations operations)
        {
            bay = null;
            operations = null;
            return carrier != null && !carrier.disabled && carrier.TryGetComponent(out bay) && carrier.TryGetComponent(out operations);
        }

        internal static CarrierBayStatus Read(MegaplaneBay bay, MegaplaneBayOperations operations)
        {
            FighterBayPhase front = operations.Phase(bay, 0), aft = operations.Phase(bay, 1);
            bool approaching = Approaching(front) || Approaching(aft);
            bool launching = front == FighterBayPhase.Launching || aft == FighterBayPhase.Launching;
            bool cycle = launching ||
                front == FighterBayPhase.Stowing || aft == FighterBayPhase.Stowing;
            bool ready = front == FighterBayPhase.Ready || aft == FighterBayPhase.Ready;
            string launchBlocked = cycle || approaching ? "BAY CYCLE IN PROGRESS" : bay.DeckHoldReason;
            return new CarrierBayStatus
            {
                Slots = "FWD " + Label(front) + " / AFT " + Label(aft),
                LaunchBlocked = ready ? launchBlocked : "NO SERVICED FIGHTER",
                RecallBlocked = launching ? "FIGHTER CLEARING BAY" :
                    (front != FighterBayPhase.Airborne && aft != FighterBayPhase.Airborne) ||
                    (bay.Carrier.IsServer && !bay.CanRecall(0) && !bay.CanRecall(1)) ? "NO FIGHTERS TO RECALL" : null,
                WaveOffBlocked = approaching ? null : "NO ACTIVE APPROACH",
            };
        }

        internal static bool Approaching(FighterBayPhase phase) => phase >= FighterBayPhase.Marshal && phase <= FighterBayPhase.Clearing;

        internal static string Label(FighterBayPhase phase) => phase switch
        {
            FighterBayPhase.Ready => "READY",
            FighterBayPhase.Servicing => "SERVICE",
            FighterBayPhase.Launching => "LAUNCH",
            FighterBayPhase.Airborne => "AWAY",
            FighterBayPhase.Marshal => "MARSHAL",
            FighterBayPhase.LineUp => "LINE UP",
            FighterBayPhase.Final => "FINAL",
            FighterBayPhase.SoftCapture => "CAPTURE",
            FighterBayPhase.Clearing => "CLEARING",
            FighterBayPhase.Stowing => "STOWING",
            _ => "EMPTY",
        };

        private static void Notice(HudTone tone, string text)
        {
            if (ModuleServices.TryGet<IHudBoard>(out var board)) board.Notice("sys", tone, text);
        }
    }
}
