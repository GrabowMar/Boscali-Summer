using TMPro;
using UnityEngine;

namespace BoscaliSummer.Framework.Contracts
{
    /// <summary>
    /// Theater SA content hosted as an OPS tab. Command implements this; Support mounts
    /// it. No sibling-feature import.
    /// </summary>
    internal interface ITheaterPage
    {
        bool Mount(RectTransform host, TMP_FontAsset font, float width, float height);
        void Unmount();
        void RefreshView();
    }
}
