# Implementation contract

## Components

- WPF/.NET 10 desktop: track roles, nine jobs, settings, local role persistence.
- Independent Python 3 Remote Script: Live API access on `update_display` only.
- Loopback TCP `127.0.0.1:16846`: one authenticated newline JSON request per
  connection. Token is in the current user's LocalAppData/AbleBounce/bridge-token.
  No eval, arbitrary API traversal or external bind address.
- Calibrated Windows GUI driver: Win32 SendInput with foreground process checks,
  common file dialog detection/filename readback through UIA, source/bit-depth
  visual checks. Fixed export switches remain user-managed.
- Queue: preflight → begin → prepare → native export → WAV
  verification → delivery mapping → restore → next job → final restore.

There is no claimed headless Live audio renderer and no claimed public export API.
Live's own engine hosts all instruments, third-party plugins and audio processing.

## Bridge protocol

Request: `{"id":"…","token":"…","method":"…","args":{…}}` followed by newline.
Reply: `{"id":"…","ok":true,"result":{…}}` or `ok:false,error:"…"`.

Methods are an allowlist: ping, status, scan, preflight, begin, prepare, restore. Prepare
and restore require the active transaction ID. A changed track-object sequence
invalidates preparation. Socket accepts/reads/writes are nonblocking and bounded;
no worker thread touches Live objects. Responses may pause during offline render.
Every dispatched state mutation requires Live's `open_dialog_count == 0` and an
empty `current_dialog_message`. File completion alone never authorises restore.

Track keys `t:N`, return keys `r:N`, `master` are scoped to a scan/transaction, not
stable identities across reordered Sets. Saved roles additionally check names.
GUI start checks path/name/parent sequence against the latest scan; bridge checks
Live object references while the batch is in progress.

## Restoration

Each write records the exact prior attribute or parameter value/automation state.
Restore runs in reverse order. Active automation is re-enabled only on the changed
parameter; pre-existing overridden automation remains overridden. Restoring a
baseline does not globally re-enable unrelated automation. Device own activators
are used because inherited `is_active` cannot reconstruct nested rack states.
Temporarily inserted Utilities are registered immediately and removed by object
identity rather than a fixed device index. Failed restorations remain retryable.

There is no automatic bridge watchdog that changes Live while the application is
offline rendering. Desktop cancellation first cancels modal/render UI, waits for
the Live thread and then restores. If that cannot be established, the transaction
is retained and the app explicitly requests manual cancellation before Restore.

Export uses the current open Set, including unsaved Sets. No project Save/Save As
command is sent and no `.als` copy is created. Local `last-session.json` records
the source and output paths. In-memory restoration remains transaction based;
there is no AbleBounce disk backup for a Live process crash.

## Export profile and validation scope

Three user points and six visual patches are captured from the actual Live
English export dialog: source All Individual Tracks, bit depth 24, Export,
bit depth 16, bit depth 32 and source Main. They cover only three controls.
Matching exact Live patch, export window size and DPI are required. Keep theme and
dialog position unchanged. The driver checks the Export patch, enters source and
bit-depth chooser values with Home/Down/Enter, then checks their captured patches.
It does not change or verify WAV/stereo, PCM, dither, Normalize, Mono, Loop, MP3 or
Include Return and Main Effects switches. Users also set rate and range in Live.

The 0.1.2 Save-dialog fix remains: identify the common Windows file dialog and its
filename control, enter the path, read it back, and submit without relying on
current focus. Existing destinations are rejected. Real Windows Save-dialog tests
cover working-copy paths, spaces and Cyrillic names; Live’s own Save/export dialog
behavior still needs end-to-end validation. No blind fixed export sleep establishes
completion.

File validation checks RIFF chunks, complete sizes, positive data length, PCM vs
float, stereo, chosen bit depth, sample rate and sample counts. These checks verify
file structure, not whether the correct musical sources are audible. Native GUI
behaviour, mute/send behaviour, PDC and plugin rendering require the Live acceptance
tests in a licensed Live 12.4.x installation, which have not yet been completed.
File-level success is never described as proof of sonic equivalence.

## Known choices

One final Utility per selected lead root is used even without fader automation,
providing consistent trim semantics. The root's own sends are reduced too;
descendant sends bypassing a selected group keep their level (group-fader-like
placement without changing drive into the group processors). Dry output
is a post-edit/post-fader sum through the unchanged Main fader, with all Main
devices bypassed; it is not a raw-source sample export. Instrument devices remain
enabled, while audio-effect rack parents are bypassed as complete devices.

All Individual Tracks produces separate track/group/return outputs. Temporary
unique naming markers ensure file mapping works with duplicate names. Mapping is
validated in full before moving multitrack files. The native Main reference is
discarded only after all expected audio tracks are accounted for. Processing of
shared nonlinear buses is not decomposed into independent reconstructable tracks.

