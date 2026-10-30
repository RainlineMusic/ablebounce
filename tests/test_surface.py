"""Transport backpressure must not truncate a pending Live response."""
import importlib.util
from pathlib import Path
import sys
import time
import types
import unittest
from unittest.mock import patch

framework = types.ModuleType("_Framework")
control = types.ModuleType("_Framework.ControlSurface")
class Base:
    def update_display(self): pass
control.ControlSurface = Base
package = types.ModuleType("ab_transport_test")
package.__path__ = []
engine = types.ModuleType("ab_transport_test.engine")
engine.Engine = object
spec = importlib.util.spec_from_file_location("ab_transport_test.surface",
    Path(__file__).parents[1] / "remote_script/AbleBounce/surface.py")
transport = importlib.util.module_from_spec(spec)
with patch.dict(sys.modules, {"_Framework": framework, "_Framework.ControlSurface": control,
                             "ab_transport_test": package, "ab_transport_test.engine": engine}):
    spec.loader.exec_module(transport)

class Server:
    def accept(self): raise BlockingIOError()
class Client:
    def __init__(self, outcomes): self.outcomes = iter(outcomes); self.closed = False
    def send(self, data):
        result = next(self.outcomes)
        if isinstance(result, Exception): raise result
        return result
    def close(self): self.closed = True

class TransportTests(unittest.TestCase):
    def make(self, outcomes):
        bridge = transport.AbleBounce.__new__(transport.AbleBounce)
        client = Client(outcomes)
        bridge._server = Server()
        bridge._clients = {client: [bytearray(), bytearray(b"reply\n"), time.monotonic()]}
        return bridge, client
    def test_would_block_retries_without_losing_response(self):
        bridge, client = self.make([BlockingIOError(), 6])
        bridge.update_display()
        self.assertFalse(client.closed)
        self.assertEqual(bridge._clients[client][1], b"reply\n")
        bridge.update_display()
        self.assertTrue(client.closed)
        self.assertNotIn(client, bridge._clients)
    def test_partial_write_preserves_unsent_bytes(self):
        bridge, client = self.make([2, 4])
        bridge.update_display()
        self.assertEqual(bridge._clients[client][1], b"ply\n")
        bridge.update_display()
        self.assertTrue(client.closed)
    def test_expired_blocked_response_is_closed(self):
        bridge, client = self.make([])
        bridge._clients[client][2] = time.monotonic() - 31
        bridge.update_display()
        self.assertTrue(client.closed)
        self.assertNotIn(client, bridge._clients)

    def test_zero_write_closes_connection(self):
        bridge, client = self.make([0])
        bridge.update_display()
        self.assertTrue(client.closed)
        self.assertNotIn(client, bridge._clients)

