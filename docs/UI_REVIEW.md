# UI review record — 0.1.3

The current interface is a plain Windows prototype with standard uncolored WPF
controls. Source inspection confirms empty application resources, native buttons,
role and variant DataGrids, bit-depth and folder fields, and explicit export,
cancellation, restoration and activity commands. Install, Connect and Calibrate
are directly visible at the top. The main window defaults to 1100 × 780 DIPs and
has a 900 × 650 minimum.

The UI states that fixed export switches are user-managed. Calibration captures
six references for source, bit depth and Export only. Exact-patch, window-size and
DPI guards remain. The 0.1.2 Save-dialog fix and tests against real Windows Save
dialogs remain; these do not prove Live-specific dialog behavior.

The final visual review returned **SHIP** with no material findings after reviewing
actual Windows captures at **1280 × 860** (`review-final/ablebounce.png`) and
**1060 × 740** (`review-final/ablebounce-minimum.png`). At those two capture sizes,
Install, Connect and Calibrate, core controls and manual-settings text were readable
and unclipped. This review did not exercise keyboard or scrolling interaction,
and neither capture is at the configured 900 × 650 minimum. The final code build,
[Windows run 41077976114](https://github.com/RainlineMusic/ablebounce/actions/runs/41077976114),
passed for [e85752f](https://github.com/RainlineMusic/ablebounce/commit/e85752ffa1ec07e5ebd26bff8f803f0f48e97350),
including 43 bridge tests and desktop native Save-path checks.

No end-to-end test has been completed in licensed Live 12.4.x. Build success,
synthetic bridge tests, real Windows Save-dialog tests and Demo captures do not
verify Live calibration, musical source selection, render completion, restoration
or cancellation timing. Use a short working Set for the first acceptance test.
