using System;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Events.Presentation
{
    /// <summary>
    /// IPoliticalDispatch (client-local): the cabinet card Nation hands over. EVN DISPATCH draws it under FOR THE CABINET with its answers;
    /// the shared event notice presents it over the big map. Nothing is drawn over the cockpit.
    /// </summary>
    internal sealed class CabinetDispatch : IPoliticalDispatch
    {
        public CabinetCard Card { get; private set; }
        private Action<int> pick;

        public void Show(CabinetCard card, Action<int> onPick)
        {
            Card = card;
            pick = onPick;
        }

        public void Close(int serial)
        {
            if (Card != null && Card.Serial == serial) { Card = null; pick = null; }
        }

        /// <summary>A local press: a request to the host, which takes the first answer.</summary>
        public void Choose(int option)
        {
            if (Card != null && Card.Chosen < 0) pick?.Invoke(option);
        }

        /// <summary>An open card nobody has answered yet.</summary>
        public bool Waiting => Card != null && Card.Chosen < 0;
    }

}
