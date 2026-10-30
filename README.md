# AbleBounce

A Windows session-delivery companion for **Ableton Live 12.4.x**, with an English
interface. Native Live renders the audio. AbleBounce prepares track/device states,
drives Live's English Export Audio/Video dialog and organises the results.

**Status: 0.1.3 integration preview.** Bridge state logic has automated regression
coverage. Windows builds and native WPF demo captures run in GitHub Actions. This project
has not yet been end-to-end tested in a licensed Live 12.4.x installation. A
successful CI build does not establish that the export dialog calibration works
on your machine. Test a short working copy before a full delivery.

## Download

Open [Actions → Build Windows app](https://github.com/RainlineMusic/ablebounce/actions/workflows/windows.yml),
choose a successful run and download one of its artifacts:

- **AbleBounce-Setup-win-x64** — EXE installer, current Windows user only.
- **AbleBounce-portable-win-x64** — self-contained application and instructions.
- **AbleBounce-design-preview** — native WPF capture using labelled demo data; it is not a Live integration test.

The application includes .NET. No separate Python, Max for Live, virtual MIDI
driver or physical controller is required. Live's built-in Python hosts the
Remote Script. Install the included bridge using the button inside the app.

## Delivery variants

| # | Variant | Vocals | Master devices |
|---|---|---|---|
| 1 | Full mix | Original | Original on/off states |
| 2 | Instrumental | No lead or backing | Original states |
| 3 | Instrumental, no master | No lead or backing | Bypassed |
| 4 | Full mix, no master | Original | Bypassed |
| 5 | Backing mix, no master | Backing only | Bypassed |
| 6 | Backing mix | Backing only | Original states |
| 7 | Full mix, lead −6 dB | Lead trimmed relative to its original level | Original states |
| 8 | Mixed multitrack | Post-fader individual tracks, groups and returns | Excluded |
| 9 | Dry lead sum | Selected lead tracks/groups without devices/sends | Bypassed |

Each variant starts from the same baseline. Already disabled devices stay
disabled. Original mute states stay muted. Existing solos must be cleared before
starting, because they otherwise make vocal variants ambiguous.

Connection accepts numeric patch releases in the **12.4.x** branch, including **12.4.5**. Each exact patch version requires its own export-dialog calibration. This version check is not a claim that every patch has been tested in Live.

## First run

Read [QUICK_START.md](docs/QUICK_START.md) for the complete procedure.

1. Open AbleBounce and choose **Install**. Select the actual Ableton
   **User Library** folder.
2. Restart Live. In **Settings → Link, Tempo & MIDI**, select **AbleBounce** in a
   free Control Surface slot. Set Input and Output to **None**.
3. Open an Arrangement Set, stop playback and click **Connect**.
4. Review role suggestions. Any number of tracks or groups can be Lead/Backing.
   Children may inherit their parent role. Mark other audio tracks Instrument.
5. Prepare Live's export dialog as described in the calibration prompt. Set
   the **sample rate, start and length, including effect tails**, in Live.
6. Choose **Calibrate**. Capture the requested control positions with
   **F8** six times: source All Individual Tracks, bit depth 24, Export, bit
   depth 16, bit depth 32, source Main. Then close the Live export dialog.
   Recalibrate for changes to window geometry, theme, display scale or exact Live patch.
7. Choose variants, bit depth (16/24/32-bit float) and output folder, then export.

The UI must remain visible and the desktop unlocked. Do not interact with Live
while AbleBounce controls it. A source/bit-depth calibration mismatch, unsupported
routing or readback failure stops the batch. Other export switches are user-managed as described below.

## What the audio options mean

- **No master** bypasses devices on Main, preserving the Main fader. It does not
  set Main to 0 dB or remove group processing.
- **Lead −6 dB** inserts a temporary, final **Utility** at each selected lead root,
  preserving the existing fader automation. This reduces the root's own sends;
  descendant sends that bypass a selected group retain their original level,
  just as they would when lowering that group's fader. Group drive is preserved.
  A selected group and its children are never trimmed twice. Gain is read back
  in decibels, not guessed from a normalized parameter value.
- **Multitrack** uses All Individual Tracks with Include Return and Main Effects
  off. Individual tracks contain their own effects and faders, **not parent
  group processing**. Group outputs contain their own bus processing. Files are
  numbered in original project order and groups/returns have separate folders.
  Do not sum all group files with all of their child files: they overlap. Shared
  nonlinear bus processing cannot be split into independently processed tracks
  that are guaranteed to reconstruct the original mix.
- **Dry lead** bypasses effects on selected roots and descendants; selected roots
  route directly to Main to bypass parent groups. Other tracks and Returns are
  muted and vocal sends set to zero. Edits, Warp, Clip Gain, faders and pan remain;
  Main devices are bypassed, Main fader remains. Printed effects cannot be undone.
- **Set manually in Live:** WAV, stereo, Encode PCM On and No Dither; Normalize,
  Convert to Mono, Render as Loop, Encode MP3 and Include Return and Main Effects
  Off. AbleBounce does not change or verify these switches. Set sample rate,
  Render Start and Render Length (including tails) in Live. The app automates only
  render source, bit depth and Export. WAV headers are checked for stereo, chosen
  bit depth and PCM/float encoding; sample rate and frame counts must agree across
  outputs. This does not verify the intended range or the user-managed switches.
  RIFF WAV files over 4 GB are rejected.

## Project protection and recovery

AbleBounce exports from the current open Set. It never invokes Live Save or
Save As and does not create a working `.als` copy. Unsaved Sets are supported.
The bridge captures parameter values, device activator and automation override
states before changes, restores them after each pass and at batch end, and
retains failed restorations for retry with **Restore Set**.

Do not manually save while temporary export changes are active. No disk recovery
copy is created: if Live itself crashes, recovery depends on Live's recovery and
your own saved project. Unsaved edits are not backed up by AbleBounce.

A manifest is written with delivered files, track roles and audio format. Partial
exports stay in their unique output batch folder. Existing deliveries are never
overwritten. Source files and user media are never deleted.

## Current limits

- Exact GUI behaviour needs verification on Live 12.4.x; no prevalidated export
  profile is included. The calibrated chooser order assumes Live's documented
  Main / All Individual Tracks / Selected Tracks and 16 / 24 / 32-bit ordering.
- Name recognition is a suggestion, not an audio classifier. Unknown material
  needs explicit role assignment for vocal-dependent variants. An Instrument
  parent is an explicit declaration about its descendants.
- Custom vocal routing outside the assigned parent/Main is rejected, as are
  non-vocal tracks feeding a vocal bus or receiving a vocal audio tap. Dedicated
  parallel vocal buses and mixed shared buses require project-side routing review.
  Returns fed by correctly assigned vocals are handled by suppressing their sends.
- Frozen dry vocals must be unfrozen first. A frozen lead selected for trim must
  also be unfrozen. External hardware devices are rejected in this first build.
- Session clip overrides and existing solos must be cleared. Parameters locked
  by macros/remote control are rejected when an override cannot be applied.
- Keep one Live instance open, at the same Windows privilege level as AbleBounce.
- No built-in plugin authorisation handling or missing-media repair. Resolve
  those problems in Live first.

## Build and test

Requires the .NET 10 SDK and Windows x64 for the desktop application. Python tests
also run on Linux/macOS, with no third-party packages.

```powershell
python -m unittest discover -s tests -p "test_*.py" -v
dotnet run --project tests/AbleBounce.Tests/AbleBounce.Tests.csproj -c Release
dotnet publish src/AbleBounce/AbleBounce.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/portable
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" installer/AbleBounce.iss
```

`AbleBounce.exe --demo` runs a clearly labelled design preview without Live.
`--demo --screenshot preview.png` captures the actual WPF window and exits.

Version 0.1.4 retains the 0.1.2 Windows Save-dialog fix: paths are entered through
the identified filename control and read back before submission. Windows tests
exercise real Save dialogs, including spaces and Cyrillic paths. These tests do
not exercise Live’s own dialogs.

See [ARCHITECTURE.md](docs/ARCHITECTURE.md) for the integration contract,
[LIVE_VALIDATION.md](docs/LIVE_VALIDATION.md) for the required acceptance run and
[UI_REVIEW.md](docs/UI_REVIEW.md) for the current review limits.

## References

- [Ableton: Exporting Audio and Video](https://www.ableton.com/en/manual/managing-files-and-sets/)
- [Ableton: Installing third-party remote scripts](https://help.ableton.com/hc/en-us/articles/209072009-Installing-third-party-remote-scripts)
- [Cycling '74: Track](https://docs.cycling74.com/apiref/lom/track/)
- [Cycling '74: DeviceParameter](https://docs.cycling74.com/apiref/lom/deviceparameter/)
- [AbletonOSC](https://github.com/ideoforms/AbletonOSC), evidence for the Remote Script transport pattern; its source is not bundled in this implementation.

AbleBounce is an independent tool, not an Ableton product.

