# Verification scope

Development host: Windows 10 Pro build 19045, NVIDIA GeForce RTX 2060, LG ULTRAGEAR reporting 2560 × 1080 at 144 Hz and approximately 34.22 inches. Build toolchain: .NET SDK 10.0.402, installed under the task's work directory.

## Evidence recorded

- Core tests were written before implementation, observed failing, then passed. A review found a landscape/portrait normalization defect; the added regression failed before the fix. Core Release suite: **11/11 passed**.
- Initial Windows component tests were observed failing **0/4**, then **4/4 passed**: cleanup order/idempotence, continued recovery after an action fails, exact driver modes, and invalid mode rejection.
- The WPF editor was built and launched through a read-only screenshot mode. It detected the physical monitor and showed two centered **1280 × 720** regions, separate render/refresh summaries, and disabled Apply while the driver was absent. No driver was installed by this check.
- The native Direct3D 11 presentation smoke test captured one physical output and drew it into two regions in a temporary non-activating window. The recorded run presented **147 frames** during a roughly two-second sampling interval. This is a functional smoke check, not a latency or sustained-FPS benchmark.
- Bundled driver DLL/catalog Authenticode signatures were reported **Valid** by Windows. Fixed file hashes are recorded in THIRD-PARTY-NOTICES.md and checked by the installer.
- A self-contained win-x64 publish was produced with its runtime and driver assets. Final release commands and review regression results are recorded with the packaged release.

## Final release checks — 17 October 2026

- Core Release tests: **11/11 passed**.
- Windows Release tests: **5/5 groups passed**, including confirmation boundaries/close, delayed readiness, topology conflicts/partial enable/already-restored state, all-step cleanup, watchdog readiness, fake install failure/retry, authenticated fake IPC failure/retry, UAC-decline/connection timeout, disposal, and bounded exclusivity. The IPC tests exercise local named pipes with fake helpers; they do not install drivers.
- App Release build with warnings as errors: **0 warnings, 0 errors**.
- Self-contained Windows x64 publish: **passed**.
- Published executable read-only monitor diagnostics: **exit 0**; managed driver absent, physical output unchanged.
- Published native presentation smoke: **exit 0**, **164 presented frames** in its sample interval, two regions from one physical capture stream. This is not a sustained-FPS benchmark.
- Published editor screenshot and ordinary window-close smoke: **exit 0**. The smoke calls the normal Close path and has a bounded 30-second process-exit check. The screenshot was visually inspected at original detail.
- Independent code review after corrections: acceptable as an **experimental engineering build**, with no remaining demonstrated P1/P2 blocker. This verdict does not establish hardware activation or recovery.

## Setup button regression — 18 October 2026, v0.1.1

- Reproduced the pre-elevation account guard rejecting the current unelevated token before a UAC prompt could appear. The editor hid that error in the scrolled inspector.
- Setup and Apply now restart the editor with Windows `runas`, transfer the edited profile and selected display, and resume the requested action. Helper IPC remains restricted to the elevated editor's account. Cancellation, operation failures, and elevated startup failures show dialogs.
- Two new elevation test groups were first observed failing, then passed. Windows Release tests: **7/7 groups passed**. App Release build with warnings as errors: **0 warnings, 0 errors**. Self-contained x64 publish succeeded.
- Published v0.1.1 diagnostics and editor/ordinary-close smoke: **exit 0**. Screenshot inspected at original detail, retaining the saved 75% game/portrait profile. Diagnostics reported the managed driver absent and no display settings mutation.
- Independent review of the elevation fix: approved with no important defects found. Review was read-only.
- Real UAC approval and driver installation were not automated. The user must approve the Windows prompt to exercise installation. Native activation/recovery limitations below still apply.

## Hardware validation limits

The original release checks did not install the driver or mutate display topology. Later user tests completed Setup, activated both virtual modes, and recorded recovery, as described below. Sustained virtual capture/presentation with v0.1.4, mouse/game focus, fullscreen/exclusive games, anti-cheat, per-screen target behavior, parent/helper crash recovery, unplug/reconnect, elevated apps, high-refresh latency, and multi-GPU operation still need live validation. Version 0.1.4 intentionally removes the confirmation countdown; historical confirmation results below describe earlier builds.

The app includes implementation paths and deterministic safety tests for these operations; a fake-backed test is not a replacement for the hardware test. Keep the **experimental** label until a tester records those results.

## Driver binding regression — 18 October 2026, v0.1.2

- Root cause: WMI reported `IsSigned=False` for the exact provider/version after Windows accepted and started the package. SetupAPI recorded success at 03:41:23; Kernel-PnP recorded both virtual monitor devices starting. SplitDeck then removed the device at 03:41:26 because its verification required the inventory flag to be true.
- Replaced that inventory trust decision with verification of the bound Driver Store package: pinned SHA-256 for INF/DLL/catalog, plus native Windows Authenticode trust for DLL and catalog. Device Manager problem codes are queried through Configuration Manager; only healthy (0) or intentionally disabled (22) devices qualify. Missing devices and signature enforcement failures remain blocked.
- Setup briefly waits for binding/device enumeration, and reports the exact device ID and verification detail when it fails. A requested restart rolls back the attempt and instructs the user to restart then rerun Setup.
- Direct-verification tests were first observed failing, then passed. Windows Release tests: **12/12 groups passed**, including trusted bundled files, tampered/unsigned rejection, absent/unhealthy devices, delayed enumeration, and the actual staged `oem38.inf` package resolved through SetupAPI. App warnings-as-errors build: **0 warnings, 0 errors**. Independent read-only review approved the fix without important defects.
- These fix checks were read-only apart from task files: no new UAC, driver installation, or layout activation was performed. See hardware validation limits above.
- Self-contained v0.1.2 publish succeeded. Published diagnostics and editor/normal-close smoke both exited **0**; the screenshot was inspected at original detail. Diagnostics show the original LG physical output and no active managed device, consistent with the prior rollback.

API references: [SetupGetInfDriverStoreLocation](https://learn.microsoft.com/en-us/windows/win32/api/setupapi/nf-setupapi-setupgetinfdriverstorelocationw), [Windows trust verification data](https://learn.microsoft.com/en-us/windows/win32/api/wintrust/ns-wintrust-wintrust_data), and [device problem codes](https://learn.microsoft.com/en-us/windows-hardware/drivers/debugger/device-manager-problem-codes).

## Black presentation regression — 18 October 2026, v0.1.3

- The user completed Setup v0.1.2 and attempted Apply. Read-only diagnostics observed the LG output moved above two active virtual modes (1920 × 1080 at 144 Hz and 603 × 1072 at 30 Hz). Both recorded session journals subsequently reached Complete=true with the original physical layout restored. This demonstrates those attempts' restoration, not every recovery scenario.
- Found the presenter bound its render target only once. Flip-model Present detaches back buffer 0, so subsequent frames cleared the buffer but drew no desktop. The frame counter kept advancing and the prior smoke test missed the failure.
- A native graphics regression queried the actual bound render target after successive presents. It failed before the fix (**12/13** groups passing) and passed after reattaching the target each frame (**13/13** groups passing). App Release warnings-as-errors build: **0 warnings, 0 errors**.
- Presentation diagnostics now read back the GPU back buffer at frame 20 into a PNG. The corrected renderer captured the physical desktop into two regions, exited **0**, and presented **65 frames** in its sample. The GPU image was inspected and contained the desktop in both regions. This does not establish sustained virtual-display presentation or games.
- Independent read-only code review approved the renderer fix and readback without important defects. Windows app-control tooling failed to initialize during this investigation; the checks used the app's own native diagnostic mode and read-only session/process information.
- The self-contained v0.1.3 executable passed monitor diagnostics, editor/ordinary-close smoke, and native presentation smoke, all with exit **0**. The packaged renderer presented **96 frames**, and its GPU readback image was inspected and showed desktop content in both regions.

API reference: [DXGI Present and flip-model target unbinding](https://learn.microsoft.com/en-us/windows/win32/api/dxgi/nf-dxgi-idxgiswapchain-present).

## Direct Apply — 18 October 2026, v0.1.4

- User requested removal of the missing Keep popup as unnecessary. Removed its UI, countdown state, timed rollback, Keep editor button, and Keep IPC. Apply now stays active until an explicit restore, editor close/loss, or a capture/presentation failure.
- Emergency hotkey registration is independent of the removed confirmation flag and remains enabled by default for live presentation. Machine lease, watchdog readiness, recovery journal, parent lifetime, renderer progress checks, and recovery stack remain in place.
- A native regression with the previous default presenter failed because it stopped after the confirmation deadline (**13/14 groups passed**). With direct Apply it stayed active past 16 seconds, showed no confirmation form, and reserved Ctrl+Alt+Shift+F12 (**14/14 groups passed**). The other recovery regressions remain passing. App Release warnings-as-errors build: **0 warnings, 0 errors**.
- Self-contained win-x64 build passed with warnings as errors. The v0.1.4 folder reuses unchanged dependency files through NTFS hard links while keeping its application binaries independent. Packaged monitor diagnostics, editor/ordinary-close smoke, and physical presentation smoke all exited **0**; presentation recorded **84 frames**. Editor and rendered back-buffer images were inspected. Independent review found no important regressions.
- No virtual topology changes were made during v0.1.4 verification. Live direct-Apply persistence and full gaming behavior remain to be confirmed on the virtual displays.

## Reproduce component checks

```powershell
dotnet run --project tests/SplitDeck.Core.Tests -c Release
dotnet run --project tests/SplitDeck.Windows.Tests -c Release
dotnet build src/SplitDeck.App -c Release --warnaserror
dotnet publish src/SplitDeck.App -c Release -r win-x64 --self-contained true -o artifacts/windows-x64
```

Physical presentation check, which does not alter display topology:

```powershell
./artifacts/windows-x64/SplitDeck.exe --diagnose ./diagnostics.json
./artifacts/windows-x64/SplitDeck.exe --presentation-smoke ./presentation.json
./artifacts/windows-x64/SplitDeck.exe --ui-smoke ./editor.png
```

## Live validation required before a stable release

Use the app's explicit Setup action and normal Windows elevation, then test a native-resolution layout before high render resolutions. Confirm that Windows enumerates the physical output plus two virtual outputs and that both virtual images are visible and interactive. Confirm the split stays active without a Keep prompt, then exercise Restore desktop, emergency restore, normal editor close, editor kill, helper kill, changed display settings, conflicting sessions, unsupported modes, and unplug/reconnect. Record the original and restored topology for each case. Finally test a fullscreen game alongside Discord and measure actual rendering/input behavior. Do not infer game compatibility from the editor or the physical-output smoke check.

## Performance repair candidate — 18 October 2026, v0.1.5

The user reported severe desktop slowdown after applying v0.1.4. Its capture workers immediately mapped full GPU frames into CPU memory, including pointer-only updates, then the renderer uploaded them again. A read-only reference smoke initially consumed approximately one CPU core. This identifies expensive work; it does not prove the cause of every system stall.

Compatible sources now use the presenter's output-adapter D3D11 device and shader-readable GPU textures. This avoids CPU pixel buffers/readback on that path. Duplication polling uses zero timeout and reuses images for pointer-only events. Cross-adapter/incompatible sources use a three-texture staging ring, nonblocking DoNotWait maps, and a shared 256 MiB/s readback budget capped at 30 fps per source. Fallback presentation is capped at 60 fps. These limits reduce work and can reduce visible smoothness; they are not native-monitor performance guarantees. Status and telemetry report which source uses which path.

Deterministic checks: Core **11/11** passed. Windows **17/17** passed with `--without-display`, which explicitly excludes four graphics groups. Warnings-as-errors test build, app build, and self-contained win-x64 build completed with **0 warnings, 0 errors**. Cadence regression first reproduced 48 captures instead of about 60 on simulated 144 Hz presentation, then passed with a persistent deadline. Resource guard, startup cancellation/helper-exit, and telemetry discovery regressions were observed failing before their fixes. Telemetry uses `.json.performance`, so released helpers scanning `*.json` do not treat it as a recovery journal.

The full Windows run, before the final telemetry regression was added, reported **16/20 passed**. All four native groups failed to receive an initial desktop frame or reproduce the marker pixel. They are unresolved verification failures, not successful tests. The strengthened GPU test requires a known pixel from a temporary non-activating marker window; a shader view handle alone cannot satisfy it. Diagnostic screenshots are armed only after capture readiness.

Physical capture also stopped working in the unchanged v0.1.4 reference. DXGI still lists DISPLAY1 at 2560x1080/144 as attached, but monitor discovery no longer reports LG identification or diagonal inches, and WMI/PnP monitor inventory is absent. The console is active/unlocked. Monitor sleep/disconnection is a possibility, not a confirmed diagnosis. No topology change was made for these checks.

A preliminary candidate smoke with a black startup image was discarded. Its apparent CPU improvement is invalid evidence and is not a measured performance result. There is no valid active-split before/after comparison yet.

The bounded `--session-benchmark` performs physical capture preflight, baseline CPU/RAM/GPU sampling, a short saved-profile activation, and automatic restore. Two consecutive resource excesses abort sampling. It independently awaits helper exit and checks journal completion and original topology, including startup timeout paths. NVIDIA telemetry covers all reported adapters; incomplete GPU coverage or missing presentation cadence cannot produce DesktopBudgetPass. The measured presentation cadence is not game FPS.

Executed preflight returned **NotRun**, **TopologyChanged=false**. Three read-only samples reported system CPU 8.1–10.6%, NVIDIA GPU activity 24–41%, available RAM 1217–1230 MiB, and GPU memory 1116–1127 MiB. These are inactive/preflight readings, not SplitDeck overhead measurements. Live desktop images, split responsiveness, game FPS/input latency, and comparison with two physical monitors remain unverified until capture is available.

API references: [DuplicateOutput adapter requirement](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_2/nf-dxgi1_2-idxgioutput1-duplicateoutput), [frame release timing](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_2/nf-dxgi1_2-idxgioutputduplication-releaseframe), and [nonblocking Map behavior](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/ne-d3d11-d3d11_map_flag).

### Follow-up with the monitor awake

The user confirmed the monitor had been asleep and woke it. LG WMI identification returned, the known desktop-marker pixel test passed, and the full Windows test runner passed **21/21**, including all four graphics groups. Combined with Core **11/11**, all **32** component groups pass. This resolves the preceding missing-frame test failures under the awake-monitor condition.

Both packaged physical presentation tests exited 0 and their GPU readback images were inspected: both contained desktop content in both regions. The short v0.1.4 process used 2750 ms CPU over 3529 ms wall time and presented 82 frames. The v0.1.5 process used 750 ms CPU over 3172 ms wall time and presented 45 frames, reporting one GPU capture stream, zero CPU fallback streams, and zero continuous CPU pixel readback bytes. Startup, cadence, and presented-frame counts differ, so this is a directional component comparison, not an equivalent game workload or a measured FPS gain. The old severe-slowdown configuration has not been reapplied for comparison.

### Guarded active-split trials and driver diagnosis

The first elevation request was canceled without a visible prompt. A normal-window launch succeeded. Its baseline memory guard refused activation with 584–643 MiB available. At the user's explicit request, other user apps were closed, retaining this chat, SplitDeck, and Windows services; available memory rose to about 3.3 GiB. No security service or display driver was stopped for memory cleanup.

Two v0.1.5 GPU-only virtual-split trials then activated and automatically restored after excessive CPU usage. The instrumented trial recorded baseline CPU 21.35%, active CPU 43.16%, baseline GPU 2.2%, active GPU 7.0%, and presentation 4.32 fps. Both source streams used GPU capture with zero continuous CPU readback. WUDFHost consumed about 16–18 CPU percentage points. Across 14 rendered frames, capture took 17.1 ms and draw submission 1.6 ms, while Present took 2327.4 ms. Both trials verified helper exit, completed recovery journal, and original topology restoration. The app was therefore not considered fixed despite passing component checks.

### v0.1.6: one monitor per managed driver instance

The measured driver-host load matches reports in [upstream issue #140](https://github.com/VirtualDrivers/Virtual-Display-Driver/issues/140). The workaround under test uses two device instances of the same pinned signed package, each configured to expose one monitor. Shared XML advertises both render modes. Ownership retains the legacy DeviceId and adds one AdditionalDeviceIds entry. Apply requires both devices verified; Setup migrates a clean SplitDeck-owned single instance and refuses unrelated installations. Recovery attempts to disable both even if one disable operation fails. Upgrade rollback removes only the added device; if removal fails, ownership of that device is retained for diagnosis/retry.

Review caught an interrupted-Setup retry that could return with enabled devices. Setup now explicitly disables and verifies native problem code 22 on all owned devices before success, including an already-present pair. Its regression was observed failing then passing. The benchmark now requires at least 80% of requested presentation cadence, so low CPU usage with 4 fps cannot pass.

The first pair-Setup attempt installed the added device successfully according to SetupAPI, but Win32_PnPSignedDriver omitted its DeviceID. Setup rejected that incomplete inventory and successfully removed the new device, retained the original disabled device, and restored its old XML. Health lookup now opens the specific device's bound software key through SetupAPI, verifies its provider/version/INF, then applies the same pinned package hashes, Windows signatures, and native Configuration Manager problem-code checks. A supplied real-device read-only test passes; source review found no outstanding important defects. No signature enforcement was bypassed and the signed package is unchanged. Windows canceled the updated setup's elevation request; it did not register another device or run the paired-driver trial.

Final candidate checks: Windows **25/25** and Core **11/11** passed, including native graphics, direct bound-package lookup on the actual owned device, pair ownership/rollback, disabled-setup retry, and cadence acceptance. Self-contained warnings-as-errors build completed with **0 warnings, 0 errors**. At the pause for Windows approval, only the original managed root device remains and is disabled (code 22), with the physical LG desktop restored. The v0.1.6 editor is opened for the user to invoke Setup through its visible button. Successful pair migration, paired-driver live CPU/GPU/cadence, and game performance remain unverified.


## Cursor/browser and recovery candidate — 18 October 2026, v0.1.7

- Full Windows Release runner: **37/37 groups passed**; Core: **11/11**.
- Self-contained x64 build with warnings as errors: **0 warnings, 0 errors**.
- Native offscreen alpha/monochrome cursor pixel tests and standard native
  cursor shape/hotspot checks passed. Animated cursor graphics use frame zero.
- Physical-only presentation smoke: **61 frames / 2.010856 seconds = 30.335 fps**
  for target30, GPU1/CPU fallback0, continuous desktop CPU readback0. This
  verifies short physical presentation cadence, not virtual/game performance.
- Editor/close smoke exited0; screenshot inspected with restart message and
  Apply disabled. Independent final review found no important remaining defect.
- Chrome baseline synthetic clicks/typing/scrolling/CSS styles passed. The
  v0.1.6 split attempt failed before ACTIVE with ACCESS_LOST and disable3010;
  its initial watcher result was invalidated. New acceptance requires fresh
  heartbeat/frame progress, matching helper/ACTIVE interval and restoration.
- Desktop-only recovery exited0; diagnostics now show only the LG2560x1080/144.
  Its Before snapshot was already physical-only, so new live detach is unproven.
  Device states0/22 and restart marker remain; journal stays incomplete.
- No further Apply trial ran after the restart requirement. Live cursor latency,
  actual transition retry, and overhead under game/Discord load remain pending.

See [the detailed bug report](cursor-browser-report-v0.1.7.txt).


## Post-restart recovery — 18 October 2026, v0.1.8

Windows had restarted and both owned devices were disabled (code22), but
redundant pnputil disable calls returned1167 and kept recovery incomplete.
The fix skips verified disabled devices and accepts1167 only after a fresh
disabled-state check. Unknown/missing/active states and other errors still fail.

- New regression groups: observed failing before implementation, then passing.
- Focused disable checks3/3; Windows without display33/33; Core11/11. Six native
  graphics/display checks were excluded for this focused cleanup patch.
- Self-contained x64 build:0warnings,0errors. Independent focused review approved.
- Live recovery exited0; old journalComplete=true/RestartRequired=false, restart
  marker absent, no incomplete journals remaining, both owned devices code22.
- Diagnostics show driver ready and only LG2560x1080/144, primary at0,0.
- Editor/close smoke exited0; screenshot inspected with Apply enabled.
- No new Apply/game trial; no new claim about cursor latency or game overhead.

See [the recovery report](recovery-report-v0.1.8.txt).


## Enable and remembered-detach faults — 18 October 2026, v0.1.9

PnPUtil enable1167 now has an owned/trusted class-installer fallback, followed
by bounded native state verification. A separate inactive enumeration prepares
detached owned outputs from currently advertised modes before layout activation.

- Four transition and two attachment regression groups; Windows without
  display39/39, Core11/11, self-contained build0warnings/0errors. Six native
  graphics/display checks excluded for this focused lifecycle patch.
- Independent code review clear after upgrade-cleanup correction.
- First live trial reached enabled devices but failed on detached topology;
  subsequent raw inventory confirmed one owned adapter Attached=false.
- First attachment trial activated at143.713fps, then resource guard aborted:
  total CPU22.08% baseline /86.30% active. Restoration/helper exit confirmed.
- Repeat, unchanged build: DesktopBudgetPass, eight active samples,142.203fps,
  readback0, CPU21.67% baseline/16.94% active, GPU3.4%/13.625%. First active CPU
  sample still68.69%; changing background activity prevents isolated cost claims.
- Exact physical restoration, helper exit and journal completion confirmed.
  Read-only diagnostics: driver ready, only LG2560x1080/144 primary0,0.
- Editor/close smoke exited0; screenshot inspected with v0.1.9/Apply enabled.
- No game FPS, long-session, native mouse-latency or real-dual-monitor parity
  claim is supported by these short idle trials.

See [all startup results](startup-report-v0.1.9.txt).
