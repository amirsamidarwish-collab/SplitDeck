# Installing and using SplitDeck

## Before you start

The portable package targets Windows x64 with a Direct3D 11-capable GPU. Development testing used Windows 10 Pro build 19045. Setup and Apply require signing into Windows with an administrator account and approving UAC. This experimental release does not support a standard-user session elevated through a different account.

Keep the monitor awake and Windows unlocked while setting up or applying a layout. Leave free disk space for the extracted package, driver files and recovery journals. The app has been tested on the development PC; installation on a fresh machine remains unverified.

## Extract the portable package

1. Open [Releases](https://github.com/amirsamidarwish-collab/SplitDeck/releases) and download `SplitDeck-0.2.2-windows-x64.zip`. If the repository is private, GitHub access is required.
2. Use Windows' **Extract All** to unpack the ZIP. `SplitDeck.exe` is at the ZIP's root.
3. Open `SplitDeck.exe` from the extracted folder. Keep the DLLs, `assets` and other accompanying files there.

The package includes the .NET runtime and driver assets. You do not need a separate runtime or driver download. GitHub's **Source code** downloads contain source files, so use the portable asset when you want an executable.

`SplitDeck.exe` is not code signed. The bundled driver DLL and catalog have valid signatures; those signatures do not cover the app executable. Windows may show an unfamiliar-app warning.

The release also provides `SplitDeck-0.2.2-windows-x64.zip.sha256`. To read the ZIP's hash in PowerShell:

```powershell
Get-FileHash .\SplitDeck-0.2.2-windows-x64.zip -Algorithm SHA256
```

Compare it with the value in the `.sha256` file. If they differ, download the ZIP again before running it.

## Choose the layout

Select your physical monitor, then choose **2 screens** or **3 screens**. The portrait/center/portrait preset creates three regions and makes the center primary. Select a screen's tab or preview region to edit it.

Visible dimensions describe the pixels assigned on the physical panel. Render dimensions describe the virtual monitor's desktop resolution. Keep rendering matched to visible pixels for the default native-size configuration. Higher render resolutions add work and are scaled down into the same physical space.

Use `0` for an automatic dimension. With a ratio lock, one explicit visible dimension can determine the other only when it produces whole pixels. Conflicting width, height and ratio values show an error and disable Apply. Exact visible widths reserve their pixels first; automatic regions divide the remaining width according to their shares.

The primary-screen button and ordering controls are independent. Moving the primary region to another position keeps that region primary. The two-screen priority preset sets initial width and refresh values; it does not reserve GPU scheduling priority for a game.

Resource bars update while you edit valid settings. They estimate pixel work and screen buffers, not hardware usage or available capacity. Monitor size in inches affects the description only.

## Set up the driver

Click **Setup driver** when it is shown. Approve the Windows administrator prompt for the copy of SplitDeck you opened. SplitDeck may reopen as administrator with your edited layout preserved.

Setup verifies the bundled signed package, prepares one managed driver device for each selected screen, and leaves the devices disabled. Changing from two to three screens can require Setup again. After three devices have been prepared, a two-screen session leaves the unused third device disabled.

Windows decides whether it accepts the driver. Setup reports binding, signature, policy and restart errors. If Windows requires a restart, save your work, restart Windows yourself and run Setup again. A successful Setup does not apply a layout.

An unrelated MttVDD or Virtual Display Driver installation can block setup. SplitDeck refuses to overwrite another installation's configuration. Resolve that conflict before retrying; repeated Setup attempts do not make the other installation safe to replace.

## Apply and place your apps

Click **Apply layout** after the preview is valid and the driver is ready. The helper enables the configured virtual outputs and starts presentation. The physical output remains active, so Windows displays it alongside the virtual screens in Display Settings.

SplitDeck moves eligible existing windows from the selected physical monitor into the virtual work areas. Owned modal dialogs move with their captured app. A minimized app stays minimized; its saved restore position moves into the virtual desktop. Apps with minimum widths larger than a region can extend across a boundary.

For a game with a monitor selector, choose a virtual output. If a window opens on the wrong display, try Windows+Shift+Left/Right Arrow or its own monitor setting. Exclusive fullscreen and anti-cheat compatibility have not been established.

There is no confirmation popup or automatic Keep countdown. The layout stays active while the editor and presenter are healthy.

## Restore the desktop

Click **Restore desktop**, press **Ctrl+Alt+Shift+F12**, or close the editor. SplitDeck restores the saved physical topology, disables its managed virtual devices and attempts to restore captured app positions. It retains the app's current minimized or maximized state.

The emergency shortcut depends on the presenter running. A separate watchdog handles helper failure. If restoration reports an error, keep the journal and follow [troubleshooting](troubleshooting.md) before applying another layout.

## Update an existing installation

Restore the desktop and close older SplitDeck editors first. Extract a new portable release into a separate folder, then launch that copy and run Setup if requested. Use v0.2.0 or newer after preparing three managed devices; older builds only understand two.

The profile is stored in `%LocalAppData%/SplitDeck/profile.json` for the account running the editor. Deleting a portable folder does not uninstall the Windows driver or remove that profile.
