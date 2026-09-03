using NWayland.Protocols.Wayland;
using NWayland.Protocols.Wlr.WlrLayerShellUnstableV1;

namespace LunaDE.LayerShellProbe;

// CAN C# DRIVE A PANEL SURFACE? Phase 0's last open question.
//
// The shell needs a surface role Avalonia.Wayland does not expose: something
// that anchors to a screen edge, sits above ordinary windows, and reserves
// space so maximised windows do not sit underneath it. zwlr_layer_shell_v1 is
// the protocol that does that, and the alternative is LunaDE defining its own.
// Choosing between them on argument alone is how a plan acquires an assumption,
// so this program asks the compositor instead.
//
// WHAT COUNTS AS SUCCESS HERE, precisely: the compositor accepts a layer
// surface and sends back a configure with a size it chose. That is the whole
// negotiation. No buffer is attached and nothing is drawn - a layer surface is
// required to commit once WITHOUT a buffer, receive configure, acknowledge it,
// and only then attach. Stopping at the acknowledgement tests the protocol path
// and nothing else, which is the point: a probe that also rendered would fail
// for reasons that have nothing to do with the question.
//
// This runs against whatever compositor is present - KWin on the development
// machine today, LunaDE's own Smithay compositor later. That it works on
// somebody else's compositor first is a feature: it means the finding is about
// the protocol and the bindings rather than about our own server.
internal static class Program
{
    private const string SurfaceNamespace = "lunade-probe";
    private const uint PanelHeight = 32;

    private static int Main()
    {
        using var display = WlDisplay.Connect(null, default);
        if (display is null)
        {
            Console.Error.WriteLine("FAIL: could not connect to a Wayland display. Is WAYLAND_DISPLAY set?");
            return 1;
        }

        var globals = new GlobalCollector();
        var registry = display.GetRegistry(globals, null);

        // One roundtrip is what turns "the registry exists" into "the registry
        // has told us what it has". Binding before this returns nothing.
        display.Roundtrip();

        Console.WriteLine($"compositor advertises {globals.Count} globals");

        if (globals.LayerShell is not { } layerShellGlobal)
        {
            // A real answer, not a crash. A compositor without layer-shell is
            // exactly the case the LunaDE-private-protocol option exists for.
            Console.WriteLine("RESULT: zwlr_layer_shell_v1 is NOT advertised by this compositor.");
            return 2;
        }

        Console.WriteLine($"zwlr_layer_shell_v1 advertised at name={layerShellGlobal.Name} version={layerShellGlobal.Version}");

        if (globals.Compositor is not { } compositorGlobal)
        {
            Console.Error.WriteLine("FAIL: no wl_compositor. Nothing can have a surface without one.");
            return 1;
        }

        var compositor = WlCompositor.Bind(registry, compositorGlobal.Name, compositorGlobal.Version, null, null);
        var layerShell = ZwlrLayerShellV1.Bind(registry, layerShellGlobal.Name, layerShellGlobal.Version, null, null);

        var surface = compositor.CreateSurface(null, null);
        var configured = new ConfigureWatcher();

        // output: null means "compositor picks". A real panel names its output;
        // a probe that named one would be testing output enumeration too.
        var layerSurface = layerShell.GetLayerSurface(
            surface, null, ZwlrLayerShellV1.LayerEnum.Top, SurfaceNamespace, configured, null);

        // A top-anchored strip spanning the full width - the shape of a panel.
        // Width 0 with left+right anchors means "as wide as the output", which
        // is the protocol's way of saying it rather than a magic number.
        layerSurface.SetSize(0, PanelHeight);
        layerSurface.SetAnchor(
            ZwlrLayerSurfaceV1.AnchorEnum.Top |
            ZwlrLayerSurfaceV1.AnchorEnum.Left |
            ZwlrLayerSurfaceV1.AnchorEnum.Right);

        // The exclusive zone is the part that makes it a panel rather than an
        // overlay: it is how much space maximised windows must leave alone.
        layerSurface.SetExclusiveZone((int)PanelHeight);

        surface.Commit();
        display.Flush();

        // Dispatch until the compositor answers or we give up. A bare Roundtrip
        // would also work today; the loop exists so that a compositor which
        // simply never answers produces a timeout that says so, rather than a
        // hang somebody has to interrupt and interpret.
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!configured.Configured && DateTime.UtcNow < deadline)
            display.Roundtrip();

        if (!configured.Configured)
        {
            Console.WriteLine("RESULT: layer surface created, but the compositor sent no configure within 3s.");
            return 3;
        }

        layerSurface.AckConfigure(configured.Serial);
        surface.Commit();
        display.Flush();

        Console.WriteLine();
        Console.WriteLine("RESULT: layer surface configured by the compositor.");
        Console.WriteLine($"  serial            : {configured.Serial}");
        Console.WriteLine($"  configured size   : {configured.Width}x{configured.Height}");
        Console.WriteLine($"  requested height  : {PanelHeight}");
        Console.WriteLine($"  exclusive zone    : {PanelHeight}");
        Console.WriteLine();
        Console.WriteLine("A panel surface is drivable from C# on this compositor.");

        layerSurface.Destroy();
        surface.Destroy();
        return 0;
    }
}

/// <summary>A global the compositor advertised, kept with the values needed to bind it.</summary>
/// <param name="Name">The registry name.</param>
/// <param name="Version">The version the compositor offers.</param>
internal readonly record struct WaylandGlobal(uint Name, uint Version);

// Collects the registry advertisements this probe cares about.
//
// It keeps the version the COMPOSITOR offered rather than a constant, because
// binding a version the server does not implement is a protocol error that
// disconnects the client - and the resulting failure looks like the protocol is
// missing rather than like the client asked wrongly.
internal sealed class GlobalCollector : WlRegistry.Listener
{
    /// <summary>How many globals were advertised in total.</summary>
    public int Count { get; private set; }

    /// <summary>The <c>wl_compositor</c> global, if advertised.</summary>
    public WaylandGlobal? Compositor { get; private set; }

    /// <summary>The <c>zwlr_layer_shell_v1</c> global, if advertised.</summary>
    public WaylandGlobal? LayerShell { get; private set; }

    /// <inheritdoc />
    protected override void Global(WlRegistry eventSender, uint name, string @interface, uint version)
    {
        Count++;

        switch (@interface)
        {
            case "wl_compositor":
                Compositor = new WaylandGlobal(name, version);
                break;
            case "zwlr_layer_shell_v1":
                LayerShell = new WaylandGlobal(name, version);
                break;
        }
    }
}

// Records the one configure the probe is waiting for.
internal sealed class ConfigureWatcher : ZwlrLayerSurfaceV1.Listener
{
    /// <summary>Whether the compositor has sent a configure.</summary>
    public bool Configured { get; private set; }

    /// <summary>The serial to acknowledge.</summary>
    public uint Serial { get; private set; }

    /// <summary>The width the compositor chose.</summary>
    public uint Width { get; private set; }

    /// <summary>The height the compositor chose.</summary>
    public uint Height { get; private set; }

    /// <inheritdoc />
    protected override void Configure(ZwlrLayerSurfaceV1 eventSender, uint serial, uint width, uint height)
    {
        Configured = true;
        Serial = serial;
        Width = width;
        Height = height;
    }
}
