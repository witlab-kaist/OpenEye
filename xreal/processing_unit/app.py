from __future__ import annotations

import argparse
import json
import socket
import threading
import time
from math import radians, tan

import cv2
import numpy as np

from ..gui_unit.core.config import DEFAULT_CONFIG as CFG

# ==============================
# Config-derived constants
# ==============================
WINDOW_NAME = "XREAL Viewer"

H_RANGE_DEG = CFG.eval.h_range_deg
V_RANGE_DEG = CFG.eval.v_range_deg
CALIB_W = int(CFG.eval.calibration_width)
CALIB_H = int(CFG.eval.calibration_height)
DIST_M = float(CFG.eval.view_distance_m)
WIDTH_M = float(CFG.eval.display_width_m)
HEIGHT_M = float(CFG.eval.display_height_m)

SOCK_PATH = "/tmp/neon_ipc.sock"
EVAL_INACTIVITY_SEC = 2.0
GAZE_TIMEOUT_SEC = 0.5


# ==============================
# Geometry helpers
# ==============================

def deg_to_px(deg_x: float, deg_y: float) -> tuple[int, int]:
    x_m = DIST_M * tan(radians(deg_x))
    y_m = DIST_M * tan(radians(deg_y))
    x_px = CALIB_W / 2 + (x_m / (WIDTH_M / 2)) * (CALIB_W / 2)
    y_px = CALIB_H / 2 + (y_m / (HEIGHT_M / 2)) * (CALIB_H / 2)
    return int(x_px), int(y_px)

xs_deg = np.linspace(H_RANGE_DEG[0], H_RANGE_DEG[1], 5)
ys_deg = np.linspace(V_RANGE_DEG[0], V_RANGE_DEG[1], 5)
CALIB_TARGETS = [deg_to_px(xd, yd) for yd in ys_deg for xd in xs_deg]

X_LEFT, _ = deg_to_px(H_RANGE_DEG[0], 0.0)
X_RIGHT, _ = deg_to_px(H_RANGE_DEG[1], 0.0)
_, Y_TOP = deg_to_px(0.0, V_RANGE_DEG[0])
_, Y_BOTTOM = deg_to_px(0.0, V_RANGE_DEG[1])

# ==============================
# Shared state / IPC
# ==============================

class SharedState:

    def __init__(self):
        self.lock = threading.Lock()

        # Modes: "idle" | "calib" | "eval"
        self.mode = "idle"

        # Calibration state
        self.calib_enabled = False
        self.calib_idx = -1
        self.calib_done = False
        self.calib_done_ts = 0.0

        # Evaluation state
        self.eval_has_target = False
        self.eval_px: tuple[float, float] | None = None
        self.eval_last_ts = 0.0

        # Gaze overlay state
        self.gaze_px: tuple[int, int] | None = None
        self.gaze_last_ts = 0.0

    def set_mode(self, mode: str) -> None:
        with self.lock:
            self.mode = mode

    def calib_stop(self) -> None:
        with self.lock:
            self.calib_enabled = False
            self.calib_idx = -1
            self.mode = "idle"

    def set_calib_step(self, step_1based: int) -> None:
        with self.lock:
            self.mode = "calib"
            self.calib_enabled = True
            self.calib_done = False
            self.calib_idx = int(step_1based)

    def set_calib_end(self) -> None:
        with self.lock:
            self.calib_enabled = False
            self.calib_done = True
            self.calib_done_ts = time.time()
            self.calib_idx = len(CALIB_TARGETS)
            self.mode = "idle"

    def send_eval_target(self, px: float, py: float) -> None:
        now = time.time()
        with self.lock:
            self.eval_px = (px, py)
            self.eval_has_target = True
            self.eval_last_ts = now
            self.mode = "eval"

    def eval_stop(self) -> None:
        with self.lock:
            self.eval_has_target = False
            self.eval_px = None
            self.mode = "idle"

    def set_gaze_visual(self, x: float, y: float) -> None:
        with self.lock:
            self.gaze_px = (int(x), int(y))
            self.gaze_last_ts = time.time()


def ipc_recv_loop(state: SharedState) -> None:
    s = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
    print(f"[PROCESSING] connecting to {SOCK_PATH} ...")
    s.connect(SOCK_PATH)
    print("[PROCESSING] connected")

    buf = b""
    try:
        while True:
            chunk = s.recv(4096)
            if not chunk:
                print("[PROCESSING] server closed")
                break
            buf += chunk
            while b"\n" in buf:
                line, buf = buf.split(b"\n", 1)
                if not line.strip():
                    continue
                try:
                    msg = json.loads(line.decode("utf-8"))
                except Exception as e:
                    print("[PROCESSING] JSON parse error:", e)
                    continue
                handle_msg(state, msg)
    except Exception as e:
        print("[PROCESSING] recv loop error:", e)
    finally:
        s.close()


def handle_msg(state: SharedState, msg: dict) -> None:
    mtype = msg.get("type")
    payload = msg.get("payload", {})

    if mtype == "updateStep":
        step = int(payload.get("step", 1))
        print(f"[PROCESSING] STEP -> {step}")
        state.set_calib_step(step)
        return

    if mtype == "calibrationEnd":
        print("[PROCESSING] END signal received")
        state.set_calib_end()
        return

    if mtype == "evalTarget":
        px = float(payload.get("x", payload.get("px", 0.0)))
        py = float(payload.get("y", payload.get("py", 0.0)))
        state.send_eval_target(px, py)
        return

    if mtype == "gazeVisual":
        x = float(payload.get("x", 0.0))
        y = float(payload.get("y", 0.0))
        state.set_gaze_visual(x, y)
        return

    print("[PROCESSING] unknown message:", msg)


# ==============================
# Rendering
# ==============================

def render_loop(state: SharedState) -> None:
    cv2.namedWindow(WINDOW_NAME, cv2.WINDOW_NORMAL)
    cv2.resizeWindow(WINDOW_NAME, CALIB_W, CALIB_H)

    while True:
        key = cv2.waitKey(10) & 0xFF
        if key in (ord("q"), ord("Q"), 27):
            break

        frame = np.zeros((CALIB_H, CALIB_W, 3), dtype=np.uint8)

        cv2.rectangle(
            frame,
            (int(X_LEFT), int(Y_TOP)),
            (int(X_RIGHT), int(Y_BOTTOM)),
            (40, 40, 40),
            1,
            lineType=cv2.LINE_AA,
        )

        with state.lock:
            mode = state.mode
            calib_enabled = state.calib_enabled
            idx = state.calib_idx
            calib_done = state.calib_done
            done_ts = state.calib_done_ts
            eval_has_target = state.eval_has_target
            eval_px = state.eval_px
            eval_last_ts = state.eval_last_ts
            gaze_px = state.gaze_px
            gaze_last_ts = state.gaze_last_ts

        if mode == "calib" and calib_enabled:
            if 0 <= idx < len(CALIB_TARGETS):
                tx, ty = CALIB_TARGETS[idx]
                cv2.circle(frame, (tx, ty), 16, (255, 255, 255), -1)
            elif idx >= len(CALIB_TARGETS) or calib_done:
                if time.time() - done_ts > 1.0:
                    state.calib_stop()

        elif mode == "eval":
            if time.time() - eval_last_ts > EVAL_INACTIVITY_SEC:
                state.eval_stop()
            elif eval_has_target and eval_px is not None:
                ex, ey = eval_px
                cv2.circle(frame, (int(ex), int(ey)), 12, (255, 255, 255), -1)

        if gaze_px is not None and time.time() - gaze_last_ts <= GAZE_TIMEOUT_SEC:
            gx, gy = gaze_px
            cv2.circle(frame, (int(gx), int(gy)), 6, (0, 200, 255), -1)
            cv2.line(frame, (int(gx), 0), (int(gx), CALIB_H), (80, 80, 80), 1)
            cv2.line(frame, (0, int(gy)), (CALIB_W, int(gy)), (80, 80, 80), 1)
        else:
            with state.lock:
                state.gaze_px = None

        cv2.imshow(WINDOW_NAME, frame)

    cv2.destroyAllWindows()


# ==============================
# CLI entrypoint
# ==============================

def _build_arg_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(prog="openeye-xreal-processing")
    p.add_argument(
        "--window-name",
        type=str,
        default=WINDOW_NAME,
        help="Override the OpenCV window title.",
    )
    return p


def main(argv=None) -> int:
    global WINDOW_NAME

    args = _build_arg_parser().parse_args(argv)
    WINDOW_NAME = args.window_name

    state = SharedState()
    t = threading.Thread(target=ipc_recv_loop, args=(state,), daemon=True)
    t.start()

    try:
        render_loop(state)
    finally:
        time.sleep(0.1)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
