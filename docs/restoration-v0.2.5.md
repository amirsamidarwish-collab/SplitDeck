# Restoration and scaling candidate: v0.2.5

This candidate addresses windows that reopen outside the normal desktop and very different apparent app sizes between split regions. It has been reviewed in source and compiled with warnings treated as errors. **No tests, display trials, application launches, administrator prompts or reboot simulations were run for this change**, at the user's request. The fixes still need runtime confirmation.

## What the existing evidence shows

The saved layout used a 3840 × 2160 render desktop for the landscape region and a 603 × 1072 native render desktop for the portrait region. Mapping those desktops onto one physical panel without coordinating Windows scaling can make the same app look small on one side and large on the other.

Recent recovery records confirmed the positions of all windows captured before Apply. The code, however, did not capture windows opened later in the session. Those windows could retain coordinates belonging to a virtual output. The display-mode staging code also used persistent Windows registry settings, so the saved boot configuration needed separate treatment while the temporary split ran. These findings identify gaps in the code; they do not reproduce or prove every part of the reported reboot behavior.

## Changes

- Capture eligible windows opened during the split before disabling its outputs. Save their return positions in the recovery journal and account for render density when sizing them on the physical desktop.
- On a later normal editor startup, recover fully offscreen windows onto an attached physical work area. Leave visible windows alone, preserve minimized/maximized state and skip this operation if an existing session lock cannot be acquired without elevation.
- Start each editor session with a fresh layout and native render detail. Edits stay in memory, including through the explicit administrator handoff; old saved profiles are not loaded automatically.
- Derive each virtual screen's Windows scale from the physical screen's effective DPI and its render-to-visible pixel density. Choose the closest supported Windows scaling step. Keep the physical monitor's scale unchanged and journal each virtual screen's original scale before applying changes.
- Restore virtual scaling while sources are still available, then disable the devices and restore the physical layout. Record each confirmed DPI restoration separately. An unavailable source cannot silently clear a pending restoration.
- Stage the original physical boot defaults with `CDS_NORESET` after the live split is configured. Do not commit that staged baseline over the running split. This is intended to prevent the split from becoming the normal boot layout; abrupt-power-loss behavior remains unverified.
- Wait for the session helper to exit before normal editor cleanup. Remove known local profile/log temporary files after confirmed recovery. The watchdog removes completed ordinary-session journals and their known sidecars after the helper exits. Explicit diagnostic sessions retain their reports.

Failed or unverified restoration keeps its recovery journal. Driver ownership metadata and the signed installed driver remain available, with the managed devices disabled after successful restoration. This is temporary-session cleanup, **not driver uninstallation**. SplitDeck does not delete browser or Discord caches, profiles, tabs, or other app data.

## Limits

Per-source DPI changes use Windows Settings' private `DisplayConfigGetDeviceInfo`/`DisplayConfigSetDeviceInfo` packet types. The code checks packet sizes, ownership, scale ranges and readback, and refuses activation if the required interface fails. Microsoft does not document this private packet contract. Compatibility with the current PC and other Windows versions has not been established for this candidate. The physical DPI reading uses the documented `GetDpiForWindow` API on an invisible per-monitor-aware helper window.

Windows exposes discrete scale steps, so sizing can be approximate. Some older apps do not respond correctly to DPI changes. Render densities outside 1/16–16 are rejected before scaling is applied. Hung or restricted app windows may remain unconfirmed; their recovery evidence is retained rather than reported as fully restored. No claim is made that force-closing Windows or removing power can always finish cleanup.

The earlier [v0.2.4 performance measurements](performance-v0.2.4.md) were not repeated and are not validation of this candidate. GPU overhead and ordinary Chromium/Electron visibility remain unresolved.

References: [Windows mode staging and registry flags](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-changedisplaysettingsexw), [GetDpiForWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getdpiforwindow), [original per-source DPI research](https://github.com/lihas/windows-DPI-scaling-sample).
