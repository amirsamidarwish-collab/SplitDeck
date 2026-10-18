# Mixed-refresh checks for v0.2.4

The capture scheduler now avoids applying two competing rate limits to the fast screen. This improved animation delivery in the development test. It did **not** establish lower GPU usage or performance equivalent to two physical monitors.

The test PC used Windows 10 build 19045, an RTX 2060 and an LG 2560 × 1080 panel at 144 Hz. Each comparison used the same two-screen layout: 1920 × 1080 at 144 Hz and a 603 × 1072 portrait screen at 30 Hz. The landscape screen was primary. The saved editor profile was left unchanged. The physical output remained at 144 Hz and normal synchronized presentation was used.

Chrome ran static content, a moving square, a new tab and typing in an isolated profile. Discord was running. The user confirmed that both apps were visible and usable with a responsive pointer in the earlier fast-primary trial and the final experimental trial. Each completed comparison restored the normal desktop, exited its helper and recorded no rendering or recovery error. The resource budget was still exceeded.

| Measurement | Reference with both rate limits | Presenter-paced fast capture | Experimental 1 ms acquisition wait |
| --- | ---: | ---: | ---: |
| Whole-GPU utilization before the split | 6.20% | 6.80% | 4.20% |
| Whole-GPU utilization during the split | 18.31% | 18.22% | 21.48% |
| Total CPU utilization during the split | 37.78% | 39.66% | 25.01% |
| Animation-phase capture polls/second | 111.67 | 113.87 | 101.60 |
| Animation-phase fast-screen images copied/second | 28.98 | 48.16 | 56.98 |
| Animation-phase presentations/second | 36.61 | 50.10 | 61.78 |
| Average fast-image age when acquired, whole trial | 7.66 ms | 6.57 ms | 2.57 ms |
| Mean board power during Chrome phases | 18.60 W | 21.75 W | 26.62 W |
| Chrome animation callbacks in about 10 seconds | 1,442 | 1,453 | 1,428 |

GPU and CPU values cover the benchmark's active sampling interval. Board power covers the four Chrome phases, sampled once a second. These are individual trials with varying background activity and GPU clock rates. The small difference between 18.31% and 18.22% is not evidence of a GPU reduction. CPU and board-power readings also prevent treating the scheduling change as a proven overall efficiency gain.

The 1 ms wait improved acquisition timing but used more GPU and board power in this comparison. It is **not included** in v0.2.4. The shipped path retains immediate, non-blocking acquisition and releases each held frame just before requesting the next one, following [Microsoft's duplication guidance](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_2/nf-dxgi1_2-idxgioutputduplication-releaseframe).

## What changed

The presenter paces its loop. Previously the fast capture also had an independent clock at the same target rate. Small shifts from presentation and cursor work could cause that second clock to reject an otherwise valid capture opportunity. A regression test reproduced 72 skipped opportunities in a simulated 144 Hz second. Fast sources now use the presenter's pacing; slower sources keep their independent limit. Standalone capture keeps its own limit as well.

Positive presenter waits now round up to whole milliseconds. The previous `WaitOne(TimeSpan)` call truncated fractional milliseconds and could wake before its deadline. This fix removes that source of premature wakeups, but the live tests do not show that it alone resolves the mixed-refresh slowdown.

Diagnostics now distinguish cadence skips, acquisition timeouts, pointer-only frames and acquired-image age. Intervals between acquired images, including their source timestamps, do not measure every frame produced upstream: missed and coalesced frames affect those intervals. Optional telemetry reads tolerate an in-progress replacement or partial JSON instead of interrupting a display trial.

## Chrome visibility limits the comparison

In two ordinary-browser trials, Chrome reported the page as hidden during the split and delivered zero animation callbacks, despite the user reporting the apps visible. Those runs cannot establish animated frame delivery. [Chromium documents that native-window occlusion can stop rendering](https://chromium.googlesource.com/chromium/src/+/48e25a9e7ffe51a28ad47ec65d6cfe1cf5e92fa6/docs/windows_native_window_occlusion_tracking.md).

The three tabled comparisons therefore used `--disable-backgrounding-occluded-windows` **only in the isolated test Chrome process**. The test page then reported visible and animation ran. This supports a browser-occlusion interaction, but it does not identify which window or display state caused it. SplitDeck does not change Chrome or Discord settings, install a browser policy, or apply that flag to users' apps. Ordinary Chromium/Electron visibility remains an open compatibility issue.

Callback counts, copied images and presentation calls are different measurements. None establishes displayed game FPS, click-to-photon latency or 144 Hz motion on the physical panel. There was no physical dual-monitor baseline, long gaming run or new-PC installation test. For the user's current setup, disabling the priority preset was reported to make motion smooth; that also changes refresh targets and layout shares, so it does not isolate a single cause. See the [earlier report](performance-v0.2.2.md) for the previous layout and presentation experiments.
