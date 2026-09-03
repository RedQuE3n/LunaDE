using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Themes.Fluent;

namespace LunaDE.Shell;

// The Phase 0 application: one window, no XAML, no theme of our own.
//
// DELIBERATELY MINIMAL, because this is an instrument. Phase 0 is measuring
// which display server the process ends up on and what it can do there, and
// every additional moving part is something that can be blamed for a failure
// that was really the backend's. LunaP is not referenced yet for the same
// reason - it brings its own bootstrap that hardcodes UseX11 on Linux, which is
// precisely the thing under test. Wiring the two together is the next step, and
// it needs a seam in LunaP rather than a change here.
/// <summary>The Phase 0 probe application.</summary>
public sealed class App : Application
{
    /// <summary>Whether to print the backend report and exit rather than staying open.</summary>
    public static bool ProbeAndExit { get; set; }

    /// <summary>The builder the application was started from, which the report reads back off.</summary>
    public static AppBuilder? Builder { get; set; }

    /// <summary>The window that was shown, for the report to read back.</summary>
    public Window? ProbeWindow { get; private set; }

    /// <inheritdoc />
    public override void Initialize() => Styles.Add(new FluentTheme());

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = BuildWindow();
            ProbeWindow = window;
            desktop.MainWindow = window;

            // Opened fires before the first frame has necessarily been through
            // the compositor, and RenderScaling is exactly the value that can
            // still change when the compositor sends its scale. Waiting a beat
            // is not elegance, it is the difference between reading the default
            // and reading what the compositor said.
            window.Opened += (_, _) =>
            {
                var timer = new DispatcherTimerShim(TimeSpan.FromMilliseconds(750), () =>
                {
                    if (!ProbeAndExit)
                        return;

                    Console.WriteLine(Builder is null
                        ? "unavailable - no builder was handed to the application"
                        : BackendReport.Compose(Builder, window));

                    desktop.Shutdown();
                });

                timer.Start();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static Window BuildWindow()
    {
        var text = new TextBlock
        {
            Text = "LunaDE Phase 0 probe",
            FontSize = 20,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var hint = new TextBlock
        {
            Text = "The report is written to stdout.",
            Opacity = 0.7,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        return new Window
        {
            Title = "LunaDE Phase 0 probe",
            Width = 480,
            Height = 200,
            Background = Brushes.Transparent,
            Content = new StackPanel
            {
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { text, hint },
            },
        };
    }
}
