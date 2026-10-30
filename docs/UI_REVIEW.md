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

Historical branded captures and their earlier visual verdict do not review this
replacement interface. Fresh Windows captures at default and minimum sizes are
needed to check clipping, keyboard focus, scrolling and native control appearance.
Source inspection alone cannot establish those results.

No end-to-end test has been completed in licensed Live 12.4.x. Build success,
synthetic bridge tests, real Windows Save-dialog tests and Demo captures do not
verify Live calibration, musical source selection, render completion, restoration
or cancellation timing. Use a short working Set for the first acceptance test.
