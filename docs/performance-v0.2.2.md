# Performance checks for v0.2.2

SplitDeck still adds substantial GPU work. These tests do not establish performance equivalent to two physical monitors or predict the FPS a game will lose.

The development PC runs Windows 10 build 19045 with an RTX 2060 and an LG 2560 × 1080 panel at 144 Hz. The trial layout used a 120 Hz landscape screen and a 30 Hz portrait screen. Both comparison runs kept the same modes and pixel dimensions. Chrome ran in an isolated profile through static content, animation, a new tab and typing. Discord was open. The user confirmed that both apps and the pointer worked in the diagnostic immediate trial without a visible problem.

| Measurement | Synchronized reference | Diagnostic immediate presentation |
| --- | ---: | ---: |
| Whole-GPU utilization before the split | 16.0% | 10.4% |
| Whole-GPU utilization during the split | 29.62% | 26.81% |
| Total CPU utilization during the split | 21.76% | 25.77% |
| Completed presentation polls/second | 117.09 | 117.50 |
| Content-driven presentations/second | 35.17 | 39.63 |
| Chrome animation callbacks in about 10 seconds | 1,210 | 1,219 |
| Desktop restored and helper exited | Yes | Yes |

Immediate mode used a capability-checked DXGI tearing flag and sync interval zero. It recorded lower GPU utilization, but its pre-split baseline was also lower. CPU use increased, and board power was not consistently lower. The animation phase, for example, averaged 28.56 W in the reference and 31.18 W in immediate mode. This comparison does not show a reliable reduction in total resource use. The normal editor keeps synchronized presentation. Immediate mode remains an explicit diagnostic command, `--desktop-menu-trial-immediate <report-path>`; it may tear on other workloads or systems.

The first reference attempt hit the unchanged CPU guard twice during the display transition and restored automatically. Explorer and NVIDIA background processes were the largest CPU consumers. The retry completed all Chrome phases. Both valid runs still exceeded the performance budget. Animation callbacks and capture polls are not measurements of displayed FPS or physical mouse latency.

Earlier tests on a different 60/60 Hz layout averaged 25.5% total GPU usage against a 2.6% baseline. Partial copying reduced nominal submitted copy volume by 41.2% relative to full copies of the same acquired frames. The tests did not establish a comparable drop in whole-GPU utilization. The different layout and baseline make those older results unsuitable as a direct speed comparison with the current runs.

No long gaming test, physical dual-monitor baseline or clean-PC installation test has been completed. Local checks cover bundled signatures, runtime files and installation-policy rollback behavior. They cannot establish that a new PC will accept and run the driver.
