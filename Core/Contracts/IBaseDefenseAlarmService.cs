namespace BoscaliSummer.Core.Contracts
{
    internal interface IBaseDefenseAlarmService
    {
        string ActiveAlertTicker { get; }
        bool IsBaseUnderAttack { get; }
    }
}
