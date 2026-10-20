using System;
using UnityEngine;

namespace BoscaliSummer.Core.Contracts
{
    /// <summary>The kaiju as another module (Spooktober's boss menu) sees it. Published by Kaiju; absent while that module is off.</summary>
    internal interface IKaijuBoss
    {
        /// <summary>Host only: raises the kaiju ahead of the local aircraft. The answer is a line for the UI.</summary>
        string Summon();

        /// <summary>Host only: sends the kaiju away (a dying one too). The answer is a line for the UI.</summary>
        string Dismiss();

        /// <summary>The body's root, alive or dying, on every peer; null when there is no kaiju.</summary>
        GameObject Body { get; }

        /// <summary>The body exists and is in its death action (clients see the kill this way).</summary>
        bool Dead { get; }

        /// <summary>Changes with every summoned kaiju (and on clients with every stream they join); -1 when there is no kaiju.</summary>
        int Generation { get; }

        /// <summary>Raised on the host when the kaiju is killed (not on a Dismiss).</summary>
        event Action Defeated;
    }
}
