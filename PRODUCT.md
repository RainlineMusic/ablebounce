# AbleBounce 0.1.3

Plain Windows prototype for Ableton Live 12.4.x, with an English UI in both
applications. Standard WPF controls provide Install, Connect and Calibrate,
track-role assignment, nine selectable export variants, bit depth, output folder,
Export selected, Cancel & restore, Restore working Set and Activity.

The nine variants are full mix; instrumental; instrumental without Main devices;
full mix without Main devices; backing mix without Main devices; backing mix;
full mix with lead reduced by 6 dB; mixed multitrack with groups and returns;
and dry lead sum. Multiple tracks/groups may have Lead or Backing roles. Review editable
name-based role suggestions before exporting. Group and child selections
must not be attenuated twice. Original mute and device states remain authoritative.

Native Live renders all audio. The loopback Python Remote Script runs Live API
operations on Live’s control-surface callback thread. No cloud audio processing,
physical MIDI port, separate Python installation or Max for Live is required.

Export automation controls only render source (Main/All Individual Tracks),
bit depth (16/24/32-bit float) and Export. Six F8 captures record their visual
states. Calibration is tied to the exact Live patch, window size and display DPI;
keep the theme and dialog position unchanged. The desktop must stay unlocked.

The user sets WAV/stereo, PCM On, No Dither, Normalize/Mono/Loop/MP3/Include Return
and Main Effects Off, plus sample rate and render start/length including tails in
Live. These switches are not changed or verified by AbleBounce. WAV structure and
format validation cannot establish the musical correctness of an export.

Create a unique sibling working .als with Live Save As before bridge mutation.
Restore the working Set after each job and cancellation; restoration waits for
rendering and modal dialogs to stop. Leave the original untouched. Unsupported
routing, frozen dry vocals and unwritable parameters fail preflight.

The 0.1.2 Windows Save-dialog fix and real Windows dialog tests remain in 0.1.3.
The app has not been tested end to end in a licensed Live 12.4.x installation.
Build, bridge-unit-test and demo results do not establish Live compatibility.
