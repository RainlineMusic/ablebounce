# First export in Live 12.4.x

Use a short test Arrangement before exporting a real delivery. This is an
integration preview; end-to-end Live validation has not yet been performed.

## Install and connect

1. Extract the portable ZIP or run the installer. Start `AbleBounce.exe`.
2. Click **Install** and select your actual Ableton User Library.
   The common default is `Documents\Ableton\User Library`; it can be elsewhere.
3. Restart Live. Under **Settings → Link, Tempo & MIDI → Control Surface**, select
   **AbleBounce** in an unused slot, with Input/Output **None**. Other controller
   slots are not modified. The bridge occupies one Control Surface slot.
4. Open your Set, stop playback and return all tracks to Arrangement. Clear
   Solo buttons. Resolve missing plugins, missing samples and authorisation dialogs.
5. Click **Connect**. Review roles for every audio-producing leaf track.
   Use **Inherit** inside explicitly classified groups. Do not classify a mixed
   group as Lead or Backing; classify its individual children instead.

## Calibrate once

In Live, press Ctrl+Shift+R. Set **All Individual Tracks**, so the Include Return
and Main Effects switch is enabled. Set Bit Depth to **24** for the first
calibration captures. Set the following manually; AbleBounce does not change or
verify these switches:

| Manual setting | Required value |
|---|---|
| Encode PCM | On |
| File Type | WAV |
| Dither Options | No Dither |
| Normalize | Off |
| Convert to Mono | Off |
| Render as Loop | Off |
| Encode MP3 | Off |
| Include Return and Main Effects | Off |

Also set the intended sample rate, Render Start and Render Length. Include enough
length for reverb/delay tails; Render as Loop is not a tail-extension feature.

Return to AbleBounce and choose **Calibrate**, acknowledge the preparation
prompt and move the floating wizard away from Live's controls. It will ask for
three points: Rendered Track, Bit Depth and Export. Hover over the displayed value
text (not the arrow) and press F8 once for each. Do not click during the short
final automatic capture: AbleBounce selects and records the remaining values,
then leaves Main / 24-bit selected. Close Live's Export dialog before exporting.

Live's Export Audio/Video window is located by its title and process every time.
You can move it or resize the main Live window. Keep the export window's size,
theme, display scale and Live patch unchanged; recalibrate if those change.
Old six-point profiles must be replaced once.

## Run a batch

Select any of the nine variants, 16/24/32-bit float and an export destination.
Review the manual settings in Live before each batch; calibration does not
verify them. Click **Export selected**. Export runs directly from the current
open Set. No Save As dialog or project copy is involved; unsaved Sets work too.

Leave the desktop unlocked and do not interact with Live. During rendering, use
the app's **Cancel & restore** if needed. Cancellation may take a moment because
the render must stop before it is safe to change device/track states.

Use **Activity** to inspect the session log. The output folder opens on completion.
Mixes, dry vocal and multitrack folders contain the finished files. `manifest.json` records the batch. Staging files are
kept if a validation failed. The current Set remains open with its temporary export changes restored.

## If something stops

- **No connection:** enable the bridge, restart Live and keep only one instance
  open. The installer does not automatically change Live's MIDI preferences.
- **Wrong calibration/geometry:** restore Live's calibrated window or recalibrate.
- **Save dialog not recognised:** close it manually and retry. The app does not
  enter paths into unidentified windows.
- **Vocal routing requires review:** inspect the named output bus. This preview
  refuses to guess whether a shared parallel bus belongs to lead or backing.
- **Restore pending:** stop/cancel Live's render and close its modal dialogs, then
  click **Restore Set**. Do not save pending temporary changes.
- **Live crashed:** use Live recovery or your own saved project. AbleBounce does
  not create a project backup. Already exported files remain.
