# SplitDeck implementation plan

> For agentic workers: use superpowers:subagent-driven-development or superpowers:executing-plans. Track verified progress in .superpowers/sdd/progress.md.

Goal: a buildable Windows desktop app implementing the approved layout editor, real virtual display activation, presentation, input, and restoration, with clear compatibility diagnostics.

Architecture: a pure geometry/profile library; a Windows platform library for display discovery, driver communication, native GPU presentation, cursor confinement, topology and recovery; a WPF editor; a self-contained Windows release.

Tech stack: .NET 10 LTS, C#, WPF, DXGI/D3D11 through pinned Vortice packages, VirtualDrivers VDD using owned XML configuration and Windows device disable/enable operations.

## Global constraints

- Windows 10 build 19045 or later, x64 initial release.
- Preserve aspect ratios and distinguish physical pixels from render resolution.
- One physical output refresh cadence; per-screen values are targets, never promised game FPS.
- Never disable signature enforcement, Secure Boot, or enable test signing.
- Never overwrite a pre-existing VDD installation or its unrelated virtual displays without a supported preservation path; reject activation when ownership cannot be established.
- Apply succeeds only after both virtual outputs and both capture streams are initialized.
- Keep/Revert after 15 seconds; Ctrl+Alt+Shift+F12 restores the previous topology.
- Preview, discovery, and builds do not require changing the current desktop or installing a driver.
- Do not claim live driver/game verification when only tests, builds, and local presentation diagnostics were run.

## Task 1: geometry and profiles

Files: src/SplitDeck.Core, tests/SplitDeck.Core.Tests.
Use the exact public contracts in docs/task-core.txt. Write failing geometry, refresh, invalid-input, and profile tests first; run them, implement, then rerun. Geometry must use integer dimensions, stay within the panel, preserve locked ratios, support portrait/fill and independent virtual resolution, and clamp refresh targets to panel limits. JSON persistence must be atomic and recover from invalid files without losing the old file. Deliver test evidence in .superpowers/sdd/core-report.txt.

## Task 2: Windows backend and technical diagnostics

Files: src/SplitDeck.Windows/{Native,Displays,Driver,Presentation,Session}, tests/SplitDeck.Windows.Tests, src/SplitDeck.App/Program.cs.
Discover monitor path identities and physical sizes; verify the upstream driver package and XML schema from source; capture current modes; prepare an independent restoration watchdog; configure only owned VDD outputs while disabled; set supported modes; enumerate and verify. DXGI captures feed a D3D11 compositor on a nonactivating native host; latest-frame queues favor the first screen without accumulating latency. Cursor stays on virtual desktop coordinates and is drawn on the physical host. All failure paths restore clipping and modes. Test preparation failure, partial activation, cancellation, driver timeouts, journal integrity, and restoration order. A diagnostics command must inspect state without mutations and a separate presentation smoke test must exercise local capture/render without installing drivers.

## Task 3: approved desktop editor

Files: src/SplitDeck.App/MainWindow.xaml, MainWindow.xaml.cs, UI helpers.
Match the approved SplitDeck design: dark graphite, soft green, proportional monitor preview, screen inspector, presets, physical space slider, landscape/portrait, custom aspect, render detail, per-screen target, priority preset, Apply/Keep/Revert, restore. Call the interfaces in docs/task-ui.txt; never substitute a preview result for actual backend success. Populate monitors from live discovery; default to safe native resolution and supported rates. Persist profiles and all editing changes without display mutations. Show actionable driver and compatibility errors; keep restore available throughout a session.

## Task 4: review and release

Files: README.md, LICENSE, THIRD-PARTY-NOTICES.md, .github/workflows/build.yml, build.ps1, docs/verification.md.
Run core and backend tests, build, self-contained publish, diagnostics, presentation smoke test, and visual UI inspection. Have an independent code reviewer examine topology/recovery/resource handling and the user workflow. Fix critical/important findings, rerun relevant checks, then package source and a portable Windows build in outputs. Include supported/untested features and exact test commands. Do not publish a GitHub repository without a destination request.
