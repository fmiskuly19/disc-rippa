using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DiscRippa.Views;

/// <summary>
/// The app's own message box, styled like the main window. Same calls and results as <see cref="MessageBox.Show(string)"/>,
/// so it drops in anywhere. Text is laid out from the message itself: paragraphs (separated by a blank line) become
/// text, and a paragraph like "What MakeMKV said:" followed by "- …" lines becomes a details box that can be selected
/// and copied.
/// </summary>
public static class Dialog
{
    #region Methods

    /// <summary>Shows the styled message box and returns the button clicked.</summary>
    public static MessageBoxResult Show(string text, string caption, MessageBoxButton buttons = MessageBoxButton.OK,
                                        MessageBoxImage image = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.None)
        => Show(null, text, caption, buttons, image, defaultResult);

    /// <summary>Shows the styled message box and returns the button clicked.</summary>
    public static MessageBoxResult Show(Window? owner, string text, string caption = "DiscRippa", MessageBoxButton buttons = MessageBoxButton.OK,
                                        MessageBoxImage image = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        try
        {
            var window = new DialogWindow(text, caption, buttons, image, defaultResult);
            owner ??= Application.Current?.MainWindow;
            if (owner != null && owner != window && owner.IsLoaded && owner.IsVisible)
            {
                window.Owner = owner;
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            window.ShowDialog();
            return window.Result;
        }
        catch
        {
            //if anything about the styled dialog fails, the message must still get through.
            return MessageBox.Show(text, caption, buttons, image, defaultResult);
        }
    }

    #endregion

    #region Nested types

    private sealed class DialogWindow : Window
    {
        #region Fields

        private readonly MessageBoxButton _buttons;

        #endregion

        #region Properties

        public MessageBoxResult Result { get; private set; }

        #endregion

        #region Constructors

        /// <summary>Builds the dialog for this message.</summary>
        public DialogWindow(string text, string caption, MessageBoxButton buttons, MessageBoxImage image, MessageBoxResult defaultResult)
        {
            _buttons = buttons;
            Result = buttons is MessageBoxButton.OK ? MessageBoxResult.OK
                   : buttons is MessageBoxButton.YesNo ? MessageBoxResult.No
                   : MessageBoxResult.Cancel;   //closing the window = the "no" answer

            Style = (Style)Application.Current.FindResource("AppWindow");
            Title = "DiscRippa";
            Width = 560;
            SizeToContent = SizeToContent.Height;
            MaxHeight = SystemParameters.WorkArea.Height - 40;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = Application.Current?.MainWindow == null || !Application.Current.MainWindow.IsVisible;
            Background = Res("CardBrush");
            try
            {
                Icon = Application.Current?.MainWindow?.Icon;
            }
            catch
            {
            }

            var root = new DockPanel { LastChildFill = true };
            root.Children.Add(ButtonBar(buttons, defaultResult, image, caption, text));
            root.Children.Add(Body(text, caption, image));
            Content = root;

            PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.Escape) return;
                Close();
                e.Handled = true;
            };
        }

        #endregion

        #region Methods

        /// <summary>The icon, heading and message.</summary>
        private UIElement Body(string text, string caption, MessageBoxImage image)
        {
            var grid = new Grid { Margin = new Thickness(28, 26, 28, 22) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var badge = Badge(image);
            if (badge != null)
            {
                badge.Margin = new Thickness(0, 2, 16, 0);
                badge.VerticalAlignment = VerticalAlignment.Top;
                grid.Children.Add(badge);
            }

            var stack = new StackPanel();
            Grid.SetColumn(stack, 1);
            stack.Children.Add(new TextBlock
            {
                Text = Heading(caption, image),
                Style = (Style)FindResource("Heading"),
                FontSize = 19,
                Margin = new Thickness(0, 4, 0, 10),
            });

            foreach (var block in Paragraphs(text))
                stack.Children.Add(block);

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = stack };
            Grid.SetColumn(scroll, 1);
            grid.Children.Add(scroll);
            return grid;
        }

        /// <summary>Paragraphs as text; "Something said:" + "- …" lines as a selectable details box.</summary>
        private IEnumerable<UIElement> Paragraphs(string text)
        {
            var parts = text.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var lines = part.Split('\n');
                bool details = lines.Length > 1 && lines[0].TrimEnd().EndsWith(':') && lines.Skip(1).All(l => l.TrimStart().StartsWith("- ", StringComparison.Ordinal));
                if (details)
                {
                    yield return new TextBlock
                    {
                        Text = lines[0].TrimEnd(':', ' '),
                        Style = (Style)FindResource("Label"),
                        Margin = new Thickness(0, 6, 0, 6),
                    };
                    yield return new Border
                    {
                        Style = (Style)FindResource("StatTile"),
                        Padding = new Thickness(12, 8, 12, 8),
                        Margin = new Thickness(0, 0, 0, 10),
                        Child = new TextBox
                        {
                            Text = string.Join("\n", lines.Skip(1).Select(l => l.TrimStart().Substring(2))),
                            IsReadOnly = true,
                            TextWrapping = TextWrapping.Wrap,
                            BorderThickness = new Thickness(0),
                            Background = Brushes.Transparent,
                            FontFamily = new FontFamily("Consolas"),
                            FontSize = 12.5,
                            MaxHeight = 200,
                            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                            Padding = new Thickness(0),
                        },
                    };
                }
                else
                {
                    bool small = part.StartsWith("Full log:", StringComparison.Ordinal) || part.StartsWith("Details were saved", StringComparison.Ordinal);
                    yield return new TextBlock
                    {
                        Text = part,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 10),
                        FontSize = small ? 12.5 : 14,
                        Foreground = small ? Res("SubtleBrush") : Res("TextBrush"),
                        LineHeight = small ? double.NaN : 20,
                    };
                }
            }
        }

        /// <summary>The heading: the caption, or a default for the icon.</summary>
        private static string Heading(string caption, MessageBoxImage image)
        {
            if (!string.IsNullOrWhiteSpace(caption) && caption != "DiscRippa" && caption != "Error") return caption;
            return image switch
            {
                MessageBoxImage.Error => "Something went wrong",
                MessageBoxImage.Warning => "Heads up",
                MessageBoxImage.Question => "Are you sure?",
                _ => "DiscRippa",
            };
        }

        /// <summary>The round icon for the message type, or null.</summary>
        private Border? Badge(MessageBoxImage image)
        {
            (string glyph, string bg, string fg)? look = image switch
            {
                MessageBoxImage.Error => ("✕", "DangerSoftBrush", "DangerBrush"),
                MessageBoxImage.Warning => ("!", "WarnSoftBrush", "WarnBrush"),
                MessageBoxImage.Question => ("?", "AccentSoftBrush", "AccentBrush"),
                MessageBoxImage.Information => ("i", "AccentSoftBrush", "AccentBrush"),
                _ => null,
            };
            if (look == null) return null;
            var (glyph, bg, fg) = look.Value;
            return new Border
            {
                Width = 40,
                Height = 40,
                CornerRadius = new CornerRadius(20),
                Background = Res(bg),
                Child = new TextBlock
                {
                    Text = glyph,
                    FontSize = 20,
                    FontWeight = FontWeights.Bold,
                    Foreground = Res(fg),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
        }

        /// <summary>The bar with the answer buttons (and "Copy message" for errors).</summary>
        private UIElement ButtonBar(MessageBoxButton buttons, MessageBoxResult defaultResult, MessageBoxImage image, string caption, string text)
        {
            var bar = new Border
            {
                Background = Res("BgBrush"),
                BorderBrush = Res("LineBrush"),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(28, 14, 28, 14),
            };
            DockPanel.SetDock(bar, Dock.Bottom);

            var panel = new DockPanel { LastChildFill = false };
            if (image is MessageBoxImage.Error or MessageBoxImage.Warning)
            {
                var copy = new Button { Content = "Copy message", Style = (Style)FindResource("LinkButton"), VerticalAlignment = VerticalAlignment.Center };
                copy.Click += (_, _) =>
                {
                    try
                    {
                        Clipboard.SetText($"{caption}\n\n{text}");
                        copy.Content = "Copied";
                    }
                    catch
                    {
                    }
                };
                DockPanel.SetDock(copy, Dock.Left);
                panel.Children.Add(copy);
            }

            var choices = buttons switch
            {
                MessageBoxButton.OKCancel => new[] { MessageBoxResult.OK, MessageBoxResult.Cancel },
                MessageBoxButton.YesNo => new[] { MessageBoxResult.Yes, MessageBoxResult.No },
                MessageBoxButton.YesNoCancel => new[] { MessageBoxResult.Yes, MessageBoxResult.No, MessageBoxResult.Cancel },
                _ => new[] { MessageBoxResult.OK },
            };
            var keyboardDefault = choices.Contains(defaultResult) ? defaultResult : choices[0];

            //right to left: the main answer (OK / Yes) sits at the far right, in the accent colour.
            foreach (var choice in choices)
            {
                bool primary = choice == choices[0];
                var button = new Button
                {
                    Content = choice.ToString(),
                    MinWidth = 96,
                    Padding = new Thickness(16, 8, 16, 8),
                    Margin = new Thickness(10, 0, 0, 0),
                    IsDefault = choice == keyboardDefault,
                    IsCancel = choice is MessageBoxResult.Cancel || (choice == MessageBoxResult.No && !choices.Contains(MessageBoxResult.Cancel))
                               || (choices.Length == 1),
                };
                if (primary) button.Style = (Style)FindResource("PrimaryButton");
                button.Click += (_, _) =>
                {
                    Result = choice;
                    Close();
                };
                DockPanel.SetDock(button, Dock.Right);
                panel.Children.Add(button);
                if (button.IsDefault) Loaded += (_, _) => button.Focus();
            }
            bar.Child = panel;
            return bar;
        }

        /// <summary>A brush from the app's resources.</summary>
        private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

        #endregion
    }

    #endregion
}
