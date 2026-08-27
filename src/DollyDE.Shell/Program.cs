using Avalonia;

namespace DollyDE.Shell;

// PHASE 0 ENTRY POINT.
//
// `dotnet run` opens the probe window and leaves it open. `dotnet run -- --probe`
// opens it, reads the backend report once the compositor has had a chance to
// speak, prints it to stdout and exits. The second form is the one that
// produces a number worth writing into docs/DollyDE.md, because it terminates:
// a measurement that needs a human to close a window is a measurement that will
// not be taken twice.
//
// BOTH FORMS GO THROUGH StartWithClassicDesktopLifetime, which matters more than
// it looks. An earlier draft built the lifetime by hand so the probe could hold
// a reference to it; that put the measurement on a startup path the shell will
// never use, which is the kind of difference that makes a probe agree with
// itself and disagree with production.
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var builder = DollyApp.Configure<App>();

        // Handed over as statics because Avalonia constructs the Application
        // itself - Configure<TApp> requires a parameterless constructor, so
        // there is no point at which a value can be passed in. The alternative
        // is a service locator for two fields read once at startup.
        App.ProbeAndExit = args.Contains("--probe");
        App.Builder = builder;

        return builder.StartWithClassicDesktopLifetime(args);
    }
}
