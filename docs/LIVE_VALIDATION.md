# Required Live 12.4.x acceptance run

**Not yet performed.** Do not replace this statement with a pass merely because
the Windows application compiles or the fake-Live regression suite passes.

Use a short saved project with deterministic audio and no random modulation:
instrumental tone/drums; two lead tracks in a Lead group; two backing tracks in a
Backing group; one nested group; duplicate names; a disabled master effect; one
enabled master processor; a shared send reverb; lead fader automation and an
automated device activator. Keep an untouched source copy and manual exports.

1. Install bridge. Check scan order including folded children, group parents,
   Return/Main identification and device classification. Verify Python API
   DeviceType enum, `Track.insert_device`, Utility Gain and volume formatter on
   the real host. Check source and working Set paths after Save As.
2. Calibrate on English Live 12.4.x using six F8 captures: All Individual Tracks,
   bit depth 24, Export, bit depth 16, bit depth 32 and Main. Verify source and
   bit-depth Home/Down orders and visual readback. Confirm exact-patch mismatch
   requires recalibration. Verify native Save dialog detection and filename UIA
   readback, including spaces and Cyrillic paths (the 0.1.2 fix). Test 100%,
   125%, 150% DPI separately and a second monitor. Moving/resizing/theme changes
   must fail closed or require recalibration.
3. Export all nine variants with a short fixed range. Compare each with a manual
   reference export. Instrumental variants must contain no lead/backing or vocal
   wet return. Backing variants must retain backing while removing lead.
4. Set WAV/stereo, PCM On and No Dither manually in Live; set Normalize, Mono,
   Loop, MP3 and Include Return and Main Effects Off. Set sample rate, Render
   Start and Render Length including tails. AbleBounce does not change or verify
   these switches: confirm the UI and documentation make that responsibility clear.
   At 16/24/32-bit verify file format, common start, sample count and actual sample
   rate against manual references. Verify 32-bit is IEEE float. Check unchanged
   manual settings after every pass and test reverb tails. WAV header validation
   alone cannot prove that dither, normalization or the intended range is correct.
5. For −6 dB, compare the lead root before downstream group/master processing;
   expected amplitude ratio is `10^(-6/20) = 0.501187…`. The processed final mix
   need not differ by that exact ratio because other sources and nonlinear
   processors remain. Fader automation must be unchanged; pre-fader sends must
   follow the documented trim semantics. Group+child assignment must trim once.
6. Check multitrack numbering including groups and duplicate names. Verify own
   track effects, separate group effects, separate Returns and no master effects
   on leaf outputs. Do not test by summing every group with all children.
7. Dry lead must omit track/group/master effects and returns, preserving edits,
   Warp, Clip Gain, faders and pan. Test multiple independent leads and a lead
   subgroup inside a mixed parent; temporary direct routing must restore exactly.
8. After every job compare own Device On states, automation states (including
   previously overridden parameters), volume, mute, sends, routing, names and
   device count with the initial state. Originally disabled devices stay off.
9. Cancel during preparation, Save, render, multitrack render and verification.
   No restoration may occur while offline processing is still active. Test
   closing the app, dropping its bridge connection, changing the Set structure,
   stealing focus and locking the desktop. No input may go to another app.
10. Force a bridge write/restoration failure and test retry. Kill the desktop
    process mid-batch: reconnect and restore. Crash Live: reopen the saved working
    baseline; original `.als` hash must remain unchanged. Existing output files
    must never be overwritten or removed.
11. Verify preflight on frozen vocal, missing role, conflicting group role,
    custom bus, locked macro-controlled parameter, Session override, existing
    Solo and external hardware. The UI must give an actionable reason.

Record the Windows build, Live full version, display configuration, plugin list,
export profile and comparison results before declaring the integration verified.

