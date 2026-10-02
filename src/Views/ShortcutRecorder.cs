using System;
using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace SourceGit.Views
{
    public class ShortcutRecorder : Border
    {
        public static readonly StyledProperty<Models.Shortcut> ShortcutProperty =
            AvaloniaProperty.Register<ShortcutRecorder, Models.Shortcut>(nameof(Shortcut));

        public Models.Shortcut Shortcut
        {
            get => GetValue(ShortcutProperty);
            set => SetValue(ShortcutProperty, value);
        }

        public static readonly RoutedEvent<RoutedEventArgs> ShortcutChangedEvent =
            RoutedEvent.Register<ShortcutRecorder, RoutedEventArgs>(nameof(ShortcutChanged), RoutingStrategies.Bubble);

        public event EventHandler<RoutedEventArgs> ShortcutChanged
        {
            add => AddHandler(ShortcutChangedEvent, value);
            remove => RemoveHandler(ShortcutChangedEvent, value);
        }

        public ShortcutRecorder()
        {
            Focusable = true;
            Cursor = new Cursor(StandardCursorType.Hand);
            Child = _text;
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == ShortcutProperty)
            {
                if (change.OldValue is Models.Shortcut oldValue)
                    oldValue.PropertyChanged -= OnShortcutPropertyChanged;
                if (change.NewValue is Models.Shortcut newValue)
                    newValue.PropertyChanged += OnShortcutPropertyChanged;

                UpdateText();
            }
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);

            if (Shortcut is { } shortcut)
                shortcut.PropertyChanged -= OnShortcutPropertyChanged;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            if (Shortcut is { } shortcut)
            {
                shortcut.PropertyChanged -= OnShortcutPropertyChanged;
                shortcut.PropertyChanged += OnShortcutPropertyChanged;
            }

            UpdateText();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            Focus(NavigationMethod.Pointer);
            e.Handled = true;
        }

        protected override void OnGotFocus(GotFocusEventArgs e)
        {
            base.OnGotFocus(e);
            UpdateText();
        }

        protected override void OnLostFocus(RoutedEventArgs e)
        {
            base.OnLostFocus(e);
            UpdateText();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (Shortcut is not { } shortcut)
            {
                base.OnKeyDown(e);
                return;
            }

            e.Handled = true;

            // Wait until a non-modifier key is pressed.
            if (Models.Shortcut.IsModifierKey(e.Key))
                return;

            if (e is { Key: Key.Escape, KeyModifiers: KeyModifiers.None })
            {
                StopRecording();
                return;
            }

            if (e is { Key: Key.Back or Key.Delete, KeyModifiers: KeyModifiers.None })
                shortcut.Gesture = null;
            else
                shortcut.Gesture = new KeyGesture(e.Key, e.KeyModifiers);

            RaiseEvent(new RoutedEventArgs(ShortcutChangedEvent));
            StopRecording();
        }

        private void StopRecording()
        {
            TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
        }

        private void OnShortcutPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            UpdateText();
        }

        private void UpdateText()
        {
            if (IsFocused)
            {
                _text.Text = App.Text("Preferences.Shortcuts.Recording");
                _text.FontStyle = FontStyle.Italic;
            }
            else
            {
                var display = Shortcut?.DisplayText;
                _text.Text = string.IsNullOrEmpty(display) ? "-" : display;
                _text.FontStyle = FontStyle.Normal;
            }

            Classes.Set("conflict", Shortcut is { HasConflict: true });
        }

        private readonly TextBlock _text = new TextBlock()
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
    }
}
