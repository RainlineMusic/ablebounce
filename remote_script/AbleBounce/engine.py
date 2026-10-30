"""Live-facing transaction engine. No network/thread code and no third-party deps.

Called only from the ControlSurface update_display callback. Device activation is
read from its own Device On parameter, not inherited Device.is_active.
"""
import math
import re
import uuid


class BounceError(Exception):
    pass


def parameter_state(parameter):
    return (float(parameter.value), int(parameter.automation_state))


def device_on(device):
    for parameter in device.parameters:
        if parameter.name in ("Device On", "Device Activator"):
            return parameter
    # The first parameter of Live devices is normally the host activator; do
    # not guess if a device exposes a different first parameter.
    raise BounceError("No host Device On parameter: " + device.name)


def audio_effect(device):
    try:
        import Live
        return device.type == Live.Device.DeviceType.audio_effect
    except ImportError:  # test host
        return device.type == "audio_effect"


def flatten_devices(track):
    result = []
    def visit(devices):
        for d in devices:
            result.append(d)
            for c in getattr(d, "chains", ()):
                visit(c.devices)
            for c in getattr(d, "return_chains", ()):
                visit(c.devices)
    visit(track.devices)
    return result


def role_guess(name):
    tokens = re.sub(r"[_\-\d]+", " ", name.lower()).split()
    backing = {"back", "backs", "backing", "bgv", "bgvs", "adlib", "adlibs",
               "harmony", "harmonies", "бэк", "бэки", "беки", "бэквокал"}
    lead = {"lead", "main", "leadvox", "leadvoс", "мейн", "лид"}
    vocal = {"vocal", "vocals", "vox", "voice", "вокал", "голос"}
    instrument = {"kick", "snare", "hat", "hats", "drums", "bass", "piano",
                  "guitar", "synth", "keys", "percussion", "strings", "барабаны", "бас"}
    if set(tokens) & backing:
        return "Backing"
    if set(tokens) & lead:
        return "Lead"
    if set(tokens) & vocal:
        return "Unassigned"
    if set(tokens) & instrument:
        return "Instrument"
    return "Unassigned"


class Engine:
    def __init__(self, song, application):
        self.song = song
        self.application = application
        self.transaction = None
        self.changes = []
        self.utilities = []
        self.roles = {}
        self.fingerprint = None

    def tracks(self):
        return list(self.song.tracks)

    def all_tracks(self):
        return self.tracks() + list(self.song.return_tracks) + [self.song.master_track]

    def key(self, track):
        for i, t in enumerate(self.tracks()):
            if t == track:
                return "t:%d" % i
        for i, t in enumerate(self.song.return_tracks):
            if t == track:
                return "r:%d" % i
        if track == self.song.master_track:
            return "master"
        raise BounceError("Track no longer belongs to this Set")

    def resolve(self, key):
        try:
            kind, index = key.split(":")
            index = int(index)
            if index < 0:
                raise ValueError()
            if kind == "t":
                return self.tracks()[index]
            if kind == "r":
                return list(self.song.return_tracks)[index]
        except (ValueError, IndexError):
            pass
        raise BounceError("Invalid track reference: " + str(key))

    def structure(self):
        # Hold Live object references, not names, so external reordering during
        # a batch fails closed instead of applying roles to a different track.
        return tuple(self.all_tracks())

    def check_structure(self):
        if self.fingerprint is not None and self.structure() != self.fingerprint:
            raise BounceError("Set structure changed. Restore the working Set before continuing.")

    def parent(self, track):
        return getattr(track, "group_track", None)

    def ancestors(self, track):
        result = []
        p = self.parent(track)
        while p and p not in result:
            result.append(p)
            p = self.parent(p)
        return result

    def descendants(self, track):
        return [t for t in self.tracks() if track in self.ancestors(t)]

    def selected_roots(self, role):
        selected = [t for t in self.tracks() if self.roles.get(self.key(t)) == role]
        return [t for t in selected if not any(p in selected for p in self.ancestors(t))]

    def effective_role(self, track):
        own = self.roles.get(self.key(track), "Unassigned")
        if own not in ("Unassigned", "Inherit"):
            return own
        for p in self.ancestors(track):
            r = self.roles.get(self.key(p), "Unassigned")
            if r not in ("Unassigned", "Inherit"):
                return r
        return "Unassigned"

    def scan(self):
        output = []
        for t in self.all_tracks():
            k = self.key(t)
            parent = self.parent(t) if k.startswith("t:") else None
            group = bool(getattr(t, "is_foldable", False))
            output.append({
                "key": k, "name": t.name, "parent": self.key(parent) if parent else None,
                "group": group, "return": k.startswith("r:"), "master": k == "master",
                "audio": True if k == "master" else bool(getattr(t, "has_audio_output", True)),
                "frozen": bool(getattr(t, "is_frozen", False)) if k.startswith("t:") else False,
                "mute": bool(t.mute) if k != "master" else False,
                "solo": bool(t.solo) if k != "master" else False, "color": int(t.color),
                "suggested_role": "Inherit" if parent and role_guess(t.name) == "Unassigned" else role_guess(t.name),
                "volume": str(t.mixer_device.volume),
                "volume_automation": int(t.mixer_device.volume.automation_state),
                "output": self.routing_name(t.output_routing_type) if k != "master" else "",
                "devices": [d.name for d in t.devices],
            })
        app = self.application
        return {"protocol": 1, "version": "%s.%s.%s" % (
            app.get_major_version(), app.get_minor_version(), app.get_bugfix_version()),
            "path": self.song.file_path, "name": self.song.name,
            "tracks": output, "loop_start": self.song.loop_start,
            "loop_length": self.song.loop_length,
            "transaction": self.transaction, "pending_changes": len(self.changes) + len(self.utilities)}

    @staticmethod
    def routing_name(routing):
        if routing is None:
            return ""
        if isinstance(routing, dict):
            return routing.get("display_name", "")
        return getattr(routing, "display_name", str(routing))

    def main_routing(self, track):
        for route in track.available_output_routing_types:
            if self.routing_name(route) in ("Main", "Master"):
                return route
        raise BounceError("Direct Main routing unavailable for " + track.name)

    def validate_roles(self):
        allowed = {"Lead", "Backing", "Instrument", "Unassigned", "Inherit"}
        for key, role in self.roles.items():
            self.resolve(key)
            if role not in allowed:
                raise BounceError("Unknown role: " + str(role))
        for t in self.tracks():
            own = self.roles.get(self.key(t), "Unassigned")
            # A Lead/Backing group cannot contain an explicitly different role.
            for p in self.ancestors(t):
                pr = self.roles.get(self.key(p), "Unassigned")
                if pr in ("Lead", "Backing", "Instrument") and own not in (pr, "Inherit", "Unassigned"):
                    raise BounceError("Conflicting roles in group %s. Assign its children instead." % p.name)

    def preflight(self, jobs, roles):
        self.require_idle()
        self.roles = dict(roles)
        self.validate_roles()
        if not self.song.file_path:
            raise BounceError("Save your Live Set before exporting.")
        if self.song.is_playing or getattr(self.song, "record_mode", False):
            raise BounceError("Stop playback and recording before exporting.")
        if bool(getattr(self.song, "back_to_arranger", False)):
            raise BounceError("Return all tracks to Arrangement before exporting.")
        if any(bool(getattr(t, "back_to_arranger", False)) for t in self.tracks()):
            raise BounceError("Some tracks are playing Session clips. Use Back to Arrangement first.")
        if any(bool(t.solo) for t in self.tracks() + list(self.song.return_tracks)):
            raise BounceError("Clear existing Solo states before exporting; muted tracks are preserved.")
        if not jobs or any(j not in range(1, 10) for j in jobs):
            raise BounceError("Choose at least one valid delivery variant.")
        vocal_jobs = any(j in (2, 3, 5, 6, 7, 9) for j in jobs)
        if vocal_jobs:
            unknown = [t.name for t in self.tracks() if not t.is_foldable
                       and t.has_audio_output and self.effective_role(t) == "Unassigned"]
            if unknown:
                raise BounceError("Assign roles to: " + ", ".join(unknown[:12]))
            if not self.selected_roots("Lead") and any(j in (5, 6, 7, 9) for j in jobs):
                raise BounceError("Select at least one Lead track or group.")
            if not self.selected_roots("Lead") and not self.selected_roots("Backing"):
                raise BounceError("No vocal tracks have been assigned.")
        if 9 in jobs:
            dry_tracks = set()
            for t in self.selected_roots("Lead"):
                dry_tracks.update([t] + self.descendants(t))
                self.main_routing(t)  # establish that dry roots can bypass parent effects
            frozen = [t.name for t in dry_tracks if getattr(t, "is_frozen", False)]
            if frozen:
                raise BounceError("Unfreeze these dry-vocal tracks in Live first: " + ", ".join(frozen))
        if 7 in jobs:
            frozen = [t.name for t in self.selected_roots("Lead") if getattr(t, "is_frozen", False)]
            if frozen:
                raise BounceError("Unfreeze these lead trim roots in Live first: " + ", ".join(frozen))
        # Shared buses that contain both vocal and instrumental material require
        # explicit project-side separation; a folder relationship is not proof.
        warnings = []
        for t in self.tracks():
            role = self.effective_role(t)
            if vocal_jobs and role in ("Lead", "Backing"):
                route = self.routing_name(t.output_routing_type)
                parent = self.parent(t)
                expected = ["Main", "Master"] + ([parent.name, "Group"] if parent else [])
                if route not in expected:
                    raise BounceError("Vocal custom routing requires review: %s → %s. Use a dedicated parent group or route this track to Main." % (t.name, route))
        if vocal_jobs:
            vocal_bus_names = {t.name for t in self.tracks()
                               if self.effective_role(t) in ("Lead", "Backing")}
            for t in self.tracks() + list(self.song.return_tracks):
                role = self.effective_role(t) if t in self.tracks() else "Instrument"
                route = self.routing_name(t.output_routing_type)
                if route in vocal_bus_names and role not in ("Lead", "Backing"):
                    raise BounceError("Non-vocal audio feeds a vocal bus: %s → %s. Separate that routing before exporting vocal variants." % (t.name, route))
                # Pre-/post-FX audio taps can bypass Track Activator. Reject a
                # non-vocal receiver of a vocal source instead of leaking voice.
                incoming = self.routing_name(getattr(t, "input_routing_type", None)) if t in self.tracks() and not t.is_foldable else ""
                if incoming in vocal_bus_names and role not in ("Lead", "Backing"):
                    raise BounceError("A non-vocal track receives a vocal audio tap: %s ← %s. Review its role and routing." % (t.name, incoming))
        # Automated send/device on parameters are permitted only if writable.
        # write() captures and restores their exact automation override state.
        for t in self.all_tracks():
            for d in flatten_devices(t):
                if "External" in getattr(d, "class_name", ""):
                    raise BounceError("External hardware rendering needs manual supervision; remove external devices from this batch.")
        return {"ok": True, "warnings": warnings, "tracks": len(self.tracks())}

    def begin(self, jobs, roles):
        if self.transaction:
            raise BounceError("An unfinished transaction exists. Restore it first.")
        self.preflight(jobs, roles)
        self.transaction = str(uuid.uuid4())
        self.fingerprint = self.structure()
        return {"transaction": self.transaction, "baseline": self.scan()}

    def status(self):
        app = self.application
        try:
            dialogs = int(app.open_dialog_count)
            message = str(app.current_dialog_message)
        except AttributeError:
            return {"supported": False, "idle": False, "dialogs": -1,
                    "message": "Live does not expose dialog status; automatic mutation is disabled."}
        return {"supported": True, "idle": dialogs == 0 and not message,
                "dialogs": dialogs, "message": message}

    def require_idle(self):
        status = self.status()
        if not status["idle"]:
            raise BounceError("Close Live's dialogs or finish/cancel its render before changing Set state. " + status["message"])

    def write(self, obj, attribute, value, parameter=False):
        old = parameter_state(obj) if parameter else getattr(obj, attribute)
        if (old[0] if parameter else old) == value and (not parameter or old[1] != 1):
            return
        if parameter and not bool(getattr(obj, "is_enabled", True)):
            raise BounceError("Parameter is controlled or disabled: " + obj.name)
        self.changes.append((obj, attribute, old, parameter))
        setattr(obj, attribute, value)
        actual = getattr(obj, attribute)
        if isinstance(value, (int, float)):
            if abs(float(actual) - float(value)) > 1e-5:
                raise BounceError("Parameter write did not match the requested value.")
        elif actual != value:
            raise BounceError("Live did not accept a state change.")

    def set_parameter(self, parameter, value):
        self.write(parameter, "value", float(value), True)

    def silence(self, track):
        # Silence every descendant's sends as well: group mute alone is not a
        # reliable guarantee that separate return/custom routes contain no voice.
        for t in [track] + self.descendants(track):
            self.set_parameter(t.mixer_device.track_activator, 0)
            for send in t.mixer_device.sends:
                self.set_parameter(send, send.min)

    def bypass(self, track, recursive=False):
        tracks = [track] + (self.descendants(track) if recursive else [])
        for t in tracks:
            for d in t.devices:
                if audio_effect(d):
                    p = device_on(d)
                    self.set_parameter(p, 0)

    def reduce_lead(self, track):
        if getattr(track, "is_frozen", False):
            raise BounceError("Unfreeze before applying the lead trim: " + track.name)
        # A fresh final Utility retains fader automation and device drive. It
        # also attenuates this root's own pre-fader sends. Descendant sends that
        # bypass a selected group do not traverse the trim, like a group fader.
        before = list(track.devices)
        track.insert_device("Utility", len(before))
        added = [d for d in track.devices if d not in before]
        if len(added) != 1:
            raise BounceError("Could not identify the temporary Utility.")
        d = added[0]
        self.utilities.append((track, d))  # register before any further writes
        d.name = "AbleBounce temporary −6 dB"
        gain = next((p for p in d.parameters if p.name == "Gain"), None)
        if gain is None:
            raise BounceError("Utility Gain is not available.")
        # Internal gain scales differ across Live parameters. Set the displayed
        # dB value if supported; otherwise derive the internal value using the
        # parameter's own formatter, never by assuming a normalized scale.
        try:
            gain.display_value = -6.0
        except (AttributeError, RuntimeError):
            lo, hi = gain.min, gain.max
            for _ in range(48):
                mid = (lo + hi) / 2
                s = gain.str_for_value(mid).replace("dB", "").strip()
                db = -math.inf if "inf" in s.lower() else float(s)
                if db < -6:
                    lo = mid
                else:
                    hi = mid
            gain.value = (lo + hi) / 2
        display = str(gain).replace("dB", "").strip()
        if not math.isclose(float(display), -6.0, abs_tol=0.011):
            raise BounceError("Temporary Utility gain readback is not −6 dB.")

    def prepare(self, job, transaction):
        self.require_idle()
        if transaction != self.transaction or not self.transaction:
            raise BounceError("Transaction does not match.")
        self.check_structure()
        self.restore(final=False)
        try:
            if job in (3, 4, 5, 8, 9):
                self.bypass(self.song.master_track)
            if job in (2, 3):
                for role in ("Lead", "Backing"):
                    for t in self.selected_roots(role):
                        self.silence(t)
            elif job in (5, 6):
                for t in self.selected_roots("Lead"):
                    self.silence(t)
            elif job == 7:
                for t in self.selected_roots("Lead"):
                    self.reduce_lead(t)
            elif job == 8:
                for i, t in enumerate(self.tracks()):
                    self.write(t, "name", "__AB_T%04d__%s" % (i + 1, t.name))
                for i, t in enumerate(self.song.return_tracks):
                    self.write(t, "name", "__AB_R%04d__%s" % (i + 1, t.name))
            elif job == 9:
                roots = self.selected_roots("Lead")
                inherited_mute = [t for t in roots if any(p.mute for p in self.ancestors(t))]
                keep = set()
                for t in roots:
                    keep.update([t] + self.descendants(t))
                for t in self.tracks():
                    if t not in keep:
                        self.set_parameter(t.mixer_device.track_activator, 0)
                    for send in t.mixer_device.sends:
                        self.set_parameter(send, send.min)
                for t in self.song.return_tracks:
                    self.set_parameter(t.mixer_device.track_activator, 0)
                for t in roots:
                    self.bypass(t, recursive=True)
                    self.write(t, "output_routing_type", self.main_routing(t))
                for t in inherited_mute:
                    self.set_parameter(t.mixer_device.track_activator, 0)
            if job not in range(1, 10):
                raise BounceError("Unknown delivery variant.")
            return {"prepared": job, "transaction": self.transaction,
                    "source": "All Individual Tracks" if job == 8 else "Main"}
        except Exception:
            self.restore(final=False)
            raise

    def restore(self, final=True):
        errors = []
        remaining = []
        for obj, attr, old, is_parameter in reversed(self.changes):
            try:
                if is_parameter:
                    value, automation = old
                    obj.value = value
                    if automation == 1:
                        obj.re_enable_automation()
                    elif int(obj.automation_state) != automation:
                        raise BounceError("Could not restore automation state on " + obj.name)
                    if automation != 1 and abs(obj.value - value) > 1e-5:
                        raise BounceError("Restoration value mismatch on " + obj.name)
                else:
                    setattr(obj, attr, old)
                    if getattr(obj, attr) != old:
                        raise BounceError("Restoration readback mismatch")
            except Exception as e:
                errors.append(str(e))
                remaining.append((obj, attr, old, is_parameter))
        self.changes = list(reversed(remaining))
        leftover = []
        for track, device in reversed(self.utilities):
            try:
                devices = list(track.devices)
                if device in devices:
                    track.delete_device(devices.index(device))
            except Exception as e:
                errors.append(str(e))
                leftover.append((track, device))
        self.utilities = list(reversed(leftover))
        if errors:
            raise BounceError("Restoration incomplete. Keep the working copy open: " + "; ".join(errors))
        if final:
            self.transaction = None
            self.fingerprint = None
        return {"restored": True, "transaction": self.transaction}

    def dispatch(self, method, arguments):
        if method == "ping":
            return {"protocol": 1, "transaction": self.transaction}
        if method == "scan":
            return self.scan()
        if method == "status":
            return self.status()
        if method == "preflight":
            return self.preflight(arguments["jobs"], arguments["roles"])
        if method == "begin":
            return self.begin(arguments["jobs"], arguments["roles"])
        if method == "prepare":
            return self.prepare(int(arguments["job"]), arguments["transaction"])
        if method == "restore":
            self.require_idle()
            if arguments.get("transaction") != self.transaction:
                raise BounceError("Transaction does not match.")
            return self.restore(final=bool(arguments.get("final", True)))
        raise BounceError("Unknown method: " + str(method))
