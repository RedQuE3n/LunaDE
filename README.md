# DollyDE

A desktop environment for Linux. Wayland first, X11 as a fallback.

A Smithay compositor in Rust owns mechanism — surfaces, input, DRM, rendering,
and every visual effect. Managed code owns policy: a scriptable core in C#, a
system state model in F#, and a shell built on [LunaP](https://github.com/RedQuE3n/EmuSen.LunaP)
over Avalonia. The GUI is a view over the scriptable core; nothing the shell can
do is meant to be impossible from a script.

`docs/DollyDE.md` is the design record. It is section-numbered, cited from code
as `§`, and it keeps its own history rather than being tidied.

## Status

**Phase 0.** Establishing that the Wayland path works before anything is built
on it. It does — see `docs/DollyDE.md` §3.

## Building

    dotnet build DollyDE.slnx

## The backend probe

Phase 0 ships one program, and its job is to answer "which display server did
this process actually end up on, and what can it do there?" — by reading back
off the live objects rather than trusting what the bootstrap intended.

    dotnet run --project src/DollyDE.Shell -- --probe

It prints a report and exits. Without `--probe` it opens the window and stays
open.

To force a backend — useful for bisecting a rendering fault across two display
servers without logging out:

    DOLLYDE_BACKEND=x11     dotnet run --project src/DollyDE.Shell -- --probe
    DOLLYDE_BACKEND=wayland dotnet run --project src/DollyDE.Shell -- --probe

An unrecognised value is refused rather than guessed.

To see which Wayland protocols the backend actually binds:

    WAYLAND_DEBUG=1 dotnet run --project src/DollyDE.Shell -- --probe 2>&1 \
      | grep -oE '\.bind\([0-9]+, "[a-z_0-9]+"'

## The layer-shell probe

Phase 0's other question: can a panel surface be driven from C#? This asks the
running compositor rather than arguing about it.

    dotnet run --project src/DollyDE.LayerShellProbe

It binds `zwlr_layer_shell_v1`, requests a top-anchored full-width strip with an
exclusive zone, and reports the size the compositor configured. It draws
nothing — see `docs/DollyDE.md` §5.3 for what that does and does not prove.

Exit codes: `0` configured, `2` the compositor has no layer-shell, `3` no
configure arrived within three seconds.

## Requirements

.NET 10 SDK. On the Wayland path, `libwayland-client.so.0` and
`libxkbcommon.so.0`.
