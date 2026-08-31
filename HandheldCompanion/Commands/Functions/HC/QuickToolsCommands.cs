using HandheldCompanion.Helpers;
using System;
using System.Windows;
using System.Windows.Threading;

namespace HandheldCompanion.Commands.Functions.HC
{
    [Serializable]
    public class QuickToolsCommands : FunctionCommands
    {
        public int PageIndex { get; set; } = 0;

        public QuickToolsCommands()
        {
            base.Name = Properties.Resources.Hotkey_quickTools;
            base.Description = Properties.Resources.Hotkey_quickToolsDesc;
            base.Glyph = "\uEC7A";
            base.OnKeyUp = true;
        }

        public override void Execute(bool isKeyDown, bool isKeyUp, bool isBackground)
        {
            // WPF UI Removed
            base.Execute(isKeyDown, isKeyUp, false);
        }

        public override bool IsToggled => false;

        public override object Clone()
        {
            QuickToolsCommands commands = new()
            {
                commandType = this.commandType,
                Name = this.Name,
                Description = this.Description,
                Glyph = this.Glyph,
                OnKeyUp = this.OnKeyUp,
                OnKeyDown = this.OnKeyDown
            };

            return commands;
        }

        public override void Dispose()
        {
            base.Dispose();
        }
    }
}
