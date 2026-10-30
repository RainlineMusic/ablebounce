# AbleBounce interface

AbleBounce 0.1.4 is a plain Windows prototype. Use standard, uncolored WPF buttons,
DataGrids, ComboBoxes, CheckBoxes, text fields and GroupBoxes. `App.xaml` has empty
application resources: no custom control templates, palette, brand mark, shadows
or motion. Windows/WPF defaults provide control appearance and interaction.

The main window is 1100 × 780 DIPs, with a 900 × 650 minimum. Its background uses
the Windows ControlBrush. A 10-DIP outer margin contains:

- Install, Connect and Calibrate buttons with connection text at the top.
- Current Set name, then Track roles and Export variants DataGrids side by side.
- Bit depth and output folder with Browse below the grids.
- A visible list of export settings that the user must configure in Live.
- Status text, Export selected, Cancel & restore, Restore Set and Activity.

The grids use a 2:3 width ratio with an 8-DIP gap. Track names are indented by
hierarchy and show full names in tooltips. Roles use editable selectors; delivery
rows show a checkbox, variant name and textual state. All/None buttons change the
variant selection. The grids handle their own scrolling. Preserve native keyboard
focus, selection and disabled rendering; communicate status with words.

Calibration uses a plain WPF window and six F8 captures for three controls:
render source (All Individual Tracks/Main), bit depth (24/16/32) and Export.
Fixed export switches are user-managed and must be described that way in the UI.
Demo sessions must remain explicitly labelled. Demo rendering is not evidence of
Live integration. Historical styled screenshots do not document this interface;
new Windows captures are required to review its final native appearance.
