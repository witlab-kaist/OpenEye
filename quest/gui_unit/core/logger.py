from __future__ import annotations
import json
import os
import time
import threading
import queue
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict, Optional

class JsonLogger(threading.Thread):
    def __init__(
        self, path: str,
        flush_every: int=100,
        flush_ms: int=100,
        queue_maxsize: int=4096,
    ):
        super().__init__(daemon=True)
        self.path = path
        self.flush_every = int(flush_every)
        self.flush_ms = int(flush_ms)
        self.q: "queue.Queue[dict]" = queue.Queue(maxsize=int(queue_maxsize))

        self._running = threading.Event()
        self._running.set()

        self._f = None
        self._cnt_since_flush = 0
        self._last_flush = time.perf_counter()

    def run(self) -> None:
        os.makedirs(os.path.dirname(self.path), exist_ok=True)
        with open(self.path, "a", encoding="utf-8") as f:
            self._f = f
            while self._running.is_set() or not self.q.empty():
                try:
                    msg = self.q.get(timeout=0.05)
                except queue.Empty:
                    msg = None
                if msg is not None:
                    f.write(json.dumps(msg, ensure_ascii=False, separators=(",", ":")) + "\n")
                    self._cnt_since_flush += 1
                now = time.perf_counter()
                if (self._cnt_since_flush >= self.flush_every) or ((now - self._last_flush) * 1000.0 >= self.flush_ms):
                    f.flush()
                    os.fsync(f.fileno())
                    self._cnt_since_flush = 0
                    self._last_flush = now

    def log(self, msg: dict) -> None:
        try:
            self.q.put_nowait(msg)
        except queue.Full:
            try:
                _ = self.q.get_nowait()
                self.q.put_nowait(msg)
            except queue.Empty:
                pass

    def stop(self) -> None:
        self._running.clear()