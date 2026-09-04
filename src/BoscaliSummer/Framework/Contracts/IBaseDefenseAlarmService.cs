namespace BoscaliSummer.Framework.Contracts
{
    internal interface IBaseDefenseAlarmService
    {
        string ActiveAlertTicker { get; }
        bool IsBaseUnderAttack { get; }
    }
}
