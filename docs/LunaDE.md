# LunaDE — the design record

This file keeps its own history rather than being tidied. Where it and the code
disagree, **the code is the truth and this is the history**. A correction is a
new section or a correction subsection, never an edit that makes the record look
like it was always right.

Sections are numbered and cited from code as `§`.

---

## 1. What LunaDE is, and who owns what

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

`ShellApp.Configure<TApp>()` is the one place the windowing backend is chosen,
for the same reason LunaP keeps `LunaApp.Configure`: the alternative is the
sequence spelled out in several entry points that eventually disagree, and a
disagreement here is a session that starts on the wrong display server.

The rule is **Wayland where available, X11 otherwise**, with a
`LUNADE_BACKEND=wayland|x11` override for bisecting a fault across two display
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
three obligations. `ShellApp` therefore calls `.UseSkia().UseHarfBuzz()`
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
supervising process that restarts the shell with `LUNADE_BACKEND=x11` set.

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
| `ShellApp.Configure<App>()`, Wayland session | **`<UseWayland>b__0_0`** | **`Avalonia.Wayland.WindowImpl`** |
| `LUNADE_BACKEND=x11` | `<UseX11>b__0_0` | `Avalonia.X11.X11Window` |
| `WAYLAND_DISPLAY` unset | `<UseX11>b__0_0` | `Avalonia.X11.X11Window` |

**This retires LunaP §35.1's outstanding hazard.** That section said retiring it
honestly needed "a real window on a real Wayland session, on the Avalonia
version in use". That window has now been opened, and it was a native Wayland
window rather than an XWayland one.

An unrecognised `LUNADE_BACKEND` value throws rather than falling through to a
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
shell will need are LunaDE's to bind; Avalonia neither uses nor blocks them.

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

**Superseded in part by §5**, which settled the first two of these and narrowed
the third. Left standing as written, because §5 reads as an answer only if the
question is still here.

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
  shell can be built on it; `ShellApp` is deliberately a separate bootstrap
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

---

## 5. The panel surface — measured, 2026-08-27

§3.4 left this as Phase 0's last open question: panels need a surface role
Avalonia.Wayland does not expose, and the choice was between
`zwlr_layer_shell_v1` and a LunaDE-private protocol. It was settled by asking a
compositor rather than by argument.

### 5.1 A correction to the package id, and the version gap that was not there

NWayland 0.11.0's own README says to install **`NWayland.Protocol.Wlr`**,
singular. **That id does not exist on nuget.org.** The real package is
**`NWayland.Protocols.Wlr`**, plural. A search for the documented name returns
nothing, which reads like the package is missing.

The plan also recorded a version gap — `NWayland.Protocols.Wlr` at 0.12.5
against the 0.11.0 that Avalonia.Wayland 12.1.0 depends on. **There is no gap.**
0.11.0 of the protocol package exists and restores cleanly; 0.12.5 is merely the
latest. `LunaDE.LayerShellProbe` pins 0.11.0 deliberately, because the shell
will eventually want layer surfaces on the same connection Avalonia holds, and
two NWayland versions in one process is a problem worth never having.

### 5.2 A layer surface is drivable from C#

`LunaDE.LayerShellProbe` connects, binds `wl_compositor` and
`zwlr_layer_shell_v1`, creates a surface, requests a top-anchored full-width
strip with an exclusive zone, commits without a buffer, and waits for the
compositor to answer.

Run against KDE / `kwin_wayland` on the development machine:

    compositor advertises 68 globals
    zwlr_layer_shell_v1 advertised at name=64 version=5

    RESULT: layer surface configured by the compositor.
      serial            : 37322
      configured size   : 2560x32
      requested height  : 32
      exclusive zone    : 32

2560 is the full width of the primary output, which is what anchoring left and
right with a width of 0 asks for. The negotiation completed: configure received,
acknowledged, committed.

The bindings are complete — `ZwlrLayerShellV1` with `LayerEnum`
(Background/Bottom/Top/Overlay), and `ZwlrLayerSurfaceV1` with `SetSize`,
`SetAnchor`, `SetExclusiveZone`, `SetMargin`, `SetKeyboardInteractivity`,
`AckConfigure` and `SetLayer`. The package also carries
`zwlr_screencopy_manager_v1` and `zwlr_foreign_toplevel_manager_v1`.

**That it worked against somebody else's compositor first is the useful part.**
The finding is about the protocol and the bindings, not about a server we
control and could have accidentally written to agree with us.

### 5.3 What this does NOT prove — the remaining risk

The probe drives layer-shell on **its own Wayland connection**. Avalonia holds a
different one. Nothing here shows that **Avalonia can render into a layer
surface**, and that is the actual requirement for a panel: the surface has to
host LunaP content, not merely exist.

Three ways that could go, none of them measured:

- Get Avalonia to adopt a surface LunaDE created, which needs a seam into
  `Avalonia.Wayland` internals that is not currently public.
- Teach `Avalonia.Wayland` layer-shell upstream, which is the clean answer and
  the slow one.
- Draw panel content without Avalonia, which abandons LunaP for exactly the
  surfaces the shell is mostly made of.

**This is the shape of mistake §4.1 records**, so it is written down before it
can be made again: the protocol works, and "the protocol works" is not "the
panel works". The next measurement is a LunaP control rendering inside a layer
surface, and until somebody takes it, the panel path is a hazard rather than a
behaviour.

---

## 6. Can Avalonia render into a layer surface? Not through any public API.

§5.3 named this as the live risk and said the next measurement was a LunaP
control drawn inside a layer surface. Before attempting it, the cheaper question
was asked first: **is there a seam to attempt it through?** There is not.

### 6.1 The measurement

`Avalonia.Wayland` 12.1.0 contains **275 types and exports 8**:

    Avalonia.AvaloniaWaylandPlatformExtensions   (UseWayland)
    Avalonia.WaylandPlatformOptions
    Avalonia.Wayland.AvaloniaWaylandException    (and five sibling exceptions)

The other **267 are internal**, and they include every type that would matter:
`WSurface`, `IWSurface`, `WXdgShellSurface`, `IWXdgShellSurface`, `WXdgTopLevel`,
`WaylandTopLevelFactory`, `WaylandSurfaceCreateResult<T>`.

`WaylandPlatformOptions` — the one public knob — carries `WlDisplayName`,
`DisplayFd`, `EnableReconnects`, `ForceDrawnDecorations`, `GlProfiles`,
`UseDmabufSwapchain`, `UseGLibMainLoop` and
`ExternalGLibMainLoopExceptionLogger`. No surface factory, no role selection, no
adoption hook. Searching every public member of all eight exported types for
`Factory`, `Layer`, `Role` or `Surface` returns nothing.

`InternalsVisibleTo` does not help either. The grants go to Avalonia's own test
assemblies and to the XPF/WPF shims, and every one is strong-name signed, so a
consumer cannot join the list.

### 6.2 What that rules out, and what remains

**Ruled out: surface adoption.** The plan's first candidate — LunaDE creates a
`wl_surface`, gives it a layer role, and hands it to Avalonia to draw into —
requires a public seam that does not exist. Reaching it by reflection would mean
depending on 267 internal types across every future Avalonia release, which is
not a foundation for a desktop environment.

**Remaining, none of them measured:**

1. **Upstream layer-shell support in `Avalonia.Wayland`.** The clean answer. The
   repository is MIT and public, the backend already speaks xdg-shell, and a
   layer-shell role is the same shape of work its `WXdgTopLevel` already does.
   Slow, and it puts LunaDE's panel schedule behind somebody else's review.
2. **Render offscreen and blit into a layer-surface buffer.** LunaP draws to a
   bitmap; LunaDE copies it into a `wl_shm` or dmabuf buffer on the layer
   surface it already knows how to create (§5.2). Keeps every LunaP control.
   Costs a copy per frame and needs input forwarded back by hand — acceptable
   for a 2560x32 panel, questionable for a full-screen lock surface.
3. **Draw shell surfaces without Avalonia.** Abandons LunaP for exactly the
   surfaces the shell is mostly made of, which defeats the reason LunaP is being
   grown into a desktop toolkit at all.

### 6.3 What this does to the plan

Phase C assumed LunaP could grow shell surfaces as a toolkit feature. It cannot,
not on its own: **the blocker is in Avalonia's backend, one layer below LunaP,
and no amount of work inside LunaP reaches it.** LunaP's own rule — it references
Avalonia and nothing else — is not the obstacle here and must not be blamed for
it; the obstacle is that Avalonia.Wayland has no extension point for anyone.

The LunaP seam is still worth building and is not blocked by any of this. It is
what lets a host choose the windowing backend at all, and every route above
needs it. It just does not, by itself, produce a panel.

**Recorded as a hazard rather than a plan:** route 2 is the only one LunaDE can
take unilaterally, and nobody has measured whether a blitted LunaP surface holds
the frame budget or whether forwarded input feels right. Until somebody does,
"panels are possible" is a belief.

---

## 7. Building the Avalonia fork on Fedora

§6.2 chose the fork. This is what it takes to compile it here, recorded because
none of it is discoverable from the error messages and all four steps were found
the hard way.

The fork lives at `~/Projects/Avalonia`, branch `lunade/layer-shell`, from
`AvaloniaUI/Avalonia`. **The working tree carries no local modifications** -
every accommodation below is a build flag or an environment variable, never an
edit. That is deliberate: a change in the tree is a change that has to be
explained to upstream later, and none of these belong there.

### 7.1 The SDK is not available from the package manager

`global.json` pins **10.0.201**. Fedora 44 ships **10.0.111** and that is the
newest `dnf` has - `dnf check-update` reports nothing pending.

Microsoft's Fedora repositories are empty. Checked across `fedora/44`,
`fedora/42` and `fedora/41`: each returns the same ~612-byte stub `repomd.xml`
with zero packages. And the release metadata for 10.0.400 lists **16 artifacts,
none of them an rpm** - `tar.gz` for every Linux RID, `.pkg` for macOS,
`.exe`/`.zip` for Windows. Microsoft no longer publishes distro packages for
.NET.

So the SDK comes from the tarball, installed per-user:

    curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0
    # installs to ~/.dotnet; build with ~/.dotnet/dotnet

`global.json` already carries `rollForward: latestFeature`, so 10.0.400
satisfies the 10.0.201 pin with **no edit to the file**. An earlier attempt
lowered the pin instead; that was wrong and was reverted.

### 7.2 Submodules are not optional

XamlX and Avalonia.DBus are git submodules. Without them the build produces
**126 errors**, all of them `CS0246: The type or namespace name 'XamlX' could
not be found`, which reads like a broken checkout rather than a missing
submodule.

    git submodule update --init --recursive

A `--depth 1 --filter=blob:none` clone does not bring them.

### 7.3 Strong-name signing collides with Fedora's crypto policy

.NET strong-name signing uses **SHA-1**, and it is not a choice - the digest is
fixed by the format. Fedora's default crypto policy sets
`rh-allow-sha1-signatures = no` in `/etc/crypto-policies/back-ends/opensslcnf.config`,
so OpenSSL 3.5 refuses:

    Interop+Crypto+OpenSslCryptographicException: error:03000098:
    digital envelope routines::invalid digest

**Turning signing off does not work.** `-p:SignAssembly=false` produces
**298 errors**, beginning with `CS0538: 'IClickableControl' in explicit
interface declaration is not an interface` - Avalonia's `InternalsVisibleTo`
grants are keyed on strong names, so removing them removes the internals.

The fix is a private OpenSSL config for the build process only:

    sed 's/^rh-allow-sha1-signatures.*/rh-allow-sha1-signatures = yes/' \
      /etc/crypto-policies/back-ends/opensslcnf.config > /tmp/os-sha1.config
    sed "s#^\.include = /etc/crypto-policies/back-ends/opensslcnf.config#.include = /tmp/os-sha1.config#" \
      /etc/pki/tls/openssl.cnf > /tmp/openssl-sha1.cnf
    OPENSSL_CONF=/tmp/openssl-sha1.cnf ~/.dotnet/dotnet build ...

Scoped to one process. **Do not** reach for
`update-crypto-policies --set LEGACY`: it weakens every TLS decision on the
machine, permanently, to fix one build. Note that .NET strong names are not a
security boundary - .NET Core does not verify them - so the SHA-1 here is a
legacy format constant rather than a trust decision. That is the whole argument
for permitting it, and it does not extend to anything else.

### 7.4 A failed signing run leaves a corrupt analyzer, and the error blames the wrong thing

This one cost the most time and is worth the entry on its own.

After the signing failure above, the build had written a **truncated
`DevGenerators.dll`**. Every subsequent build then failed with:

    error CS8795: Partial method 'KnownColors.GetKnownColors()' must have an
    implementation part because it has accessibility modifiers.

which points at `src/Avalonia.Base/Media/KnownColors.cs` and suggests a source
problem. The real cause only appeared under `-v n`, as a **warning**:

    warning CS8034: Unable to load Analyzer assembly .../DevGenerators.dll :
    System.BadImageFormatException: PE image doesn't contain managed metadata.

The generator could not load, so `[GenerateEnumValueDictionary]` emitted
nothing, so the partial had no implementation. **The error and its cause were
in different projects, and the cause was a warning while the symptom was an
error.**

Two rules from it. Fix signing before believing any other error in this tree.
And when a generated member goes missing, check `-v n` for `CS8034` before
reading the source at all - a clean `-t:Rebuild` after fixing signing produced
66 generated files and a clean compile.

### 7.5 The command that works, and the script that recreates it

`tools/fedora-build-env.sh` does everything below and writes the OpenSSL config
into `~/.config/lunade/` rather than `/tmp`, because the `/tmp` copies from the
first session did not survive to the second.

    . tools/fedora-build-env.sh
    $DOTNET build ~/Projects/Avalonia/src/Avalonia.Wayland/Avalonia.Wayland.csproj

By hand:

    OPENSSL_CONF=/tmp/openssl-sha1.cnf ~/.dotnet/dotnet build \
      src/Avalonia.Wayland/Avalonia.Wayland.csproj -t:Rebuild

Produces `Avalonia.Wayland.dll` for `net10.0` and `net8.0`. Verified
2026-08-27.
