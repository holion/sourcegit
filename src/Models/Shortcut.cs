using System;
using System.Collections.Generic;
using System.Text;

using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.Models
{
    public enum ShortcutCategory
    {
        Global,
        Repository,
        NestedRepositories,
        Diff,
        MergeConflict,
        InteractiveRebase,
    }

    public class ShortcutGroup(string header, List<Shortcut> items)
    {
        public string Header { get; } = header;
        public List<Shortcut> Items { get; } = items;
    }

    public class Shortcut : ObservableObject
    {
        public string Id { get; }
        public ShortcutCategory Category { get; }
        public string DescriptionKey { get; }
        public KeyGesture Default { get; }

        public string Description => App.Text(DescriptionKey);
        public string DefaultDisplayText => Format(Default);
        public string DisplayText => Format(_gesture);
        public bool IsCustomized => !Equals(_gesture, Default);

        public KeyGesture Gesture
        {
            get => _gesture;
            set
            {
                if (SetProperty(ref _gesture, value))
                {
                    OnPropertyChanged(nameof(DisplayText));
                    OnPropertyChanged(nameof(IsCustomized));
                }
            }
        }

        public bool HasConflict
        {
            get => _hasConflict;
            set => SetProperty(ref _hasConflict, value);
        }

        public Shortcut(string id, ShortcutCategory category, string descriptionKey, KeyGesture defaultGesture)
        {
            Id = id;
            Category = category;
            DescriptionKey = descriptionKey;
            Default = defaultGesture;
            _gesture = defaultGesture;
        }

        public bool Matches(KeyEventArgs e)
        {
            return _gesture != null &&
                e.KeyModifiers == _gesture.KeyModifiers &&
                NormalizeKey(e.Key) == NormalizeKey(_gesture.Key);
        }

        /// <summary>
        ///     Matches the gesture with `extra` modifiers held down as well. Never matches when the gesture already
        ///     uses them.
        /// </summary>
        public bool Matches(KeyEventArgs e, KeyModifiers extra)
        {
            return _gesture != null &&
                (_gesture.KeyModifiers & extra) == 0 &&
                e.KeyModifiers == (_gesture.KeyModifiers | extra) &&
                NormalizeKey(e.Key) == NormalizeKey(_gesture.Key);
        }

        public void Reset()
        {
            Gesture = Default;
        }

        public static string Format(KeyGesture gesture)
        {
            if (gesture == null)
                return string.Empty;

            var builder = new StringBuilder();
            var mods = gesture.KeyModifiers;
            if (OperatingSystem.IsMacOS())
            {
                if (mods.HasFlag(KeyModifiers.Meta))
                    builder.Append("⌘+");
                if (mods.HasFlag(KeyModifiers.Control))
                    builder.Append("⌃+");
                if (mods.HasFlag(KeyModifiers.Alt))
                    builder.Append("⌥+");
                if (mods.HasFlag(KeyModifiers.Shift))
                    builder.Append("⇧+");
            }
            else
            {
                if (mods.HasFlag(KeyModifiers.Control))
                    builder.Append("Ctrl+");
                if (mods.HasFlag(KeyModifiers.Alt))
                    builder.Append("Alt+");
                if (mods.HasFlag(KeyModifiers.Shift))
                    builder.Append("Shift+");
                if (mods.HasFlag(KeyModifiers.Meta))
                    builder.Append(OperatingSystem.IsWindows() ? "Win+" : "Super+");
            }

            builder.Append(FormatKey(gesture.Key));
            return builder.ToString();
        }

        public static string Serialize(KeyGesture gesture)
        {
            if (gesture == null)
                return string.Empty;

            var builder = new StringBuilder();
            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Control))
                builder.Append("Ctrl+");
            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Alt))
                builder.Append("Alt+");
            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Shift))
                builder.Append("Shift+");
            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Meta))
                builder.Append("Meta+");

            builder.Append(gesture.Key.ToString());
            return builder.ToString();
        }

        public static bool TryDeserialize(string data, out KeyGesture gesture)
        {
            gesture = null;
            if (string.IsNullOrEmpty(data))
                return true;

            var parts = data.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0 || !Enum.TryParse<Key>(parts[^1], out var key) || key == Key.None)
                return false;

            var mods = KeyModifiers.None;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                switch (parts[i])
                {
                    case "Ctrl":
                        mods |= KeyModifiers.Control;
                        break;
                    case "Alt":
                        mods |= KeyModifiers.Alt;
                        break;
                    case "Shift":
                        mods |= KeyModifiers.Shift;
                        break;
                    case "Meta":
                        mods |= KeyModifiers.Meta;
                        break;
                    default:
                        return false;
                }
            }

            gesture = new KeyGesture(key, mods);
            return true;
        }

        public static bool IsModifierKey(Key key)
        {
            return key is Key.LeftCtrl or Key.RightCtrl
                or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift
                or Key.LWin or Key.RWin
                or Key.None;
        }

        private static Key NormalizeKey(Key key)
        {
            if (key >= Key.NumPad0 && key <= Key.NumPad9)
                return Key.D0 + (key - Key.NumPad0);

            return key;
        }

        private static string FormatKey(Key key)
        {
            if (key >= Key.D0 && key <= Key.D9)
                return ((char)('0' + (key - Key.D0))).ToString();

            return key switch
            {
                Key.Enter => "Enter",
                Key.Escape => "Esc",
                Key.Back => "Backspace",
                Key.PageUp => "PageUp",
                Key.PageDown => "PageDown",
                Key.OemComma => ",",
                Key.OemPeriod => ".",
                Key.OemPlus => "=",
                Key.OemMinus => "-",
                Key.OemTilde => "`",
                Key.OemQuestion => "/",
                Key.OemSemicolon => ";",
                Key.OemQuotes => "'",
                Key.OemOpenBrackets => "[",
                Key.OemCloseBrackets => "]",
                Key.OemPipe => "\\",
                _ => key.ToString(),
            };
        }

        private KeyGesture _gesture;
        private bool _hasConflict = false;
    }

    public static class Shortcuts
    {
        private static readonly bool s_isMacOS = OperatingSystem.IsMacOS();
        private static readonly KeyModifiers s_cmd = s_isMacOS ? KeyModifiers.Meta : KeyModifiers.Control;

        public static List<Shortcut> All { get; } = [];

        public static event Action Changed;

        // Global
        public static readonly Shortcut OpenPreferences = Add("OpenPreferences", ShortcutCategory.Global, "Hotkeys.Global.OpenPreferences", Key.OemComma, s_cmd);
        public static readonly Shortcut OpenHotkeys = Add("OpenHotkeys", ShortcutCategory.Global, "Hotkeys", Key.F1);
        public static readonly Shortcut NewTab = Add("NewTab", ShortcutCategory.Global, "Hotkeys.Global.NewTab", Key.T, s_cmd);
        public static readonly Shortcut CloseTab = Add("CloseTab", ShortcutCategory.Global, "Hotkeys.Global.CloseTab", Key.W, s_cmd);
        public static readonly Shortcut GotoPrevTab = s_isMacOS
            ? Add("GotoPrevTab", ShortcutCategory.Global, "Hotkeys.Global.GotoPrevTab", Key.Left, s_cmd | KeyModifiers.Alt)
            : Add("GotoPrevTab", ShortcutCategory.Global, "Hotkeys.Global.GotoPrevTab", Key.Tab, s_cmd | KeyModifiers.Shift);
        public static readonly Shortcut GotoNextTab = s_isMacOS
            ? Add("GotoNextTab", ShortcutCategory.Global, "Hotkeys.Global.GotoNextTab", Key.Right, s_cmd | KeyModifiers.Alt)
            : Add("GotoNextTab", ShortcutCategory.Global, "Hotkeys.Global.GotoNextTab", Key.Tab, s_cmd);
        public static readonly Shortcut Clone = Add("Clone", ShortcutCategory.Global, "Hotkeys.Global.Clone", Key.R, s_cmd);
        public static readonly Shortcut OpenLocalRepository = Add("OpenLocalRepository", ShortcutCategory.Global, "Hotkeys.Global.OpenLocalRepository", Key.L, s_cmd);
        public static readonly Shortcut ShowWorkspaceMenu = Add("ShowWorkspaceMenu", ShortcutCategory.Global, "Hotkeys.Global.ShowWorkspaceDropdownMenu", Key.P, s_cmd | KeyModifiers.Alt);
        public static readonly Shortcut SwitchTab = Add("SwitchTab", ShortcutCategory.Global, "Hotkeys.Global.SwitchTab", Key.P, s_cmd);
        public static readonly Shortcut ZoomIn = Add("ZoomIn", ShortcutCategory.Global, "Hotkeys.Global.ZoomIn", Key.OemPlus, s_cmd);
        public static readonly Shortcut ZoomOut = Add("ZoomOut", ShortcutCategory.Global, "Hotkeys.Global.ZoomOut", Key.OemMinus, s_cmd);
        public static readonly Shortcut OpenTerminal = Add("OpenTerminal", ShortcutCategory.Global, "Welcome.OpenTerminal", Key.OemTilde, KeyModifiers.Control);
        public static readonly Shortcut Search = Add("Search", ShortcutCategory.Global, "Hotkeys.Global.Search", Key.F, s_cmd);
        public static readonly Shortcut Quit = Add("Quit", ShortcutCategory.Global, "Quit", Key.Q, s_cmd);

        // Repository
        public static readonly Shortcut ViewHistories = Add("ViewHistories", ShortcutCategory.Repository, "Hotkeys.Repo.ViewDashboard", Key.D1, s_cmd);
        public static readonly Shortcut ViewChanges = Add("ViewChanges", ShortcutCategory.Repository, "Hotkeys.Repo.ViewChanges", Key.D2, s_cmd);
        public static readonly Shortcut ViewStashes = Add("ViewStashes", ShortcutCategory.Repository, "Hotkeys.Repo.ViewStashes", Key.D3, s_cmd);
        public static readonly Shortcut ToggleSearchCommits = Add("ToggleSearchCommits", ShortcutCategory.Repository, "Hotkeys.Repo.ToggleSearchCommits", Key.F, s_cmd | KeyModifiers.Shift);
        public static readonly Shortcut OpenCommandPalette = Add("OpenCommandPalette", ShortcutCategory.Repository, "Hotkeys.Repo.OpenCommandPalette", Key.P, s_cmd | KeyModifiers.Shift);
        public static readonly Shortcut Commit = Add("Commit", ShortcutCategory.Repository, "Hotkeys.Repo.Commit", Key.Enter, s_cmd);
        public static readonly Shortcut CommitWithAutoStage = Add("CommitWithAutoStage", ShortcutCategory.Repository, "Hotkeys.Repo.CommitWithAutoStage", Key.Enter, s_cmd | KeyModifiers.Shift);
        public static readonly Shortcut CommitAndPush = Add("CommitAndPush", ShortcutCategory.Repository, "Hotkeys.Repo.CommitAndPush", Key.Enter, s_cmd | KeyModifiers.Alt);
        public static readonly Shortcut CreateBranch = Add("CreateBranch", ShortcutCategory.Repository, "Hotkeys.Repo.CreateBranch", Key.B, s_cmd);
        public static readonly Shortcut CreateBranchFromCommit = Add("CreateBranchFromCommit", ShortcutCategory.Repository, "Hotkeys.Repo.CreateBranchFromCommit", Key.B, s_cmd | KeyModifiers.Shift);
        public static readonly Shortcut CreateTagFromCommit = Add("CreateTagFromCommit", ShortcutCategory.Repository, "Hotkeys.Repo.CreateTagFromCommit", Key.T, s_cmd | KeyModifiers.Shift);
        public static readonly Shortcut Fetch = Add("Fetch", ShortcutCategory.Repository, "Hotkeys.Repo.Fetch", Key.Down, s_cmd);
        public static readonly Shortcut Pull = Add("Pull", ShortcutCategory.Repository, "Hotkeys.Repo.Pull", Key.Down, s_cmd | KeyModifiers.Shift);
        public static readonly Shortcut Push = Add("Push", ShortcutCategory.Repository, "Hotkeys.Repo.Push", Key.Up, s_cmd | KeyModifiers.Shift);
        public static readonly Shortcut OpenInFileManager = Add("OpenInFileManager", ShortcutCategory.Repository, "Repository.Explore", Key.E, s_cmd);
        public static readonly Shortcut OpenInVSCode = Add("OpenInVSCode", ShortcutCategory.Repository, "Hotkeys.Repo.OpenInVSCode", Key.E, s_cmd | KeyModifiers.Shift);
        public static readonly Shortcut ToggleCommitDetailPanel = Add("ToggleCommitDetailPanel", ShortcutCategory.Repository, "Hotkeys.Repo.ToggleCommitDetailPanel", Key.J, s_cmd);
        public static readonly Shortcut OpenCommitDetailStandalone = Add("OpenCommitDetailStandalone", ShortcutCategory.Repository, "HistoriesDetailsStandalone", Key.N, s_cmd);
        public static readonly Shortcut GoToParent = Add("GoToParent", ShortcutCategory.Repository, "Hotkeys.Repo.GoToParent", Key.Down, KeyModifiers.Alt);
        public static readonly Shortcut GoToChild = Add("GoToChild", ShortcutCategory.Repository, "Hotkeys.Repo.GoToChild", Key.Up, KeyModifiers.Alt);
        public static readonly Shortcut OpenFileWithDefaultEditor = Add("OpenFileWithDefaultEditor", ShortcutCategory.Repository, "Hotkeys.Repo.OpenFileWithDefaultEditor", Key.O, s_cmd);
        public static readonly Shortcut Refresh = Add("Refresh", ShortcutCategory.Repository, "Hotkeys.Repo.Refresh", Key.F5);

        // Nested repositories
        public static readonly Shortcut FetchAllRepositories = Add("FetchAllRepositories", ShortcutCategory.NestedRepositories, "NestedRepos.FetchAll", Key.F, s_cmd | KeyModifiers.Alt | KeyModifiers.Shift);
        public static readonly Shortcut PullAllRepositories = Add("PullAllRepositories", ShortcutCategory.NestedRepositories, "NestedRepos.PullAll", Key.Down, s_cmd | KeyModifiers.Alt | KeyModifiers.Shift);
        public static readonly Shortcut PushAllRepositories = Add("PushAllRepositories", ShortcutCategory.NestedRepositories, "NestedRepos.PushAll", Key.Up, s_cmd | KeyModifiers.Alt | KeyModifiers.Shift);
        public static readonly Shortcut RefreshAllRepositories = Add("RefreshAllRepositories", ShortcutCategory.NestedRepositories, "NestedRepos.Refresh", Key.F5, KeyModifiers.Shift);
        public static readonly Shortcut SelectPrevRepository = Add("SelectPrevRepository", ShortcutCategory.NestedRepositories, "Hotkeys.NestedRepos.SelectPrev", Key.Up, KeyModifiers.Alt | KeyModifiers.Shift);
        public static readonly Shortcut SelectNextRepository = Add("SelectNextRepository", ShortcutCategory.NestedRepositories, "Hotkeys.NestedRepos.SelectNext", Key.Down, KeyModifiers.Alt | KeyModifiers.Shift);

        // Diff
        public static readonly Shortcut GotoFirstChange = Add("GotoFirstChange", ShortcutCategory.Diff, "Diff.First", Key.Home, s_cmd | KeyModifiers.Alt);
        public static readonly Shortcut GotoPrevChange = Add("GotoPrevChange", ShortcutCategory.Diff, "Diff.Prev", Key.Up, s_cmd | KeyModifiers.Alt);
        public static readonly Shortcut GotoNextChange = Add("GotoNextChange", ShortcutCategory.Diff, "Diff.Next", Key.Down, s_cmd | KeyModifiers.Alt);
        public static readonly Shortcut GotoLastChange = Add("GotoLastChange", ShortcutCategory.Diff, "Diff.Last", Key.End, s_cmd | KeyModifiers.Alt);
        public static readonly Shortcut OpenExternalMergeTool = Add("OpenExternalMergeTool", ShortcutCategory.Diff, "Diff.UseMerger", Key.D, s_cmd | KeyModifiers.Shift);
        public static readonly Shortcut StageChunk = Add("StageChunk", ShortcutCategory.Diff, "Hunk.Stage", Key.S, s_cmd);
        public static readonly Shortcut UnstageChunk = Add("UnstageChunk", ShortcutCategory.Diff, "Hunk.Unstage", Key.U, s_cmd);
        public static readonly Shortcut DiscardChunk = Add("DiscardChunk", ShortcutCategory.Diff, "Hunk.Discard", Key.D, s_cmd);

        // Merge conflict editor
        public static readonly Shortcut PrevConflict = Add("PrevConflict", ShortcutCategory.MergeConflict, "MergeConflictEditor.PrevConflict", Key.Up, s_cmd);
        public static readonly Shortcut NextConflict = Add("NextConflict", ShortcutCategory.MergeConflict, "MergeConflictEditor.NextConflict", Key.Down, s_cmd);
        public static readonly Shortcut SaveAndStageConflict = Add("SaveAndStageConflict", ShortcutCategory.MergeConflict, "MergeConflictEditor.SaveAndStage", Key.S, s_cmd);

        // Interactive rebase
        public static readonly Shortcut RebaseMoveUp = Add("RebaseMoveUp", ShortcutCategory.InteractiveRebase, "Hotkeys.InteractiveRebase.MoveUp", Key.Up, s_cmd);
        public static readonly Shortcut RebaseMoveDown = Add("RebaseMoveDown", ShortcutCategory.InteractiveRebase, "Hotkeys.InteractiveRebase.MoveDown", Key.Down, s_cmd);
        public static readonly Shortcut RebasePick = Add("RebasePick", ShortcutCategory.InteractiveRebase, "Hotkeys.InteractiveRebase.Pick", Key.P);
        public static readonly Shortcut RebaseEdit = Add("RebaseEdit", ShortcutCategory.InteractiveRebase, "Hotkeys.InteractiveRebase.Edit", Key.E);
        public static readonly Shortcut RebaseReword = Add("RebaseReword", ShortcutCategory.InteractiveRebase, "Hotkeys.InteractiveRebase.Reword", Key.R);
        public static readonly Shortcut RebaseSquash = Add("RebaseSquash", ShortcutCategory.InteractiveRebase, "Hotkeys.InteractiveRebase.Squash", Key.S);
        public static readonly Shortcut RebaseFixup = Add("RebaseFixup", ShortcutCategory.InteractiveRebase, "Hotkeys.InteractiveRebase.Fixup", Key.F);
        public static readonly Shortcut RebaseDrop = Add("RebaseDrop", ShortcutCategory.InteractiveRebase, "Hotkeys.InteractiveRebase.Drop", Key.D);

        public static List<ShortcutGroup> GetGroups()
        {
            var groups = new List<ShortcutGroup>();
            foreach (var category in Enum.GetValues<ShortcutCategory>())
            {
                var items = All.FindAll(x => x.Category == category);
                var header = category switch
                {
                    ShortcutCategory.Global => "Hotkeys.Global",
                    ShortcutCategory.Repository => "Hotkeys.Repo",
                    ShortcutCategory.NestedRepositories => "NestedRepos",
                    ShortcutCategory.Diff => "Hotkeys.Diff",
                    ShortcutCategory.MergeConflict => "Hotkeys.MergeConflict",
                    _ => "Hotkeys.InteractiveRebase",
                };

                groups.Add(new ShortcutGroup(App.Text(header), items));
            }

            return groups;
        }

        public static void ResetAll()
        {
            foreach (var shortcut in All)
                shortcut.Reset();

            NotifyChanged();
        }

        public static void Load(Dictionary<string, string> custom)
        {
            foreach (var shortcut in All)
            {
                if (custom != null && custom.TryGetValue(shortcut.Id, out var data) && Shortcut.TryDeserialize(data, out var gesture))
                    shortcut.Gesture = gesture;
                else
                    shortcut.Reset();
            }

            NotifyChanged();
        }

        public static Dictionary<string, string> Export()
        {
            var custom = new Dictionary<string, string>();
            foreach (var shortcut in All)
            {
                if (shortcut.IsCustomized)
                    custom[shortcut.Id] = Shortcut.Serialize(shortcut.Gesture);
            }

            return custom;
        }

        public static void NotifyChanged()
        {
            UpdateConflicts();
            Changed?.Invoke();
        }

        private static void UpdateConflicts()
        {
            foreach (var shortcut in All)
            {
                var conflict = false;
                if (shortcut.Gesture != null)
                {
                    foreach (var other in All)
                    {
                        if (other != shortcut && CanConflict(shortcut.Category, other.Category) && shortcut.Gesture.Equals(other.Gesture))
                        {
                            conflict = true;
                            break;
                        }
                    }
                }

                shortcut.HasConflict = conflict;
            }
        }

        private static bool CanConflict(ShortcutCategory a, ShortcutCategory b)
        {
            // Merge conflict editor and interactive rebase run in their own windows, so they only
            // share their window with global shortcuts.
            if (a == b || a == ShortcutCategory.Global || b == ShortcutCategory.Global)
                return true;

            return a is ShortcutCategory.Repository or ShortcutCategory.NestedRepositories or ShortcutCategory.Diff &&
                b is ShortcutCategory.Repository or ShortcutCategory.NestedRepositories or ShortcutCategory.Diff;
        }

        private static Shortcut Add(string id, ShortcutCategory category, string descriptionKey, Key key, KeyModifiers modifiers = KeyModifiers.None)
        {
            var shortcut = new Shortcut(id, category, descriptionKey, new KeyGesture(key, modifiers));
            All.Add(shortcut);
            return shortcut;
        }
    }
}
