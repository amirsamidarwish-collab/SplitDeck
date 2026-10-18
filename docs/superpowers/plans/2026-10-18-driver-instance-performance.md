# Driver instance performance repair

**Goal:** Keep both virtual screens without the severe Windows-wide slowdown measured when the pinned driver exposes two monitors on one device.

**Evidence:** Guarded trials restored successfully but recorded 4.3–4.6 presentation fps, 16–18 CPU percentage points in WUDFHost, and more than 99% of renderer stage time waiting in Present. Both captures were GPU-only. Upstream issue https://github.com/VirtualDrivers/Virtual-Display-Driver/issues/140 reports the same multi-monitor-per-instance behavior and a one-monitor-per-instance workaround on the pinned 11.30.4.434 driver. Treat the report as a hypothesis to validate locally, not proof of success.

**Architecture:** Keep the signed driver and GPU renderer. During explicit Setup, migrate the existing owned root device to an owned pair, one monitor each sharing a one-monitor XML configuration and advertised modes. Preserve the legacy owner DeviceId while recording one additional owned ID. Refuse unrelated devices. Enable and recover both owned devices; older builds will refuse the extra device rather than alter it. Roll back a failed second-device upgrade without removing the existing device or losing ownership of a device whose removal fails.

- [x] Add failing tests: one-monitor XML, legacy/pair owner parsing and foreign-device rejection, upgrade rollback and retained ownership on removal failure.
- [x] Implement paired ownership, explicit Setup migration, independent enable/disable with all-device cleanup. Require completed pair before Apply. Keep trust checks, machine lease, recovery journal/watchdog, and saved user profile.
- [x] Add benchmark cadence acceptance so a 4 fps renderer cannot pass on resource usage alone. Keep fresh process CPU consumers and capture/draw/present stage timing to distinguish driver load from app work.
- [x] Review code and rollback before any new device registration; run component tests and warnings-as-errors build. Final Windows25/25 andCore11/11 pass; warnaserror0. Read-only review approved migration and direct binding lookup.
- [ ] Run explicit Setup under normal Windows elevation, then a short guarded trial. Check actual CPU/GPU/RAM/cadence and confirmed restoration. Publish only claims supported by the results; retain experimental label and gaming validation limits.

First Setup trial rejected missing WMI device inventory and rolled back the added device. ReadBoundDriver now reads the bound software key with SetupAPI instead, preserving all package/trust/problem-code checks; real primary-device read-only test passed. The updated elevation request was canceled by Windows before a new process started. Only original owned device remains, disabled. Open v0.1.6 editor for visible Setup; paired-driver performance trial awaits that Windows approval.
