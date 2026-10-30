"""Regression tests against a strict fake of the Live API, not audio integration."""
import importlib.util
import math
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("ab_engine", Path(__file__).parents[1] / "remote_script/AbleBounce/engine.py")
ab = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ab)


class Parameter:
    def __init__(self, name, value, automation=0, low=0, high=1):
        self.name = name
        self._value = value
        self.automation_state = automation
        self.is_enabled = True
        self.min, self.max = low, high
        self.fail = False

    @property
    def value(self):
        return self._value

    @value.setter
    def value(self, value):
        if self.fail:
            raise RuntimeError("simulated write failure")
        self._value = value
        if self.automation_state == 1:
            self.automation_state = 2

    def re_enable_automation(self):
        self.automation_state = 1

    def str_for_value(self, value):
        if self.name == "Gain":
            return "%.4f dB" % (value * 70 - 35)
        return str(value)

    @property
    def display_value(self):
        if self.name == "Gain":
            return self.value * 70 - 35
        return self.value

    @display_value.setter
    def display_value(self, value):
        self.value = (value + 35) / 70 if self.name == "Gain" else value

    def __str__(self):
        return self.str_for_value(self.value)


class Device:
    def __init__(self, name, on=1, automation=0, kind="audio_effect"):
        self.name, self.type, self.class_name = name, kind, name
        self.parameters = [Parameter("Device On", on, automation)]
        self.chains = []


class Route:
    def __init__(self, name):
        self.display_name = name


class Mixer:
    def __init__(self):
        self.volume = Parameter("Volume", 0.77)
        self.track_activator = Parameter("Track Activator", 1)
        self.sends = [Parameter("A", .4), Parameter("B", .25)]


class Track:
    def __init__(self, name, parent=None, group=False):
        self.name, self.group_track, self.is_foldable = name, parent, group
        self.mixer_device = Mixer()
        self.devices = [Device("Compressor"), Device("Disabled FX", 0)]
        self.solo, self.color, self.has_audio_output = False, 0xAABBCC, True
        self.has_midi_input = False
        self.is_frozen = self.back_to_arranger = False
        self.available_output_routing_types = [Route("Main"), Route("Bus")]
        self.output_routing_type = self.available_output_routing_types[0]
        if parent:
            self.output_routing_type = Route(parent.name)

    @property
    def mute(self):
        return not bool(self.mixer_device.track_activator.value)

    def insert_device(self, name, index):
        d = Device(name)
        d.parameters.append(Parameter("Gain", .5))
        self.devices.insert(index, d)

    def delete_device(self, index):
        del self.devices[index]


class Song:
    def __init__(self):
        group = Track("Lead Vox", group=True)
        self.tracks = [Track("Drums"), group, Track("Lead A", group), Track("Lead B", group), Track("BGV")]
        self.return_tracks = [Track("Reverb")]
        self.master_track = Track("Main")
        self.file_path, self.name = "C:/Project/Song.als", "Song"
        self.loop_start, self.loop_length = 8, 64
        self.is_playing = self.record_mode = self.back_to_arranger = False


class Application:
    open_dialog_count = 0
    current_dialog_message = ""
    def get_major_version(self): return 12
    def get_minor_version(self): return 4
    def get_bugfix_version(self): return 6


class EngineTests(unittest.TestCase):
    def setUp(self):
        self.song = Song()
        self.engine = ab.Engine(self.song, Application())
        self.roles = {"t:0": "Instrument", "t:1": "Lead", "t:2": "Inherit", "t:3": "Inherit", "t:4": "Backing"}

    def begin(self, jobs=None):
        return self.engine.begin(jobs or [1, 2, 3, 4, 5, 6, 7, 8, 9], self.roles)["transaction"]

    def snapshot(self):
        return [(t, t.name, t.mute, t.solo, t.output_routing_type, tuple(t.devices),
                 tuple((p, p.value, p.automation_state) for p in [t.mixer_device.volume, t.mixer_device.track_activator] + t.mixer_device.sends + [p for d in t.devices for p in d.parameters]))
                for t in self.engine.all_tracks()]

    def test_all_nine_restore_to_exact_baseline(self):
        original = self.snapshot()
        token = self.begin()
        for job in range(1, 10):
            with self.subTest(job=job):
                self.engine.prepare(job, token)
                self.engine.restore(final=False)
                self.assertEqual(original, self.snapshot())

    def test_master_disabled_effect_stays_disabled(self):
        token = self.begin([4])
        self.engine.prepare(4, token)
        self.assertEqual([p.parameters[0].value for p in self.song.master_track.devices], [0, 0])
        self.engine.restore()
        self.assertEqual([p.parameters[0].value for p in self.song.master_track.devices], [1, 0])

    def test_group_children_not_trimmed_twice(self):
        self.roles["t:2"] = "Lead"
        token = self.begin([7])
        self.engine.prepare(7, token)
        self.assertEqual(len(self.song.tracks[1].devices), 3)
        self.assertEqual(len(self.song.tracks[2].devices), 2)
        self.assertEqual(len(self.song.tracks[3].devices), 2)

    def test_trim_retains_fader_automation(self):
        volume = self.song.tracks[1].mixer_device.volume
        volume.automation_state = 1
        original = parameter = ab.parameter_state(volume)
        token = self.begin([7])
        self.engine.prepare(7, token)
        self.assertEqual(ab.parameter_state(volume), original)
        self.assertAlmostEqual(float(str(self.song.tracks[1].devices[-1].parameters[1]).replace("dB", "")), -6)
        self.engine.restore()
        self.assertEqual(ab.parameter_state(volume), parameter)

    def test_trim_formatter_fallback_uses_decibels(self):
        class LegacyParameter(Parameter):
            @property
            def display_value(self): raise AttributeError()
            @display_value.setter
            def display_value(self, value): raise AttributeError()
        t = self.song.tracks[1]
        def insert(name, index):
            d = Device(name)
            d.parameters.append(LegacyParameter("Gain", .5))
            t.devices.insert(index, d)
        t.insert_device = insert
        token = self.begin([7])
        self.engine.prepare(7, token)
        self.assertAlmostEqual(t.devices[-1].parameters[1].value * 70 - 35, -6, places=3)

    def test_instrumental_silences_all_vocal_sends(self):
        token = self.begin([2])
        self.engine.prepare(2, token)
        self.assertFalse(self.song.tracks[0].mute)
        for t in self.song.tracks[1:]:
            self.assertTrue(t.mute)
            self.assertEqual([p.value for p in t.mixer_device.sends], [0, 0])

    def test_backing_mix_preserves_backing_and_master(self):
        token = self.begin([6])
        self.engine.prepare(6, token)
        self.assertFalse(self.song.tracks[4].mute)
        self.assertEqual(self.song.master_track.devices[0].parameters[0].value, 1)

    def test_dry_bypasses_devices_and_routes_root_to_main(self):
        token = self.begin([9])
        self.engine.prepare(9, token)
        self.assertTrue(self.song.tracks[0].mute)
        self.assertTrue(self.song.tracks[4].mute)
        self.assertTrue(self.song.return_tracks[0].mute)
        for t in self.song.tracks[1:4]:
            self.assertEqual([d.parameters[0].value for d in t.devices], [0, 0])
        self.assertEqual(self.song.tracks[1].output_routing_type.display_name, "Main")

    def test_dry_does_not_bypass_instrument_device(self):
        d = Device("Sampler", kind="instrument")
        self.song.tracks[2].devices.insert(0, d)
        token = self.begin([9])
        self.engine.prepare(9, token)
        self.assertEqual(d.parameters[0].value, 1)

    def test_dry_child_bypasses_parent_bus_by_routing(self):
        self.roles["t:1"] = "Unassigned"
        self.roles["t:2"] = "Lead"
        self.roles["t:3"] = "Instrument"
        token = self.begin([9])
        self.engine.prepare(9, token)
        self.assertEqual(self.song.tracks[2].output_routing_type.display_name, "Main")
        self.assertTrue(self.song.tracks[1].mute)

    def test_multitrack_has_stable_unique_markers(self):
        self.song.tracks[0].name = self.song.tracks[4].name = "Same"
        token = self.begin([8])
        self.engine.prepare(8, token)
        self.assertTrue(self.song.tracks[0].name.startswith("__AB_T0001__"))
        self.assertTrue(self.song.tracks[4].name.startswith("__AB_T0005__"))
        self.assertTrue(self.song.return_tracks[0].name.startswith("__AB_R0001__"))

    def test_next_job_starts_from_baseline(self):
        token = self.begin([3, 1])
        self.engine.prepare(3, token)
        self.engine.prepare(1, token)
        self.assertFalse(self.song.tracks[1].mute)
        self.assertEqual(self.song.master_track.devices[0].parameters[0].value, 1)

    def test_originally_muted_track_stays_muted(self):
        self.song.tracks[2].mixer_device.track_activator.value = 0
        token = self.begin([2])
        self.engine.prepare(2, token)
        self.engine.restore()
        self.assertTrue(self.song.tracks[2].mute)

    def test_active_automation_restored(self):
        p = self.song.master_track.devices[0].parameters[0]
        p.automation_state = 1
        token = self.begin([4])
        self.engine.prepare(4, token)
        self.assertEqual(p.automation_state, 2)
        self.engine.restore()
        self.assertEqual(p.automation_state, 1)

    def test_currently_off_automated_device_still_overridden(self):
        p = self.song.master_track.devices[0].parameters[0]
        p.value = 0; p.automation_state = 1
        token = self.begin([4])
        self.engine.prepare(4, token)
        self.assertEqual(p.automation_state, 2)
        self.engine.restore()
        self.assertEqual(p.automation_state, 1)

    def test_preexisting_automation_override_preserved(self):
        p = self.song.master_track.devices[0].parameters[0]
        p.automation_state = 2
        token = self.begin([4])
        self.engine.prepare(4, token); self.engine.restore()
        self.assertEqual((p.value, p.automation_state), (1, 2))

    def test_controlled_parameter_rolls_back_preparation(self):
        p = self.song.master_track.devices[0].parameters[0]
        p.is_enabled = False
        token = self.begin([4])
        with self.assertRaises(ab.BounceError): self.engine.prepare(4, token)
        self.assertEqual(p.value, 1)

    def test_conflicting_group_roles_fail(self):
        self.roles["t:2"] = "Backing"
        with self.assertRaises(ab.BounceError): self.begin([2])

    def test_unknown_tracks_fail_vocal_jobs(self):
        self.roles["t:0"] = "Unassigned"
        with self.assertRaises(ab.BounceError): self.begin([2])

    def test_unknown_tracks_allowed_for_full_mix(self):
        self.roles["t:0"] = "Unassigned"
        self.begin([1])

    def test_frozen_dry_rejected(self):
        self.song.tracks[2].is_frozen = True
        with self.assertRaises(ab.BounceError): self.begin([9])

    def test_custom_vocal_bus_rejected(self):
        self.song.tracks[2].output_routing_type = Route("Unreviewed Bus")
        with self.assertRaises(ab.BounceError): self.begin([2])

    def test_existing_solo_rejected(self):
        self.song.tracks[0].solo = True
        with self.assertRaises(ab.BounceError): self.begin([1])

    def test_session_playback_rejected(self):
        self.song.tracks[0].back_to_arranger = True
        with self.assertRaises(ab.BounceError): self.begin([1])

    def test_structure_change_rejected(self):
        token = self.begin([2])
        self.song.tracks.reverse()
        with self.assertRaises(ab.BounceError): self.engine.prepare(2, token)

    def test_bad_transaction_rejected(self):
        self.begin([1])
        with self.assertRaises(ab.BounceError): self.engine.prepare(1, "wrong")

    def test_failed_restore_remains_retryable(self):
        p = self.song.master_track.devices[0].parameters[0]
        token = self.begin([4]); self.engine.prepare(4, token)
        p.fail = True
        with self.assertRaises(ab.BounceError): self.engine.restore()
        self.assertIsNotNone(self.engine.transaction)
        self.assertTrue(self.engine.changes)
        p.fail = False
        self.engine.restore()
        self.assertEqual(p.value, 1)
        self.assertIsNone(self.engine.transaction)

    def test_doubly_begin_rejected(self):
        self.begin([1])
        with self.assertRaises(ab.BounceError): self.begin([1])

    def test_scan_includes_hidden_children_and_returns(self):
        scan = self.engine.scan()
        self.assertEqual(scan["version"], "12.4.6")
        self.assertEqual(len(scan["tracks"]), 7)
        self.assertEqual(scan["tracks"][2]["parent"], "t:1")
        self.assertTrue(scan["tracks"][-1]["master"])

    def test_name_suggestions_are_conservative(self):
        self.assertEqual(ab.role_guess("Main Vocal 2"), "Lead")
        self.assertEqual(ab.role_guess("BGV Harmony"), "Backing")
        self.assertEqual(ab.role_guess("Vocal"), "Unassigned")
        self.assertEqual(ab.role_guess("Audio 17"), "Unassigned")

    def test_mutation_during_render_is_rejected(self):
        token = self.begin([4])
        self.engine.application.open_dialog_count = 1
        self.engine.application.current_dialog_message = "Rendering audio…"
        with self.assertRaises(ab.BounceError): self.engine.prepare(4, token)
        with self.assertRaises(ab.BounceError): self.engine.dispatch("restore", {"transaction": token})
        self.assertEqual(self.song.master_track.devices[0].parameters[0].value, 1)

    def test_master_unsupported_mute_is_never_read(self):
        class MasterTrack(Track):
            @property
            def mute(self): raise RuntimeError("not in master track")
        self.song.master_track = MasterTrack("Main")
        self.assertFalse(self.engine.scan()["tracks"][-1]["mute"])
        self.begin([1])

    def test_frozen_trim_root_rejected_before_transaction(self):
        self.song.tracks[1].is_frozen = True
        with self.assertRaises(ab.BounceError): self.begin([7])
        self.assertIsNone(self.engine.transaction)

    def test_dry_does_not_unmute_a_muted_parent(self):
        self.roles["t:1"] = "Unassigned"
        self.roles["t:2"] = "Lead"
        self.roles["t:3"] = "Instrument"
        self.song.tracks[1].mixer_device.track_activator.value = 0
        token = self.begin([9])
        self.engine.prepare(9, token)
        self.assertTrue(self.song.tracks[2].mute)


if __name__ == "__main__":
    unittest.main()
