# Direct Apply implementation plan

**Goal:** Apply keeps the split active without a confirmation popup or countdown, as requested by the user.

**Architecture:** Remove confirmation UI/state/IPC. The presenter reserves the emergency restore shortcut independently. The helper continues until Restore, editor exit, emergency restore, capture/presentation failure, or parent loss. Existing journal, watchdog, and topology recovery stay in place.

**Tech stack:** C#/.NET 10, WPF, WinForms native message pump, Direct3D 11.

The user explicitly stated the missing confirmation is unnecessary; this is authorization to simplify the existing approved app flow. No additional design choice is required. Increasing the timeout or relocating the popup would retain the unwanted interaction, so this plan removes it.

- [x] Add a native regression that checks no confirmation window appears, the presentation stays active beyond 15 seconds, and the emergency hotkey is reserved. Observe failure with the existing default presenter.
- [x] Remove confirmation state and UI; decouple emergency-hotkey registration from the removed confirmation flag. Remove Keep IPC/UI remnants and update active-session status text.
- [x] Run Windows regressions and warnings-as-errors app build. Review the focused diff.
- [x] Prepare v0.1.4 beside the running build, verify diagnostics/editor/native rendering, update README/verification, and provide the executable. Reuse unchanged runtime dependencies where possible to conserve C: space.
