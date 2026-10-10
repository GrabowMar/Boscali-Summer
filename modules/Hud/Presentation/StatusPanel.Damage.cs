using BoscaliSummer.Modules.Hud.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // Damage cautions (2026-10-10): a row of up to four lamp tiles that stays lit as long as the condition does, where the
    // vanilla damage silhouette fades after ten seconds. FIRE (flashing warning), ENG OUT, FUEL LEAK, STR n%, ENG DMG; the
    // row takes no room while nothing is damaged. The rules are HudDamage's.
    internal sealed partial class StatusPanel
    {
        private readonly TextMeshProUGUI[] damageText = new TextMeshProUGUI[HudDamage.MaxLamps];
        private readonly Lamp[] damageLamps = new Lamp[HudDamage.MaxLamps];
        private int damageCount;

        private void BuildDamage()
        {
            for (int i = 0; i < damageText.Length; i++)
            {
                damageText[i] = Text("Damage" + i, TextAlignmentOptions.Center, legendSize, TextOverflowModes.Overflow);
                Shrink(damageText[i], legendSize);
                damageText[i].gameObject.SetActive(false);
            }
        }

        private void DestroyDamage()
        {
            for (int i = 0; i < damageText.Length; i++) damageText[i] = null;
            damageCount = 0;
        }

        /// <summary>Only a lamp that flashes (FIRE) goes off on the dark beat; a warning that merely persists (ENG OUT, STR) stays lit.</summary>
        private bool DamageLit(Lamp lamp) => !(lamp.Flash && !f.FlashOn);

        private void PresentDamage()
        {
            float u = f.U, tileW = HudBoardLayout.TileWidth(f.G.Width, u);
            for (int i = 0; i < damageText.Length; i++)
            {
                bool on = i < damageCount;
                Show(damageText[i], on);
                if (!on) continue;
                Lamp lamp = damageLamps[i];
                if (damageText[i].text != lamp.Legend) damageText[i].text = lamp.Legend;
                damageText[i].color = LampInk(lamp, DamageLit(lamp), false);
                Place(damageText[i].rectTransform, HudBoardLayout.TileX(f.G.Width, u, i), f.G.Damage.Y, tileW, HudBoardLayout.TileH * u);
                beat |= lamp.Flash;
                signature = signature * 31 + (int)lamp.State * 5 + lamp.Legend.GetHashCode();
            }
        }

        private void PaintDamage(AvQuadBuffer b)
        {
            float u = f.U, tileW = HudBoardLayout.TileWidth(f.G.Width, u), h = HudBoardLayout.TileH * u;
            float y1 = f.G.Height - f.G.Damage.Y;
            for (int i = 0; i < damageCount; i++)
                LampTile(b, damageLamps[i], DamageLit(damageLamps[i]), false, HudBoardLayout.TileX(f.G.Width, u, i), y1, tileW, h);
        }
    }
}
