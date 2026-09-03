using Avalonia;

namespace LunaDE.Shell;

// THE ONE PLACE THE WINDOWING BACKEND IS CHOSEN.
//
// LunaDE is Wayland-first and falls back to X11. That ordering is a decision
// rather than a default, and this is the only file allowed to express it, for
// the same reason LunaP keeps LunaApp.Configure: the alternative is the
// sequence spelled out in several entry points that eventually disagree, and a
// disagreement here is a session that starts on the wrong display server.
//
// WHY UsePlatformDetect IS NEVER CALLED. It cannot select Wayland. Avalonia's
// platform detection resolves to X11 on Linux - measured on 12.1.0 by
// reflecting the builder's private WindowingSubsystemInitializer, recorded in
// LunaP's docs/LunaP.md §35.1 - and Avalonia.Desktop, the package detection
// ships in, does not even reference Avalonia.Wayland. The backend is opt-in,
// not absent. Calling UsePlatformDetect here would therefore silently pin the
// shell to X11 no matter what this file appeared to say.
//
// WHAT THE FALLBACK ACTUALLY KEYS ON, AND WHAT IT DOES NOT. The rule below is
// "WAYLAND_DISPLAY is set" plus a manual override. It is deliberately NOT
// "try Wayland, catch, retry X11": the backend is installed on the builder here
// and only initialised later, inside Setup, so a failure surfaces after this
// method has returned and cannot be caught at this level. Recovering from that
// honestly needs a supervising process that restarts the shell with the
// override set. That is not built, and saying so is the point - see
// docs/LunaDE.md §2.
public static class ShellApp
{
    // Set LUNADE_BACKEND=x11 or =wayland to override the rule below. This
    // exists for bisecting a rendering fault across two display servers on one
    // machine, which is otherwise a logout and a login.
    private const string BackendOverrideVariable = "LUNADE_BACKEND";

    /// <summary>The Avalonia bootstrap sequence: Wayland where available, X11 otherwise.</summary>
    /// <typeparam name="TApp">Your Application type, constructed by Avalonia.</typeparam>
    /// <returns>A builder to call StartWithClassicDesktopLifetime on.</returns>
    public static AppBuilder Configure<TApp>() where TApp : Application, new() =>
        Finish(AppBuilder.Configure<TApp>());

    /// <summary>Which backend <see cref="Configure{TApp}"/> will select, without building anything.</summary>
    /// <returns>The chosen backend, and the reason it was chosen.</returns>
    public static (Backend Backend, string Reason) Decide()
    {
        var over = Environment.GetEnvironmentVariable(BackendOverrideVariable);
        if (!string.IsNullOrWhiteSpace(over))
        {
            if (over.Equals("x11", StringComparison.OrdinalIgnoreCase))
                return (Backend.X11, $"{BackendOverrideVariable}={over}");
            if (over.Equals("wayland", StringComparison.OrdinalIgnoreCase))
                return (Backend.Wayland, $"{BackendOverrideVariable}={over}");

            // An unrecognised override is not silently ignored. Somebody typed
            // it meaning something, and picking a backend anyway would hide a
            // typo behind behaviour that looks deliberate.
            throw new InvalidOperationException(
                $"{BackendOverrideVariable}='{over}' is not recognised. Use 'wayland' or 'x11'.");
        }

        if (!OperatingSystem.IsLinux())
            return (Backend.PlatformDefault, "not Linux - Avalonia's own platform default applies");

        var display = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        return string.IsNullOrEmpty(display)
            ? (Backend.X11, "WAYLAND_DISPLAY is unset")
            : (Backend.Wayland, $"WAYLAND_DISPLAY={display}");
    }

    private static AppBuilder Finish(AppBuilder builder)
    {
        // UseSkia IS NOT OPTIONAL HERE, and the reason is the whole shape of
        // this file. UsePlatformDetect does two jobs: it picks a windowing
        // subsystem AND it installs the rendering subsystem. Skipping it to
        // reach Wayland therefore also skips the renderer, and Setup throws
        // "No rendering system configured. Consider calling UseSkia()", and then,
        // once that was added, "No text shaping system configured. Consider
        // calling UseHarfBuzz()". Both were hit in order on the Phase 0 probe,
        // 2026-08-27. UsePlatformDetect installs THREE subsystems - windowing,
        // rendering and text shaping - and replacing it means replacing all
        // three by hand.
        //
        // LunaP's docs/LunaP.md §35.2 records the same coupling from the other
        // side: its BootstrapTests prove UsePlatformDetect is in the chain
        // through what it installs - RenderingSubsystemName and
        // TextShapingSubsystemName - rather than through its own name. Anything
        // that replaces that call inherits the obligation to replace all of it.
        builder = builder.WithInterFont().LogToTrace().UseSkia().UseHarfBuzz();

        return Decide().Backend switch
        {
            Backend.Wayland => builder.UseWayland(),
            Backend.X11 => builder.UseX11(),

            // Windows and macOS. Detection is correct on both, and neither has
            // a Wayland question to answer.
            _ => builder.UsePlatformDetect(),
        };
    }
}

/// <summary>The windowing backend LunaDE will ask Avalonia for.</summary>
public enum Backend
{
    /// <summary>Wayland, via Avalonia.Wayland. The default on a Wayland session.</summary>
    Wayland,

    /// <summary>X11, via Avalonia.X11. The fallback, and XWayland in practice under a Wayland compositor.</summary>
    X11,

    /// <summary>Avalonia's own platform detection, used off Linux where it is correct.</summary>
    PlatformDefault,
}
