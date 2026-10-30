# First export in Live 12.4.6

Use a short test Arrangement before exporting a real delivery. This is an
integration preview; end-to-end Live validation has not yet been performed.

## Install and connect

1. Extract the portable ZIP or run the installer. Start `AbleBounce.exe`.
2. Click **Install Live bridge…** and select your actual Ableton User Library.
   The common default is `Documents\Ableton\User Library`; it can be elsewhere.
3. Restart Live. Under **Settings → Link, Tempo & MIDI → Control Surface**, select
   **AbleBounce** in an unused slot, with Input/Output **None**. Other controller
   slots are not modified. The bridge occupies one Control Surface slot.
4. Open your saved Set, stop playback and return all tracks to Arrangement. Clear
   Solo buttons. Resolve missing plugins, missing samples and authorisation dialogs.
5. Click **Connect to Live**. Review roles for every audio-producing leaf track.
   Use **Inherit** inside explicitly classified groups. Do not classify a mixed
   group as Lead or Backing; classify its individual children instead.

## Calibrate once

In Live, press Ctrl+Shift+R. Set **All Individual Tracks**, so the Include Return
and Main Effects switch is enabled. Set all of these before capturing points:

| Setting | Calibration value |
|---|---|
| Encode PCM | On |
| File Type | WAV |
| Bit Depth | 24 |
| Dither Options | No Dither |
| Normalize | Off |
| Convert to Mono | Off |
| Render as Loop | Off |
| Encode MP3 | Off |
| Include Return and Main Effects | Off |

Also set the intended sample rate, Render Start and Render Length. Include enough
length for reverb/delay tails; Render as Loop is not a tail-extension feature.

Return to AbleBounce and choose **Calibrate export…**, acknowledge the preparation
prompt and move the floating wizard away from Live's controls. It will ask for
eleven points. For each one, bring Live to the foreground, hover over the centre
of the requested value/switch and press **F8**. Do not click the Export button or
open dropdown menus. The wizard records a small visual reference for switch
verification. If you capture the wrong point, use **Undo last point**.

Keep the window size, theme, display scale and export dialog position unchanged.
Recalibrate if those change. When finished, **close Live's Export dialog** before
starting the queue.

## Run a batch

Select any variants, 16/24/32-bit float and an export destination. All other
settings remain as described in README. Click **Export selected**. Live Save As
creates a unique sibling working Set and the app verifies that Live has switched
to it before applying changes. No original file is overwritten.

Leave the desktop unlocked and do not interact with Live. During rendering, use
the app's **Cancel & restore** if needed. Cancellation may take a moment because
the render must stop before it is safe to change device/track states.

The output folder opens on completion. Mixes, dry vocal and multitrack folders
contain the finished files. `manifest.json` records the batch. Staging files are
kept if a validation failed. The restored working Set remains open; review it or
reopen your original Set as appropriate.

## If something stops

- **No connection:** enable the bridge, restart Live and keep only one instance
  open. The installer does not automatically change Live's MIDI preferences.
- **Wrong calibration/geometry:** restore Live's calibrated window or recalibrate.
- **Save dialog not recognised:** close it manually and retry. The app does not
  enter paths into unidentified windows.
- **Vocal routing requires review:** inspect the named output bus. This preview
  refuses to guess whether a shared parallel bus belongs to lead or backing.
- **Restore pending:** stop/cancel Live's render and close its modal dialogs, then
  click **Restore working Set**. Do not save pending temporary changes.
- **Live crashed:** reopen the saved working `.als` or original `.als`; the on-disk
  working copy was saved before temporary changes. Already exported files remain.
