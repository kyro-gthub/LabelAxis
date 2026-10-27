using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace LabelAxis.Setup;

/// <summary>
/// Setup's window: Welcome, Options, Installing, Finish. "Install" on the Welcome page installs
/// with the usual choices (just for this user, the .labelproj file type, no desktop shortcut), or
/// with those of the copy already installed when this is an update.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "The cancellation source is disposed when the window closes (OnClosed).")]
public partial class SetupWindow : Window
{
    private enum TaskState
    {
        Waiting,
        Working,
        Done,
    }

    private const int WelcomeStep = 0, OptionsStep = 1, InstallStep = 2, FinishStep = 3;

    private static readonly string[] Placeholder = { "{0}" };

    private readonly InstalledCopy? _installed = Machine.FindInstalled();
    private readonly bool _needsRuntime = Machine.IsSmall && !Machine.DesktopRuntimeInstalled();
    private int _step;
    private bool _busy;
    private bool _copying;
    private bool _failed;
    private CancellationTokenSource? _cancel;
    private string _folderForJustMe = Machine.DefaultFolder(false);
    private string _folderForEveryone = Machine.DefaultFolder(true);
    private InstallProgress _last = new() { Stage = InstallStage.Preparing };

    public SetupWindow()
    {
        InitializeComponent();
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not TextBox)
            {
                DragMove();
            }
        };

        // The label sheet faintly drawn at the foot of the blue panel.
        for (int row = 0; row < 6; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                var label = new Rectangle { Width = 50, Height = 27, RadiusX = 5, RadiusY = 5, Stroke = new SolidColorBrush(Color.FromArgb(0x2E, 255, 255, 255)) };
                Canvas.SetLeft(label, 14 + (col * 58));
                Canvas.SetTop(label, 14 + (row * 34));
                SheetPattern.Children.Add(label);
            }
        }

        if (_installed is not null)
        {
            JustMeCard.IsEnabled = EveryoneCard.IsEnabled = FolderBox.IsEnabled = BrowseButton.IsEnabled = false;
            (_installed.ForEveryone ? EveryoneCard : JustMeCard).IsChecked = true;
            FolderBox.Text = _installed.Folder;
            AssociateSwitch.IsChecked = _installed.Associate;
            DesktopSwitch.IsChecked = _installed.Desktop;
            UpdatingNote.Visibility = Visibility.Visible;
        }
        else
        {
            JustMeCard.IsChecked = true;
        }

        AppOpenNote.Visibility = Machine.AppIsRunning() ? Visibility.Visible : Visibility.Collapsed;
        ApplyLanguage();
        Show(WelcomeStep);
    }

    // ---- words and pages ---------------------------------------------------

    private void ApplyLanguage()
    {
        FlowDirection = Strings.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        // The brand font has Latin letters only; other scripts use Windows' own fonts for them.
        FontFamily = Strings.Language switch
        {
            "ar" or "fa" or "ckb" or "ru" => new FontFamily("Segoe UI"),
            "hi" => new FontFamily("Nirmala UI, Segoe UI"),
            "zh-Hans" => new FontFamily("Microsoft YaHei UI, Segoe UI"),
            _ => (FontFamily)FindResource("Brand"),
        };

        VersionText.Text = Strings.F("SetupVersion", Machine.Version);
        OpenSourceText.Text = Strings.T("OpenSource");
        MinimiseButton.ToolTip = Strings.T("Minimise");
        CloseButton.ToolTip = Strings.T("ButtonClose");
        LanguageName.Text = Strings.LanguageNames[Array.IndexOf(Strings.Languages, Strings.Language)];

        bool update = _installed is not null;
        WelcomeOver.Text = Strings.T("WelcomeOver");
        WelcomeTitle.Text = Strings.T(update ? "UpdateTitle" : "WelcomeTitle");
        WelcomeBody.Text = update ? Strings.F("UpdateBody", _installed!.Version, Machine.Version) : Strings.T("WelcomeBody");
        Feature1Title.Text = Strings.T("Feature1Title");
        Feature1Text.Text = Strings.T("Feature1Text");
        Feature2Title.Text = Strings.T("Feature2Title");
        Feature2Text.Text = Strings.T("Feature2Text");
        Feature3Title.Text = Strings.T("Feature3Title");
        Feature3Text.Text = Strings.T("Feature3Text");
        string[] accept = Strings.T("Accept").Split(Placeholder, StringSplitOptions.None);
        AcceptBefore.Text = accept[0];
        AcceptAfter.Text = accept.Length > 1 ? accept[1] : string.Empty;
        LicenceLink.Content = Strings.T("Licence");

        OptionsOver.Text = Strings.T("OptionsOver");
        OptionsTitle.Text = Strings.T("OptionsTitle");
        InstallForLabel.Text = Strings.T("InstallFor");
        JustMeTitle.Text = Strings.T("JustMe");
        JustMeText.Text = Strings.T("JustMeText");
        EveryoneTitle.Text = Strings.T("Everyone");
        EveryoneText.Text = Strings.T("EveryoneText");
        FolderLabel.Text = Strings.T("Folder");
        BrowseButton.Content = Strings.T("ButtonBrowse");
        UpdatingNote.Text = Strings.T("UpdatingHere");
        AssociateTitle.Text = Strings.T("Associate");
        AssociateText.Text = Strings.T("AssociateText");
        DesktopTitle.Text = Strings.T("Desktop");
        DesktopText.Text = Strings.T("DesktopText");

        InstallingOver.Text = Strings.T("InstallingOver");
        InstallingTitle.Text = Strings.T("InstallingTitle");
        TipTitle.Text = Strings.T("TipTitle");
        TipText.Text = Strings.T("TipText");
        ErrorTitle.Text = Strings.T("FailedTitle");

        ReadyTitle.Text = Strings.T("ReadyTitle");
        ReadyBody.Text = Strings.T("ReadyBody");
        NewInText.Text = Strings.F("NewIn", Machine.Version);
        NewCard.Visibility = Machine.Version == Strings.HighlightsVersion ? Visibility.Visible : Visibility.Collapsed;
        HighlightList.Children.Clear();
        foreach (string key in new[] { "Highlight1", "Highlight2", "Highlight3" })
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 7) };
            row.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = (Brush)FindResource("Accent"), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = Strings.T(key), FontSize = 12.5, Margin = new Thickness(10, 0, 0, 0) });
            HighlightList.Children.Add(row);
        }

        OpenNowText.Text = Strings.T("OpenNow");
        AppOpenNote.Text = Strings.T("AppOpen");
        LicenceTitle.Text = Strings.T("Licence");
        LicenceClose.Content = Strings.T("ButtonClose");

        LanguageList.Children.Clear();
        for (int i = 0; i < Strings.Languages.Length; i++)
        {
            string code = Strings.Languages[i];
            var item = new Button { Content = Strings.LanguageNames[i], Tag = code, Style = (Style)FindResource("Secondary"), Margin = new Thickness(0, 0, 0, 3), Height = 32, FontSize = 12.5, HorizontalContentAlignment = HorizontalAlignment.Left };
            item.Click += (_, _) =>
            {
                LanguagePopup.IsOpen = false;
                Strings.Use(code);
                ApplyLanguage();
                Show(_step);
            };
            LanguageList.Children.Add(item);
        }

        if (_failed)
        {
            Show(InstallStep);
        }
    }

    private void Show(int step)
    {
        _step = step;
        WelcomePage.Visibility = step == WelcomeStep ? Visibility.Visible : Visibility.Collapsed;
        OptionsPage.Visibility = step == OptionsStep ? Visibility.Visible : Visibility.Collapsed;
        InstallPage.Visibility = step == InstallStep ? Visibility.Visible : Visibility.Collapsed;
        FinishPage.Visibility = step == FinishStep ? Visibility.Visible : Visibility.Collapsed;
        DrawSteps();

        string install = Strings.T(_installed is null ? "ButtonInstall" : "ButtonUpdate");
        SecondaryButton.Visibility = Visibility.Visible;
        PrimaryButton.Visibility = Visibility.Visible;
        AppOpenNote.Visibility = step <= OptionsStep && Machine.AppIsRunning() ? Visibility.Visible : Visibility.Collapsed;
        switch (step)
        {
            case WelcomeStep:
                FooterText.Text = Strings.F(ForEveryone ? "FooterEveryone" : "FooterJustMe", Machine.SizeMb);
                SecondaryButton.Content = Strings.T("ButtonOptions");
                PrimaryButton.Content = install;
                PrimaryButton.IsEnabled = SecondaryButton.IsEnabled = AcceptBox.IsChecked == true;
                break;
            case OptionsStep:
                FooterText.Text = Strings.F(Machine.IsSmall ? "SizeSmall" : "SizeOffline", Machine.SizeMb);
                SecondaryButton.Content = Strings.T("ButtonBack");
                PrimaryButton.Content = install;
                PrimaryButton.IsEnabled = SecondaryButton.IsEnabled = true;
                break;
            case InstallStep:
                FooterText.Text = string.Empty;
                SecondaryButton.Content = Strings.T(_failed ? "ButtonClose" : "ButtonCancel");
                SecondaryButton.IsEnabled = _failed || !_copying;
                PrimaryButton.Content = Strings.T("ButtonTryAgain");
                PrimaryButton.Visibility = _failed ? Visibility.Visible : Visibility.Collapsed;
                PrimaryButton.IsEnabled = true;
                ErrorBox.Visibility = _failed ? Visibility.Visible : Visibility.Collapsed;
                TipBox.Visibility = _failed ? Visibility.Collapsed : Visibility.Visible;
                Report(_last);
                break;
            default:
                FooterText.Text = string.Empty;
                SecondaryButton.Visibility = Visibility.Collapsed;
                PrimaryButton.Content = Strings.T("ButtonFinish");
                PrimaryButton.IsEnabled = true;
                break;
        }

        MinimiseButton.IsEnabled = true;
        CloseButton.IsEnabled = !_copying;
    }

    private bool ForEveryone => EveryoneCard.IsChecked == true;

    /// <summary>The four steps in the blue panel: ticked when done, a ring around the current one.</summary>
    private void DrawSteps()
    {
        string[] names = { "StepWelcome", "StepOptions", "StepInstalling", "StepFinish" };
        var white = Brushes.White;
        var faint = new SolidColorBrush(Color.FromArgb(0x66, 255, 255, 255));
        StepList.Children.Clear();
        for (int n = 0; n < names.Length; n++)
        {
            bool done = n < _step || (_step == FinishStep && n == FinishStep);
            bool now = n == _step && !done;
            var dot = new Grid { Width = 24, Height = 24 };
            if (done)
            {
                dot.Children.Add(new Ellipse { Fill = white });
                dot.Children.Add(new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("Icons"), FontSize = 11, Foreground = new SolidColorBrush((Color)FindResource("PanelBottom")), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            }
            else if (now)
            {
                dot.Children.Add(new Ellipse { Stroke = white, StrokeThickness = 2 });
                dot.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = white });
            }
            else
            {
                dot.Children.Add(new Ellipse { Stroke = faint, StrokeThickness = 1.5 });
            }

            var row = new StackPanel { Orientation = Orientation.Horizontal, Opacity = done || now || n == _step ? 1 : 0.6 };
            row.Children.Add(dot);
            row.Children.Add(new TextBlock { Text = Strings.T(names[n]), Foreground = white, FontSize = 13.5, FontWeight = n == _step ? FontWeights.Bold : FontWeights.Medium, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            StepList.Children.Add(row);
            if (n < names.Length - 1)
            {
                StepList.Children.Add(new Rectangle { Width = 2, Height = 22, Fill = n < _step ? white : new SolidColorBrush(Color.FromArgb(0x4D, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(11, 3, 0, 3) });
            }
        }
    }

    // ---- buttons -----------------------------------------------------------

    private void OnPrimary(object sender, RoutedEventArgs e)
    {
        switch (_step)
        {
            case WelcomeStep:
            case OptionsStep:
            case InstallStep:
                _ = InstallAsync();
                break;
            default:
                if (OpenNowBox.IsChecked == true)
                {
                    StartLabelAxis();
                }

                Close();
                break;
        }
    }

    private void OnSecondary(object sender, RoutedEventArgs e)
    {
        switch (_step)
        {
            case WelcomeStep:
                Show(OptionsStep);
                break;
            case OptionsStep:
                Show(WelcomeStep);
                break;
            default:
                if (_failed)
                {
                    Close();
                }
                else
                {
                    _cancel?.Cancel();
                }

                break;
        }
    }

    private void OnAcceptChanged(object sender, RoutedEventArgs e) => Show(_step);

    private void OnScopeChanged(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || _installed is not null)
        {
            return;
        }

        // Keep a folder the user typed for one choice, and offer the usual one for the other.
        if (sender == EveryoneCard)
        {
            _folderForJustMe = FolderBox.Text.Length > 0 ? FolderBox.Text : _folderForJustMe;
            FolderBox.Text = _folderForEveryone;
        }
        else
        {
            if (FolderBox.Text.Length > 0 && FolderBox.Text != _folderForJustMe)
            {
                _folderForEveryone = FolderBox.Text;
            }

            FolderBox.Text = _folderForJustMe;
        }

        if (_step == WelcomeStep)
        {
            Show(WelcomeStep);
        }
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        if (FolderPicker.Pick(this, Strings.T("ChooseFolder"), FolderBox.Text) is { } chosen)
        {
            // A folder picked as it is gets LabelAxis's own folder inside it, as Inno Setup does.
            FolderBox.Text = chosen.EndsWith("LabelAxis", StringComparison.OrdinalIgnoreCase) ? chosen : System.IO.Path.Combine(chosen, "LabelAxis");
        }
    }

    private void OnLanguageButton(object sender, RoutedEventArgs e) => LanguagePopup.IsOpen = !LanguagePopup.IsOpen;

    private void OnShowLicence(object sender, RoutedEventArgs e)
    {
        using Stream? s = typeof(SetupWindow).Assembly.GetManifestResourceStream("LICENSE.txt");
        LicenceBody.Text = s is null ? string.Empty : new StreamReader(s).ReadToEnd();
        LicencePanel.Visibility = Visibility.Visible;
        e.Handled = true;
    }

    private void OnHideLicence(object sender, RoutedEventArgs e) => LicencePanel.Visibility = Visibility.Collapsed;

    private void OnMinimise(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _cancel?.Dispose();
        base.OnClosed(e);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_copying)
        {
            e.Cancel = true; // files are being copied; stopping now would leave a half-installed copy
        }
        else if (_busy && !_failed)
        {
            if (MessageBox.Show(this, Strings.T("StopQuestion"), "LabelAxis", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _cancel?.Cancel();
            }
            else
            {
                e.Cancel = true;
            }
        }
    }

    // ---- installing --------------------------------------------------------

    private async Task InstallAsync()
    {
        var choice = new InstallChoice
        {
            ForEveryone = ForEveryone,
            Folder = (FolderBox.Text.Length > 0 ? FolderBox.Text : Machine.DefaultFolder(ForEveryone)).Trim(),
            Associate = AssociateSwitch.IsChecked == true,
            Desktop = DesktopSwitch.IsChecked == true,
        };

        _failed = false;
        _busy = true;
        _last = new InstallProgress { Stage = InstallStage.Preparing };
        _cancel?.Dispose();
        _cancel = new CancellationTokenSource();
        Show(InstallStep);
        try
        {
            if (!Engine.Present)
            {
                throw new SetupException(Strings.T("ErrorMissing"));
            }

            long need = Machine.SizeMb + (_needsRuntime ? 60 : 0);
            long free = Machine.FreeMb(choice.Folder);
            if (free >= 0 && free < need)
            {
                throw new SetupException(Strings.F("ErrorSpace", System.IO.Path.GetPathRoot(choice.Folder) ?? choice.Folder, need));
            }

            var progress = new Progress<InstallProgress>(Report);
            await Engine.InstallAsync(choice, Machine.FileCount, progress, _cancel.Token);
            _busy = _copying = false;
            FolderBox.Text = choice.Folder;
            Show(FinishStep);
        }
        catch (OperationCanceledException)
        {
            _busy = _copying = false;
            Show(OptionsStep);
        }
        catch (SetupException ex)
        {
            Fail(ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Fail(ex.Message);
        }
    }

    private void Fail(string message)
    {
        _busy = _copying = false;
        _failed = true;
        ErrorText.Text = message;
        StopSweep();
        Show(InstallStep);
    }

    private void Report(InstallProgress p)
    {
        _last = p;
        _copying = p.Stage >= InstallStage.Copying && _busy;
        SecondaryButton.IsEnabled = _failed || !_copying;
        CloseButton.IsEnabled = !_copying;

        StatusText.Text = p.Stage switch
        {
            InstallStage.Copying when p.Detail.Length > 0 => Strings.F("CopyingFile", p.Detail),
            InstallStage.Finishing => Strings.T("Finishing"),
            InstallStage.Preparing => Strings.T("Preparing"),
            _ => p.Detail,
        };

        if (p.Fraction >= 0)
        {
            StopSweep();
            Fill.Width = Math.Max(0, Track.ActualWidth > 0 ? Track.ActualWidth * p.Fraction : 0);
            PercentText.Text = ((int)Math.Round(p.Fraction * 100)).ToString(System.Globalization.CultureInfo.CurrentCulture) + "%";
        }
        else
        {
            PercentText.Text = string.Empty;
            StartSweep();
        }

        if (_failed)
        {
            StopSweep();
            StatusText.Text = PercentText.Text = string.Empty;
        }

        TaskList.Children.Clear();
        AddTask(Strings.T("TaskCheck"), null, p.Stage > InstallStage.Preparing || _failed ? TaskState.Done : TaskState.Working);
        if (_needsRuntime)
        {
            AddTask(Strings.T("TaskRuntime"), Strings.T("TaskRuntimeNote"),
                p.Stage >= InstallStage.Copying ? TaskState.Done : p.Stage >= InstallStage.DownloadingRuntime ? TaskState.Working : TaskState.Waiting);
        }

        AddTask(Strings.T("TaskCopy"), null, p.Stage >= InstallStage.Finishing ? TaskState.Done : p.Stage == InstallStage.Copying ? TaskState.Working : TaskState.Waiting);
        AddTask(Strings.T("TaskFinish"), null, p.Stage == InstallStage.Finishing ? TaskState.Working : TaskState.Waiting);
    }

    private void AddTask(string text, string? note, TaskState state)
    {
        if (_failed && state == TaskState.Working)
        {
            state = TaskState.Waiting;
        }

        var mark = new Grid { Width = 22, Height = 22 };
        switch (state)
        {
            case TaskState.Done:
                mark.Children.Add(new Ellipse { Fill = (Brush)FindResource("Good") });
                mark.Children.Add(new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("Icons"), FontSize = 11, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                break;
            case TaskState.Working:
                mark.Children.Add(new Ellipse { Stroke = (Brush)FindResource("Line"), StrokeThickness = 2.5 });
                var arc = new System.Windows.Shapes.Path { Stroke = (Brush)FindResource("Accent"), StrokeThickness = 2.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Data = Geometry.Parse("M11,1.25 A9.75,9.75 0 0 1 20.75,11"), RenderTransformOrigin = new Point(0.5, 0.5) };
                var spin = new RotateTransform();
                arc.RenderTransform = spin;
                spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever });
                mark.Children.Add(arc);
                break;
            default:
                mark.Children.Add(new Ellipse { Stroke = (Brush)FindResource("Line"), StrokeThickness = 1.6 });
                break;
        }

        var words = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = text, FontSize = 13, FontWeight = state == TaskState.Working ? FontWeights.Bold : FontWeights.Medium, Foreground = (Brush)FindResource(state == TaskState.Waiting ? "Muted" : "Text") });
        if (note is not null)
        {
            words.Children.Add(new TextBlock { Text = note, FontSize = 11.5, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 1, 0, 0) });
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        row.Children.Add(mark);
        row.Children.Add(words);
        TaskList.Children.Add(row);
    }

    /// <summary>A short bar moving along the track while how far it has got cannot be told.</summary>
    private void StartSweep()
    {
        if (Fill.Tag is "sweep" || Track.ActualWidth <= 0)
        {
            return;
        }

        Fill.Tag = "sweep";
        Fill.Width = Track.ActualWidth * 0.3;
        FillShift.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(-Fill.Width, Track.ActualWidth, TimeSpan.FromSeconds(1.4)) { RepeatBehavior = RepeatBehavior.Forever });
    }

    private void StopSweep()
    {
        if (Fill.Tag is "sweep")
        {
            Fill.Tag = null;
            FillShift.BeginAnimation(TranslateTransform.XProperty, null);
            FillShift.X = 0;
        }
    }

    private void StartLabelAxis()
    {
        string exe = System.IO.Path.Combine(FolderBox.Text, "LabelAxis.exe");
        if (File.Exists(exe))
        {
            try
            {
                using Process? _ = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = FolderBox.Text });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // It is in the Start menu.
            }
        }
    }
}
