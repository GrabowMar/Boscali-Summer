using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Progression.Configuration;
using BoscaliSummer.Modules.Progression.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Progression.Presentation
{
    /// <summary>
    /// The player's own insignia for another module's card: their PNG when one is set, otherwise the generated
    /// design on one reusable canvas. The stored text is parsed only when it changes, so a read is two string compares.
    /// </summary>
    internal sealed class SquadronInsignia : ISquadronInsignia
    {
        private readonly ProgressionSettings settings;
        private readonly EmblemCanvas canvas = new EmblemCanvas();
        private string file, text;
        private Sprite fileSprite;
        private EmblemDesign design = EmblemDesign.Default;

        public SquadronInsignia(ProgressionSettings settings) => this.settings = settings;

        public Sprite Current
        {
            get
            {
                string path = settings.EmblemFile.Value;
                if (!string.Equals(path, file))
                {
                    file = path;
                    fileSprite = EmblemRenderer.File(path);
                }
                if (fileSprite != null) return fileSprite;
                string wanted = settings.Emblem.Value;
                if (!string.Equals(wanted, text))
                {
                    text = wanted;
                    design = EmblemDesign.Parse(wanted, EmblemDesign.Default);
                }
                return canvas.Draw(design);
            }
        }
    }
}
