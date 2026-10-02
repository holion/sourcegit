using System.Collections.Generic;

namespace SourceGit.Views
{
    public partial class Hotkeys : ChromelessWindow
    {
        public List<Models.ShortcutGroup> Groups
        {
            get;
        } = Models.Shortcuts.GetGroups();

        public Hotkeys()
        {
            CloseOnESC = true;
            InitializeComponent();
        }
    }
}
