"""Loopback newline-JSON transport. Live API access stays on the Live thread."""
import json
import os
import socket
import time
import uuid
from _Framework.ControlSurface import ControlSurface
from .engine import Engine

PORT = 16846
MAX_REQUEST = 256 * 1024


class AbleBounce(ControlSurface):
    def __init__(self, c_instance):
        super(AbleBounce, self).__init__(c_instance)
        self._engine = Engine(self.song(), self.application())
        self._clients = {}
        self._server = None
        root = os.path.join(os.environ.get("LOCALAPPDATA", os.path.expanduser("~")), "AbleBounce")
        os.makedirs(root, exist_ok=True)
        token_path = os.path.join(root, "bridge-token")
        if os.path.isfile(token_path):
            with open(token_path, "r") as f:
                self._token = f.read().strip()
        else:
            self._token = uuid.uuid4().hex + uuid.uuid4().hex
            with open(token_path, "w") as f:
                f.write(self._token)
        try:
            self._server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            self._server.bind(("127.0.0.1", PORT))
            self._server.listen(4)
            self._server.setblocking(False)
            self.show_message("AbleBounce ready · localhost:%d" % PORT)
        except Exception as e:
            self.log_message("AbleBounce: " + str(e))
            self.show_message("AbleBounce could not open its local port. Check another Live instance.")
            if self._server:
                self._server.close()
            self._server = None

    def update_display(self):
        super(AbleBounce, self).update_display()
        if not self._server:
            return
        # A bounded amount of work per tick; do not block audio/UI threads on IO.
        for _ in range(4):
            try:
                client, _address = self._server.accept()
                client.setblocking(False)
                self._clients[client] = [bytearray(), bytearray(), time.monotonic()]
            except BlockingIOError:
                break
        for client, state in list(self._clients.items()):
            try:
                incoming, outgoing, created = state
                if outgoing:
                    sent = client.send(outgoing)
                    del outgoing[:sent]
                    if not outgoing:
                        self._close(client)
                    continue
                if time.monotonic() - created > 30:
                    self._close(client)
                    continue
                try:
                    data = client.recv(8192)
                except BlockingIOError:
                    continue
                if not data:
                    self._close(client)
                    continue
                incoming.extend(data)
                if len(incoming) > MAX_REQUEST:
                    self._close(client)
                    continue
                if b"\n" not in incoming:
                    continue
                request = None
                try:
                    request = json.loads(bytes(incoming).split(b"\n", 1)[0].decode("utf-8"))
                    if request.get("token") != self._token:
                        raise ValueError("Local bridge token mismatch. Reconnect AbleBounce.")
                    result = self._engine.dispatch(request["method"], request.get("args", {}))
                    reply = {"id": request.get("id"), "ok": True, "result": result}
                except Exception as e:
                    reply = {"id": request.get("id") if isinstance(request, dict) else None,
                             "ok": False, "error": str(e)}
                outgoing.extend((json.dumps(reply, ensure_ascii=False) + "\n").encode("utf-8"))
            except (OSError, ValueError):
                self._close(client)

    def _close(self, client):
        self._clients.pop(client, None)
        client.close()

    def disconnect(self):
        # Called by Live, not a desktop watchdog. Best effort only; the untouched
        # saved working Set is the recovery point if Live itself is closing.
        try:
            if self._engine.transaction:
                self._engine.restore()
        except Exception as e:
            self.log_message("AbleBounce restoration during disconnect: " + str(e))
        for client in list(self._clients):
            self._close(client)
        if self._server:
            self._server.close()
        super(AbleBounce, self).disconnect()
