from __future__ import annotations
import os
import json
import socket
import threading
import time
from pathlib import Path
from typing import Optional, Callable

SOCK_PATH = "/tmp/neon_ipc.sock"

def _safe_unlink(path: str):
    try:
        if os.path.exists(path):
            os.unlink(path)
    except Exception:
        pass

class IpcServer(threading.Thread):
    def __init__(self, on_status: Optional[Callable[[str], None]] = None):
        super().__init__(daemon=True)
        self.on_status = on_status or (lambda s: None)
        self.sock: Optional[socket.socket] = None
        self.conn: Optional[socket.socket] = None
        self._lock = threading.Lock()
        self._running = False

    def run(self):
        _safe_unlink(SOCK_PATH)
        self.sock = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        self.sock.bind(SOCK_PATH)
        os.chmod(SOCK_PATH, 0o666)
        self.sock.listen(1)
        self._running = True
        self.on_status("listening")

        try:
            self.conn, _ = self.sock.accept()
            self.on_status("connected")
            while self._running:
                time.sleep(0.05)
        except Exception as e:
            self.on_status(f"error: {e}")
        finally:
            self._cleanup()

    def _cleanup(self):
        with self._lock:
            try:
                if self.conn:
                    self.conn.close()
                    self.conn = None
            except Exception:
                pass
            try:
                if self.sock:
                    self.sock.close()
                    self.sock = None
            except Exception:
                pass
            _safe_unlink(SOCK_PATH)
            self.on_status("closed")

    def close(self):
        self._running = False
        try:
            if self.conn:
                self.conn.shutdown(socket.SHUT_RDWR)
        except Exception:
            pass

    def _send(self, obj: dict):
        with self._lock:
            if not self.conn:
                return
            data = (json.dumps(obj, ensure_ascii=False) + "\n").encode("utf-8")
            self.conn.sendall(data)

    def send_step(self, step: int):
        self._send({"type": "updateStep", "payload": {"step": int(step)}})

    def send_end_signal(self):
        self._send({"type": "calibrationEnd", "payload": {}})

    def send_eval_target(self, px: float, py: float, t_ms: Optional[int] = None):
        self._send({
            "type": "evalTarget",
            "payload": {"px": float(px), "py": float(py), "t_ms": (None if t_ms is None else int(t_ms))}
        })

    def send_gaze_visual(self, ts: float, x: float, y: float):
        self._send({
            "type": "gazeVisual",
            "payload": {"t": float(ts), "x": float(x), "y": float(y)}
        })

class EvalPlanStreamer(threading.Thread):
    def __init__(self, ipc: IpcServer, plan_path: str, tick_hz: int = 50):
        super().__init__(daemon=True)
        self.ipc = ipc
        self.plan_path = Path(plan_path)
        self.tick_hz = int(tick_hz)
        self._running = False
        self._timeline = []
        self._interval = 1.0 / self.tick_hz

    def start(self):
        with self.plan_path.open("r", encoding="utf-8") as f:
            plan = json.load(f)
        self._timeline = plan.get("timeline", [])
        self._running = True
        super().start()

    def run(self):
        if not self._timeline:
            return

        start_mono = time.monotonic()
        use_deadline = "t_ms" in self._timeline[0]

        for i, frame in enumerate(self._timeline):
            if not self._running:
                break

            pos = frame.get("pos", {})
            px = float(pos.get("x", 0.0))
            py = float(pos.get("y", 0.0))

            if use_deadline:
                t_ms = int(frame.get("t_ms", i * int(self._interval * 1000)))
                deadline = start_mono + (t_ms / 1000.0)
                now = time.monotonic()
                sleep_s = deadline - now
                if sleep_s > 0:
                    time.sleep(sleep_s)
            else:
                deadline = start_mono + (i + 1) * self._interval
                now = time.monotonic()
                sleep_s = deadline - now
                if sleep_s > 0:
                    time.sleep(sleep_s)

            if not self._running:
                break

            try:
                if use_deadline:
                    self.ipc.send_eval_target(px, py, t_ms=t_ms)
                else:
                    elapsed_ms = int((time.monotonic() - start_mono) * 1000.0)
                    self.ipc.send_eval_target(px, py, t_ms=elapsed_ms)
            except Exception:
                self._running = False
                break

    def stop(self):
        self._running = False

