# DollyDE — the design record

This file keeps its own history rather than being tidied. Where it and the code
disagree, **the code is the truth and this is the history**. A correction is a
new section or a correction subsection, never an edit that makes the record look
like it was always right.

Sections are numbered and cited from code as `§`.

---

## 1. What DollyDE is, and who owns what

A desktop environment, not a distribution. Five languages, one job each:

| Layer | Language | Owns |
|---|---|---|
| Compositor | Rust, Smithay, from `anvil` | Mechanism: surfaces, input, DRM, rendering, and every visual effect |
| Scriptable core | C#, DianaOS | Interpreter, Unix commands, users, sessions, sandbox, man pages |
| State model | F# | The system state model |
| Shell | C#, Avalonia, LunaP | Policy: panels, launcher, workspaces, OSD, settings |
| Persistence | SQLite | Application catalogue, search index, durable state |
| Tooling | Python | Codegen, harnesses, packaging. **Never shipped in the runtime.** |

The split is the design spine: **Rust owns mechanism, C# owns policy.** The
tension to watch is policy drifting into Rust, because it is always slightly
easier to put it there.

**The GUI is a view over a scriptable core.** Nothing the shell can do may be
impossible from a script. This is why the IPC spine is built before the shell.

---

## 2. Bootstrap: Wayland first, X11 fallback

`DollyApp.Configure<TApp>()` is the one place the windowing backend is chosen,
for the same reason LunaP keeps `LunaApp.Configure`: the alternative is the
sequence spelled out in several entry points that eventually disagree, and a
disagreement here is a session that starts on the wrong display server.

The rule is **Wayland where available, X11 otherwise**, with a
`DOLLYDE_BACKEND=wayland|x11` override for bisecting a fault across two display
servers without a logout.

### 2.1 Why `UsePlatformDetect` is never called

It cannot select Wayland. Two facts, both measured, that together look like a
contradiction and are not:

1. **Avalonia's platform detection resolves to X11 on Linux.** LunaP's
   `docs/LunaP.md` §35.1 established this on 12.1.0 by reflecting the builder's
   `WindowingSubsystemInitializer`: `UsePlatformDetect()` and
   `UsePlatformDetect().UseX11()` install the identical initializer,
   `<UseX11>b__0_0`.
2. **An official Wayland backend exists.** `Avalonia.Wayland` 12.1.0 — authors
   "Avalonia Team", repository `AvaloniaUI/Avalonia` at commit `a21b9f5`, MIT —
   ships `UseWayland()` and `WaylandPlatformOptions`.

They reconcile structurally: **`Avalonia.Desktop`, the package platform
detection ships in, does not reference `Avalonia.Wayland`.** Measured from its
`.nuspec` on 2026-08-27: it depends on Avalonia.Native, Avalonia, Avalonia.X11,
Avalonia.HarfBuzz, Avalonia.Skia and Avalonia.Win32. The Wayland backend is not
in the dependency graph unless you put it there. **It is opt-in, not absent.**

### 2.2 `UsePlatformDetect` installs three subsystems, not one

Found by running the Phase 0 probe, 2026-08-27, and worth recording because it
is not obvious from the name and it bites immediately.

Replacing `UsePlatformDetect()` with `UseWayland()` produced, in order:

    System.InvalidOperationException: No rendering system configured. Consider calling UseSkia().
    System.InvalidOperationException: No text shaping system configured. Consider calling UseHarfBuzz().

So platform detection selects a **windowing** subsystem *and* installs
**rendering** and **text shaping**. Anything replacing that call inherits all
three obligations. `DollyApp` therefore calls `.UseSkia().UseHarfBuzz()`
explicitly.

LunaP §35.2 records the same coupling from the other side: its `BootstrapTests`
prove `UsePlatformDetect` is in the chain through what it installs —
`RenderingSubsystemName` and `TextShapingSubsystemName` — rather than through
its own name.

### 2.3 What the fallback keys on, and what it does not do — a hazard

The rule is "`WAYLAND_DISPLAY` is set", plus the override. It is deliberately
**not** "try Wayland, catch the failure, retry X11".

The backend is installed on the builder in `Configure`, and only initialised
later inside `Setup`. A failure therefore surfaces after `Configure` has
returned and cannot be caught at that level. Recovering honestly needs a
supervising process that restarts the shell with `DOLLYDE_BACKEND=x11` set.

**That supervisor is not built.** A Wayland session whose compositor advertises
a display but refuses the connection will fail to start rather than fall back.
This is a hazard, not a behaviour, and it is written here so that it is not
rediscovered as a bug.

---

## 3. Phase 0 measurements, 2026-08-27

Taken on: Fedora, kernel 7.1.10, KDE / `kwin_wayland`, .NET SDK 10.0.111,
Avalonia 12.1.0, two outputs (2560x1440 and 1920x1080), both at scale 1.

### 3.1 The backend actually selected

The instrument is `BackendReport`, which reads back off the live objects rather
than trusting what the bootstrap intended. Two independent readings agree.

| Bootstrap | Windowing initializer | Window platform impl |
|---|---|---|
| `UsePlatformDetect()` — LunaP §35.1 | `<UseX11>b__0_0` | not taken there |
| `UsePlatformDetect().UseX11()` — LunaP §35.1 | `<UseX11>b__0_0` | not taken there |
| `DollyApp.Configure<App>()`, Wayland session | **`<UseWayland>b__0_0`** | **`Avalonia.Wayland.WindowImpl`** |
| `DOLLYDE_BACKEND=x11` | `<UseX11>b__0_0` | `Avalonia.X11.X11Window` |
| `WAYLAND_DISPLAY` unset | `<UseX11>b__0_0` | `Avalonia.X11.X11Window` |

**This retires LunaP §35.1's outstanding hazard.** That section said retiring it
honestly needed "a real window on a real Wayland session, on the Avalonia
version in use". That window has now been opened, and it was a native Wayland
window rather than an XWayland one.

An unrecognised `DOLLYDE_BACKEND` value throws rather than falling through to a
guess, because a typo that silently picks a backend is a typo nobody finds.

### 3.2 Protocols the Wayland backend binds

Read from `WAYLAND_DEBUG=1` traffic, filtered to `bind` rather than `global`, so
this is what Avalonia **took** and not what KWin **offered**. Verified with a
sanity control: `wl_compositor` must appear, and does.

    wl_compositor                  wp_fractional_scale_manager_v1
    wl_data_device_manager         wp_presentation
    wl_fixes                       wp_viewporter
    wl_output                      xdg_wm_base
    wl_seat                        zwp_linux_dmabuf_v1
    wl_shm                         zwp_text_input_manager_v3
    zxdg_decoration_manager_v1     zxdg_exporter_v2
    zxdg_output_manager_v1

Notable in what is **absent**: no layer-shell, and no `ext_*` protocol at all.
The workspace, foreign-toplevel, idle-notifier and screencopy protocols that the
shell will need are DollyDE's to bind; Avalonia neither uses nor blocks them.

`wp_presentation` being bound is worth keeping in view — presentation-time
feedback is the honest instrument for the compositor frame budget in §5.

### 3.3 Fractional scaling

**`wp_fractional_scale_manager_v1` is bound**, together with `wp_viewporter`,
which is the companion a client needs to actually apply a fractional scale.

The probe reported `RenderScaling: 1` and both screens at `scaling 1`. That is
**not** evidence against fractional scaling: this machine has both outputs at
100%. The protocol is taken; the value has simply never been anything else here.

**This retires the "integer scaling only, 100% and 200%" mitigation** that the
plan carried while the X11 path was assumed. That constraint now applies only to
the X11 fallback.

**Still unmeasured:** a fractional scale actually rendering correctly. Setting an
output to 125% or 150% and reading `RenderScaling` back is a measurement nobody
has taken. Hazard, not behaviour.

### 3.4 What Phase 0 has not settled

- **How panels get a surface role.** Avalonia.Wayland exposes xdg-shell
  toplevels and popups; the string `layer` does not appear in its public API.
  KWin advertises `zwlr_layer_shell_v1`, so the protocol is testable on this
  machine today, but nothing has driven it from C# yet.
- **`NWayland.Protocols.Wlr`** exists on nuget.org at 0.12.5 and is presumed to
  carry layer-shell. Nobody has looked inside it. Note the version gap:
  Avalonia.Wayland 12.1.0 depends on NWayland **0.11.0**, and the protocol
  packages are at **0.12.5**.
- **LunaP cannot reach any of this yet.** `LunaApp.Configure` hardcodes
  `UseX11()` on Linux (LunaP §3, §35.1). The toolkit needs a seam before the
  shell can be built on it; `DollyApp` is deliberately a separate bootstrap
  rather than a patch to LunaP, because LunaP must stay cross-platform and
  reference Avalonia and nothing else.

---

## 4. Corrections

### 4.1 The plan's first conclusion about XWayland was wrong

Revision 1 of the phase plan concluded from LunaP §35.1 that the shell "will be
an XWayland client". The measurement was correct; the sentence built on it was
not. §35.1 measured what platform detection resolves to, and nothing more.
§2.1 above is what is actually true. Recorded rather than deleted, because the
older statement is what makes §2.1 legible.

### 4.2 Two broken measurement methods, both caught by a control

Neither produced a wrong entry in this document, because both were caught, and
they are recorded so the method is not repeated.

- **`grep -x` against packed assembly metadata.** A presence check for Wayland
  protocol names in `NWayland.dll` reported every one absent, including names
  already known to be present. The names sit inside packed metadata strings, not
  on lines of their own, and `-x` requires a whole-line match. Clean, confident,
  entirely false output. Fixed by substring matching plus a control that must be
  present.
- **`BackendReport.ReadWindowingInitializer` with the wrong binding flags.** The
  first version searched only for a non-public *instance* member and returned
  "unavailable", which reads like a finding about Avalonia and was a bug in the
  instrument. It now searches every visibility, and on failure lists the members
  whose names contain "Windowing" so the next reader is not guessing.

**The rule both produce:** a check that reports absence must first prove it can
report presence.
