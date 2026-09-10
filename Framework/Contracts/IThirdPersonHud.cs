namespace BoscaliSummer.Framework.Contracts
{
    internal interface IThirdPersonHud
    {
        bool IsEnabled { get; }
        void Toggle();
    }
}
