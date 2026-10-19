# Troubleshooting SplitDeck

If the desktop becomes slow or the cursor stops responding normally, restore before trying another layout. Use **Restore desktop** or **Ctrl+Alt+Shift+F12** while the presenter is running. Closing SplitDeck also requests restoration.

## The download has no executable

Use `SplitDeck-0.2.5-windows-x64.zip` from [Releases](https://github.com/amirsamidarwish-collab/SplitDeck/releases). GitHub's source-code ZIP contains source files. Extract the complete portable ZIP before opening `SplitDeck.exe`; moving only the executable leaves out its runtime and driver assets.

## Setup does nothing or Apply is disabled

Use the current portable release and close older editors. Setup may restart the editor to request Windows elevation. Approve UAC if it appears. If a request was canceled, there is no pending approval to wait for; click Setup again to make a new request.

This release requires signing into Windows with an administrator account. Standard-user sessions elevated through another account are unsupported. A canceled prompt leaves setup unfinished.

Apply also stays disabled while driver verification is running, when the selected count has not been prepared, when no physical monitor is available, or when the layout is invalid. Check the message beside the controls. Switching to three screens can require an additional managed device and another Setup run.

## Driver binding, signature or restart error

Keep the full portable folder, including `assets/driver`. Setup verifies the pinned package and Windows signature trust. Windows can reject a driver because of device state or system policy; keep the exact error message when reporting it.

If the message requires a restart, save your work and restart Windows yourself. Run Setup again afterward. SplitDeck keeps unfinished recovery information and blocks a new split while the driver restart is pending.

If another MttVDD or Virtual Display Driver installation is present, SplitDeck refuses to change its configuration. Identify the existing installation before removing or changing any driver.

## Black regions, blinking or no desktop

Restore first. Check that the physical monitor is awake, connected and selected, and that Windows is unlocked. The current capture path requires SDR output; turn off HDR for the display being tested. Then check the reported driver and capture errors before trying again.

The physical monitor remains in Windows Display Settings because it presents the virtual images. Do not disable it while SplitDeck is active. Games, protected content and fullscreen modes can require separate compatibility work; a working editor preview does not prove they can be captured.

## An app is visible only in the taskbar

The v0.2.5 candidate adds restoration for windows opened during the split. Opening the editor normally also attempts to bring fully offscreen windows back onto an attached physical desktop, without Apply or an administrator prompt, when no session owns the machine lock. This is source-reviewed behavior and has not yet been confirmed in a live trial. See the [candidate notes](restoration-v0.2.5.md).

SplitDeck moves eligible windows that were on the selected physical monitor when Apply began. A minimized window stays minimized. New windows can remember a previous monitor, and some apps manage their own positions.

Select the app and try Windows+Shift+Left/Right Arrow to move it to another output. A window wider than a narrow region may cross its boundary because the app enforces a minimum size. Increasing its virtual render resolution changes scaling and cost; it cannot add physical pixels.

## GPU usage is high

Restore and compare with the normal desktop before continuing. Rendering apps, the virtual driver, desktop capture and Windows composition all contribute to the load.

The v0.2.4 capture fix improved animation delivery in a 144/30 Hz comparison. Total GPU utilization stayed around 18.3%; this does not show a GPU reduction. A 1 ms capture-wait experiment used more GPU and power and was left out. The [performance report](performance-v0.2.4.md) explains the browser visibility limitation, measurements and earlier results. These tests do not establish game FPS or performance equivalent to physical monitors.

Match render resolution to visible pixels to avoid extra detail being downsampled. Lowering a virtual refresh target is an available user choice, but it also reduces that source's update target. The resource bars are estimates of layout demand, not a measurement of actual GPU usage or RAM capacity.

## Restoration needs attention

An incomplete journal means SplitDeck could not confirm every recovery step. If another app or a user changed display settings during the session, recovery can refuse to replay saved modes over those changes.

In Windows Display Settings, identify the physical monitor and make it the main display if needed. If you use Device Manager for manual recovery, change only SplitDeck's managed Virtual Display Driver devices. Their IDs are recorded in `%ProgramData%/SplitDeck/Driver/owner.json`; do not disable an unrelated display adapter. Keep the recovery files so the failure can be diagnosed.

If the error says the driver needs a restart, repeated Apply attempts will remain blocked. Restore what you can, save your work and restart Windows yourself.

## Logs and issue reports

These paths belong to the account running the editor and the machine-wide driver setup:

| Path | Contents |
| --- | --- |
| `%LocalAppData%/SplitDeck/operations.log` | Editor operation errors |
| `%LocalAppData%/SplitDeck/last-error.txt` | Last command or startup error, when recorded |
| `%LocalAppData%/SplitDeck/profile.json` | Legacy saved layout; ignored by v0.2.5 and cleared after a confirmed normal close |
| `%ProgramData%/SplitDeck/Driver/owner.json` | Managed device IDs |
| `%ProgramData%/SplitDeck/Driver/sessions` | Recovery journals and diagnostic sidecars |

From the portable folder, this command writes a read-only monitor and driver report:

```powershell
.\SplitDeck.exe --diagnose .\diagnostics.json
```

Include the SplitDeck version, Windows build, GPU, physical resolution/refresh, layout and exact error. Describe whether the problem happens before Apply, during the split or during Restore. Review logs before sharing them; they can contain account paths and device identifiers. A screenshot of the editor and the diagnostic report usually gives more context than a description of a blank region alone.
