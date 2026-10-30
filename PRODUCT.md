# AbleBounce

Windows desktop companion for Ableton Live 12.4.x, English UI in both applications.
The user selects any of nine delivery variants: original mix; instrumental;
instrumental without master effects; full mix without master effects; backing
vocals without lead, without master; backing vocals without lead, with master;
full mix with lead reduced by 6 dB; post-fader multitrack plus separate groups and
returns, excluding main processing; dry lead vocal sum.

Track roles may be assigned to multiple tracks or groups. Name-based suggestions
are editable; uncertain audio tracks require a decision before vocal-dependent
jobs. Group and child selections must never be attenuated twice. Original mute,
solo, automation overrides and device activation states are authoritative.

Native Live renders all audio. Python Remote Script communicates on loopback;
it executes Live API operations only on Live's control-surface callback thread.
No cloud audio processing. No physical MIDI port or Max for Live dependency.
Windows export automation requires an unlocked desktop and a calibrated English
Export dialog. It must stop rather than guess when its verification fails.

Always create a sibling working .als via Live's Save As before mutation. Leave
the original untouched. Restore the working Set after every job, including
cancellation; never restore while Live is still rendering. Unsupported routing,
frozen dry vocals and controlled/unwritable parameters fail preflight.

This repository can be built on Windows CI. Actual Live integration requires a
licensed Windows Live installation; CI does not establish Live compatibility.
