using Avalonia.Threading;

namespace LunaDE.Shell;

// A one-shot timer on the UI thread.
//
// DispatcherTimer already does this, and this type exists only to make the
// "fires once, then stops" contract explicit at the call site. A DispatcherTimer
// left running after its callback is a probe that keeps reporting, and the
// second report is the one that gets copied into a document by mistake.
/// <summary>A dispatcher timer that fires once and stops itself.</summary>
public sealed class DispatcherTimerShim
{
    private readonly DispatcherTimer _timer;

    /// <summary>Creates a one-shot timer. Call <see cref="Start"/> to arm it.</summary>
    /// <param name="delay">How long to wait before firing.</param>
    /// <param name="action">What to run, on the UI thread.</param>
    public DispatcherTimerShim(TimeSpan delay, Action action)
    {
        _timer = new DispatcherTimer { Interval = delay };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            action();
        };
    }

    /// <summary>Arms the timer.</summary>
    public void Start() => _timer.Start();
}
