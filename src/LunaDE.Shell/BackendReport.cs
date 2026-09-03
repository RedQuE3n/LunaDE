using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;

namespace LunaDE.Shell;

// WHAT ACTUALLY GOT SELECTED, ASKED OF THE OBJECTS THEMSELVES.
//
// The reason this class exists rather than a log line saying "using Wayland":
// a bootstrap that INTENDS to select a backend and a process that IS running on
// one are different claims, and only the second is worth anything. ShellApp
// decides; this reads back what the decision produced. When the two disagree,
// the disagreement is the finding.
//
// Both readings are reflection, and deliberately so:
//
//   - AppBuilder.WindowingSubsystemInitializer is internal. It is the same
//     private member LunaP's docs/LunaP.md §35.1 reflected to establish that
//     UsePlatformDetect and UseX11 install the identical initializer on 12.1.0.
//     Using the same instrument means this project's numbers can be compared
//     with that one's rather than merely resembling them.
//
//   - TopLevel.PlatformImpl is reflected rather than called because the
//     property carries an obsoletion, and this repository builds with
//     TreatWarningsAsErrors. Reflection is the honest way to read a member the
//     compiler is right to discourage: it does not pretend the member is
//     supported, and it cannot silently keep compiling if the member is removed
//     - it returns "unavailable", which is a result rather than a crash.
//
// NOTHING HERE INVENTS A VALUE. Every reader returns null or a described
// failure when it cannot get an answer, and the caller prints that. A report
// that guessed would be worse than no report, because it would be believed.
public static class BackendReport
{
    /// <summary>The windowing initializer the builder holds, e.g. <c>&lt;UseWayland&gt;b__0_0</c>.</summary>
    /// <param name="builder">A configured builder, read before or after Setup.</param>
    /// <returns>The initializer's method name, or a description of why it could not be read.</returns>
    public static string ReadWindowingInitializer(AppBuilder builder)
    {
        const string member = "WindowingSubsystemInitializer";
        const BindingFlags anywhere =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        // Searched across every visibility rather than the one it was expected
        // to have. The first version of this reader looked only for a
        // non-public INSTANCE member and reported "unavailable", which reads
        // like a finding about Avalonia and was a bug in the instrument.
        object? value = typeof(AppBuilder).GetProperty(member, anywhere)?.GetValue(builder)
                        ?? typeof(AppBuilder).GetField(member, anywhere)?.GetValue(builder);

        if (value is null)
        {
            // Self-diagnosing on failure: if the expected name is gone, say what
            // IS there, so the next reader knows where to look instead of
            // guessing a second name and reporting a second false absence.
            var candidates = typeof(AppBuilder)
                .GetMembers(anywhere)
                .Select(m => m.Name)
                .Where(n => n.Contains("Windowing", StringComparison.Ordinal))
                .Distinct()
                .ToArray();

            return candidates.Length == 0
                ? $"unavailable - no member named {member} on AppBuilder, and no member whose name contains 'Windowing'"
                : $"unavailable - no readable {member}; members containing 'Windowing': {string.Join(", ", candidates)}";
        }

        return value switch
        {
            Delegate d => d.Method.Name,
            _ => $"unavailable - unexpected type {value.GetType().FullName}",
        };
    }

    /// <summary>The concrete platform type backing a window, e.g. <c>Avalonia.Wayland.WindowImpl</c>.</summary>
    /// <param name="window">A window that has been shown.</param>
    /// <returns>The implementation's full type name, or a description of why it could not be read.</returns>
    public static string ReadPlatformImplType(Window window)
    {
        const string member = "PlatformImpl";

        var property = typeof(TopLevel).GetProperty(member, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (property is null)
            return $"unavailable - no instance property named {member} on TopLevel";

        var impl = property.GetValue(window);
        return impl is null
            ? "unavailable - the window has no platform implementation (not shown yet?)"
            : impl.GetType().FullName ?? impl.GetType().Name;
    }

    /// <summary>Everything Phase 0 wants to know, as text fit for a terminal and a man page.</summary>
    /// <param name="builder">The builder the application was started from.</param>
    /// <param name="window">The window that was shown, or null if none was.</param>
    /// <returns>A multi-line report.</returns>
    public static string Compose(AppBuilder builder, Window? window)
    {
        var (backend, reason) = ShellApp.Decide();
        var sb = new StringBuilder();

        sb.AppendLine("LunaDE backend report");
        sb.AppendLine("======================");
        sb.AppendLine();
        sb.AppendLine("Decision");
        sb.AppendLine($"  requested backend    : {backend}");
        sb.AppendLine($"  reason               : {reason}");
        sb.AppendLine();
        sb.AppendLine("Session");
        sb.AppendLine($"  XDG_SESSION_TYPE     : {Env("XDG_SESSION_TYPE")}");
        sb.AppendLine($"  WAYLAND_DISPLAY      : {Env("WAYLAND_DISPLAY")}");
        sb.AppendLine($"  DISPLAY              : {Env("DISPLAY")}");
        sb.AppendLine($"  XDG_CURRENT_DESKTOP  : {Env("XDG_CURRENT_DESKTOP")}");
        sb.AppendLine();
        sb.AppendLine("What Avalonia actually installed");
        sb.AppendLine($"  windowing initializer: {ReadWindowingInitializer(builder)}");

        if (window is null)
        {
            sb.AppendLine("  platform impl        : no window was shown");
            return sb.ToString();
        }

        sb.AppendLine($"  platform impl        : {ReadPlatformImplType(window)}");
        sb.AppendLine();
        sb.AppendLine("Scaling");

        // RenderScaling is the whole fractional-scaling question in one number.
        // A backend limited to integer scaling reports 1 or 2 and never 1.25;
        // wp_fractional_scale_manager_v1 is what makes the third value possible.
        sb.AppendLine($"  window RenderScaling : {window.RenderScaling:0.####}");
        sb.AppendLine($"  fractional           : {(IsFractional(window.RenderScaling) ? "YES" : "no - integer scale, which does not by itself prove the protocol is missing")}");

        var screens = window.Screens;
        if (screens is null)
        {
            sb.AppendLine("  screens              : unavailable");
            return sb.ToString();
        }

        sb.AppendLine($"  screen count         : {screens.ScreenCount}");
        foreach (var screen in screens.All)
            sb.AppendLine($"    - {screen.Bounds.Width}x{screen.Bounds.Height} scaling {screen.Scaling:0.####}{(screen.IsPrimary ? " (primary)" : string.Empty)}");

        return sb.ToString();
    }

    private static bool IsFractional(double scale) => Math.Abs(scale - Math.Round(scale)) > 0.0001;

    private static string Env(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : "(unset)";
}
