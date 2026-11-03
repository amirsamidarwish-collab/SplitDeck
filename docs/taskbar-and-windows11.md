# Windows taskbar position and Windows 11 checks

In SplitDeck, use **Windows taskbar → Open Windows taskbar settings**. This opens the real Windows settings page for the Start button, app icons and system tray. It does not create a replacement taskbar.

- **Windows 10:** choose **Taskbar location on screen → Left, Top, Right or Bottom**.
- **Windows 11 with the positioning feature:** expand **Taskbar behaviors**, then choose **Taskbar position**. Availability depends on your Windows build and feature rollout. Taskbar icon alignment is a separate setting.
- **Windows 11 without that option:** this version of Windows does not expose native edge positioning. SplitDeck does not force it through registry edits or an Explorer replacement.

These are Windows preferences. Windows saves your choice, and closing SplitDeck does not undo it. No split or driver setup is needed to open the settings page. Windows controls which per-monitor taskbar options are available; SplitDeck does not add independent edge positions per virtual screen.

## Compatibility scope

SplitDeck's portable package is for **Windows x64** and includes its .NET 10 runtime. Microsoft lists Windows 11 x64 as supported by .NET 10. The bundled driver and live split still need testing on the particular PC, GPU and Windows build.

The build workflow has a **Windows 11 x64 app startup (ARM emulation)** job. It downloads the exact x64 portable ZIP from the build job, checks its SHA-256, runs the packaged editor in an isolated preview, verifies the taskbar controls, renders an image, and records the OS/build/architecture. It does not open Settings, install a driver or apply a split. The report and image are available as the `SplitDeck-Windows11-startup-check` Actions artifact.

This is a Windows 11 app-startup check under x64 emulation, not a Windows 11 x64 driver, GPU, reboot or live multi-monitor certification. The bundled AMD64 driver is not an ARM64 driver. The existing `windows-latest` build job runs on Windows Server and is not itself a Windows 11 test.

The earlier mixed-resolution zoom issue remains unresolved. This taskbar change does not establish a zoom, reboot-restoration or GPU-performance fix.

To repeat the preview check on a Windows 11 x64 PC after extracting the portable package, run this from the source checkout:

```powershell
./tools/Test-WindowsCompatibility.ps1 -ExecutablePath C:/SplitDeck/SplitDeck.exe -ReportDirectory C:/SplitDeck-check-new -RequireWindows11
```

Use a fresh report directory each time. The check closes its own preview and leaves display settings alone.

## References

- [Launch Windows Settings: taskbar URI](https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-settings)
- [Microsoft's Windows 11 taskbar-position announcement](https://blogs.windows.com/windows-insider/2026/05/15/improving-windows-quality-making-taskbar-and-start-more-personal/)
- [Customize the Windows taskbar](https://support.microsoft.com/en-us/windows/experience/personalization/customize-the-taskbar-in-windows)
- [.NET on Windows: supported operating systems](https://learn.microsoft.com/en-us/dotnet/core/install/windows)
- [GitHub-hosted runner operating systems and architectures](https://docs.github.com/en/actions/reference/runners/github-hosted-runners)
