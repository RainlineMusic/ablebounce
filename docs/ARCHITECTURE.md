# Implementation contract

## Components

- WPF/.NET 10 desktop: track roles, nine jobs, settings, local role persistence.
- Independent Python 3 Remote Script: Live API access on `update_display` only.
- Loopback TCP `127.0.0.1:16846`: one authenticated newline JSON request per
  connection. Token is in the current user's LocalAppData/AbleBounce/bridge-token.
  No eval, arbitrary API traversal or external bind address.
- Calibrated Windows GUI driver: Win32 SendInput with foreground process checks,
  common file dialog detection/filename readback through UIA, visual toggle checks.
- Queue: preflight → Save As working copy → begin → prepare → native export → WAV
  verification → delivery mapping → restore → next job → final restore.

There is no claimed headless Live audio renderer and no claimed public export API.
Live's own engine hosts all instruments, third-party plugins and audio processing.

## Bridge protocol

Request: `{"id":"…","token":"…","method":"…","args":{…}}` followed by newline.
Reply: `{"id":"…","ok":true,"result":{…}}` or `ok:false,error:"…"`.

Methods are an allowlist: ping, scan, preflight, begin, prepare, restore. Prepare
and restore require the active transaction ID. A changed track-object sequence
invalidates preparation. Socket accepts/reads/writes are nonblocking and bounded;
no worker thread touches Live objects. Responses may pause during offline render.

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

Original `.als` files are untouched. Save As uses a unique sibling path so relative
media references stay valid; that saved working baseline is the crash recovery
point. Current unsaved source edits are included in it. Export mutations are not
automatically saved. Local `last-session.json` records source/working/output paths.

## Export profile and validation scope

Eleven relative coordinates and visual patches are captured from the actual Live
English export dialog. Matching Live version, outer window size and DPI are
required. The driver checks the expected Export button and desired switch patches,
enters chooser values with Home/Down/Enter and verifies resulting file encoding.
The filename is entered only into a detected common Windows file dialog and read
back before Enter. No blind fixed export sleeps are used for completion.

File validation checks RIFF chunks, complete sizes, positive data length, PCM vs
float, stereo, chosen bit depth, sample rate and sample counts. These checks verify
file structure, not whether the correct musical sources are audible. Native GUI
behaviour, mute/send behaviour, PDC and plugin rendering require the Live acceptance
tests. File-level success is never described as proof of sonic equivalence.

## Known choices

One final Utility per selected lead root is used even without fader automation,
providing consistent trim semantics. Pre-fader sends are reduced too. Dry output
is a post-edit/post-fader sum through the unchanged Main fader, with all Main
devices bypassed; it is not a raw-source sample export. Instrument devices remain
enabled, while audio-effect rack parents are bypassed as complete devices.

All Individual Tracks produces separate track/group/return outputs. Temporary
unique naming markers ensure file mapping works with duplicate names. Mapping is
validated in full before moving multitrack files. The native Main reference is
discarded only after all expected audio tracks are accounted for. Processing of
shared nonlinear buses is not decomposed into independent reconstructable tracks.
