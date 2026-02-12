import sys
import os
import threading
import time
import json
import pandas as pd
import numpy as np

from typing import Optional, List, Dict

# ---- PySide6 GUI ----
from PySide6.QtCore import Qt, QTimer, QPointF, Signal, QObject
from PySide6.QtGui import QPixmap, QPainter, QPen, QColor, QFont, QShortcut, QKeySequence
from PySide6.QtWidgets import (
    QApplication, QMainWindow, QWidget, QVBoxLayout, QHBoxLayout,
    QLabel, QPushButton, QCheckBox, QSpinBox, QGroupBox, QLineEdit, QMessageBox
)

from pupil_labs.realtime_api.simple import discover_one_device
from networking import TcpServer, EvalPlanStreamer
from filter import ButterLPFilter
from async_json_logger import AsyncJSONLLogger
from mapping_core import (
    normalize_neon_xy,
    map_biquadratic, map_ridge_biquadratic,
    predict_biquad, predict_ridge_biquad,
    save_models, load_models
)

# ============ Config ============
CANVAS_W, CANVAS_H = 1600, 1200

FS = 200
FC = 15.0
LP_ORDER = 2

RIDGE_ALPHA = 0.01
WINDOW_WIDTH = 100

EVAL_RATE_HZ = 5
EVAL_DRUATION_S = 30
EVAL_DWELL_MS = 1000
H_RANGE_DEG = (-15.0, 15.0)
V_RANGE_DEG = (-10.0, 10.0)
VIEW_DISTANCE_M = 1.0
MIN_SACCADE_AMP_DEG = 3.0

def generate_calib_targets(h_range_deg=H_RANGE_DEG,
                           v_range_deg=V_RANGE_DEG,
                           view_dist_m=VIEW_DISTANCE_M,
                           grid_size=(5, 5)):
    cols, rows = grid_size
    h_deg = np.linspace(h_range_deg[0], h_range_deg[1], cols)
    v_deg = np.linspace(v_range_deg[1], v_range_deg[0], rows)

    h_rad = np.radians(h_deg)
    v_rad = np.radians(v_deg)

    xs = np.tan(h_rad) * view_dist_m
    ys = np.tan(v_rad) * view_dist_m

    coords = [(float(x), float(y)) for y in ys for x in xs]
    return coords

CALIB_TARGET_COORDINATES = generate_calib_targets()

# ============ Collectors ============
class SharedState:
    def __init__(self):
        self.lock = threading.Lock()
        self.running = False
        self.ended = False
        self.recording = False
        self.step = 0

        self.latest_gaze_filtered = (None, None)
        self.gaze_log = []
        self.event: Optional[str] = None
        self.datum_lines = []

        self.eval_active = False
        self.eval_logger = None
        self.tracking_active = False
        self.tracking_logger = None
        self.models = {}
    
    def update_latest_filtered_gaze(self, x, y):
        with self.lock:
            self.latest_gaze_filtered = (x, y)
    
    def append_gaze_log(self, ts, raw_x, raw_y, filt_x, filt_y, event: str=""):
        with self.lock:
            self.gaze_log.append((float(ts), float(filt_x), float(filt_y), float(raw_x), float(raw_y), event))
    
    def set_event(self):
        with self.lock:
            self.event = "event_log"
    
    def consume_event(self):
        with self.lock:
            e = self.event or ""
            self.event = None
            return e

    def snapshot_gaze_log(self):
        with self.lock:
            return list(self.gaze_log)
        
    def add_datum(self, datum):
        line = json.dumps(datum, ensure_ascii=False)
        with self.lock:
            self.datum_lines.append(line)
    
    # ==== evaluation logging ====
    def start_evaluation(self, log_path: str, models: dict, canvas_w: int, canvas_h: int):
        self.models = models or {}
        self.canvas_w, self.canvas_h = int(canvas_w), int(canvas_h)
        self.eval_logger = AsyncJSONLLogger(log_path, flush_every=100, flush_ms=100)
        self.eval_logger.start()
        self.eval_active = True

    def stop_evaluation(self):
        self.eval_active = False
        if self.eval_logger:
            self.eval_logger.stop()
            self.eval_logger.join(timeout=1.0)
            self.eval_logger = None
    
    def append_eval_record(self, ts: float, x_f: float, y_f: float, x_raw: float, y_raw: float):
        if not (self.eval_active and self.eval_logger):
            return
        neon_xy_f = np.array([[float(x_f), float(y_f)]], dtype=float)
        neon_xy_n = normalize_neon_xy(neon_xy_f, self.canvas_w, self.canvas_h)
        msg = {
            "type": "gazeEval",
            "payload": {
                "t": float(ts),
                "raw": {"x": float(x_raw), "y": float(y_raw)},
                "filtered": {"x": float(x_f), "y": float(y_f)},
                "mapped": self._predict_all(neon_xy_n)
            }
        }
        self.eval_logger.log(msg)
    
    # === gaze tracking logging ===
    def start_tracking(self, log_path: str, models: dict, canvas_w: int, canvas_h: int):
        self.models = models or {}
        self.canvas_w, self.canvas_h = int(canvas_w), int(canvas_h)
        self.tracking_logger = AsyncJSONLLogger(log_path, flush_every=100, flush_ms=100)
        self.tracking_logger.start()
        self.tracking_active = True
    
    def stop_tracking(self):
        self.tracking_active = False
        if self.tracking_logger:
            self.tracking_logger.stop()
            self.tracking_logger.join(timeout=1.0)
            self.tracking_logger = None

    def append_tracking_record(self, ts: float, x_f: float, y_f: float, x_raw: float, y_raw: float):
        if not (self.tracking_active and self.tracking_logger):
            return
        neon_xy_f = np.array([[float(x_f), float(y_f)]], dtype=float)
        neon_xy_n = normalize_neon_xy(neon_xy_f, self.canvas_w, self.canvas_h)
        msg = {
            "type": "gazeTrack",
            "payload": {
                "t": float(ts),
                "raw": {"x": float(x_raw), "y": float(y_raw)},
                "filtered": {"x": float(x_f), "y": float(y_f)},
                "mapped": self._predict_all(neon_xy_n)
            }
        }
        self.tracking_logger.log(msg)

    def _predict_all(self, neon_xy_n: np.ndarray) -> dict:
        out = {}
        if "biquadratic" in self.models:
            xy = predict_biquad(self.models["biquadratic"], neon_xy_n)[0]
            out["biquadratic"] = {"x": float(xy[0]), "y": float(xy[1])}
        if "ridge_biquadratic" in self.models:
            xy = predict_ridge_biquad(self.models["ridge_biquadratic"], neon_xy_n)[0]
            out["ridge_biquadratic"] = {"x": float(xy[0]), "y": float(xy[1])}
        return out

class GazeCollector(threading.Thread):
    def __init__(self, device, state: SharedState, lp_filter: ButterLPFilter):
        super().__init__(daemon=True)
        self.device = device
        self.state = state
        self.lp_filter = lp_filter
        self.on_new_filtered = None

    def run(self):
        while self.state.running:
            datum = self.device.receive_gaze_datum()
            x, y = datum.x, datum.y
            ts = datum.timestamp_unix_seconds

            x_f, y_f = self.lp_filter.step(x, y)
            self.state.update_latest_filtered_gaze(x_f, y_f)
            if self.on_new_filtered is not None:
                try: self.on_new_filtered(ts, x_f, y_f, x, y)
                except Exception: pass
            self.state.append_eval_record(ts, x_f, y_f, x, y)
            self.state.append_tracking_record(ts, x_f, y_f, x, y)
            if self.state.recording:
                self.state.add_datum(datum)
                event = self.state.consume_event()
                self.state.append_gaze_log(ts, x, y, x_f, y_f, event)

# ============ GUI ============
class GazeCanvas(QLabel):
    def __init__(self):
        super().__init__()
        self.setFixedSize(400, 300)
        self.pix = QPixmap(CANVAS_W, CANVAS_H)
        self.pix.fill(Qt.black)
        self.setScaledContents(True)
        self.setPixmap(self.pix)

    def draw_frame(self, gaze_xy):
        x, y = gaze_xy if gaze_xy else (None, None)
        self.pix.fill(Qt.black)
        painter = QPainter(self.pix)
        painter.setRenderHint(QPainter.Antialiasing, True)

        painter.setPen(QPen(QColor(60, 60, 60), 4))
        painter.drawRect(2, 2, CANVAS_W-4, CANVAS_H-4)
        painter.setPen(QPen(QColor(200, 200, 200), 1))
        painter.setFont(QFont("Arial", 28))
        painter.drawText(20, 50, "1600 x 1200 Gaze Canvas")

        if x is not None and y is not None:
            px = int(max(0, min(CANVAS_W - 1, x)))
            py = int(max(0, min(CANVAS_H - 1, y)))
            painter.setPen(QPen(QColor(0, 200, 255), 3))
            painter.setBrush(QColor(0, 200, 255))
            painter.drawEllipse(QPointF(px, py), 8, 8)
            painter.setPen(QPen(QColor(200, 200, 200), 2, Qt.DashLine))
            painter.drawLine(px, 0, px, CANVAS_H)
            painter.drawLine(0, py, CANVAS_W, py)
            painter.setPen(QPen(QColor(200, 200, 200), 2))
            painter.setFont(QFont("Arial", 28))
            painter.drawText(20, 90, f"x={px}, y={py}")
        
        painter.end()
        self.setPixmap(self.pix)

class MainWindow(QMainWindow):
    def __init__(self):
        super().__init__()
        self.setWindowTitle("HMD-EyeTracker Calibration")
        self.state = SharedState()
        self.device = None
        self.gaze_thread: Optional[GazeCollector] = None
        self.tcp_thread: Optional[TcpServer] = None
        self.plan_streamer: Optional[EvalPlanStreamer] = None
        self.models: Dict[str, Dict] = {}
        self.eval_active = False
        self.tracking_active = False
        
        self.participant_dir = None
        self.nine_dir = None
        self.eval_log_path = None
        self.eval_log_dir = None

        self.gv_rate_hz = 30                          # sending Hz
        self._gv_lock = threading.Lock()
        self._gv_latest_raw = None                    # (ts, x_f, y_f)
        self.gaze_tx_timer = QTimer(self)
        self.gaze_tx_timer.timeout.connect(self._tick_send_gaze)

        # --- UI ---
        central = QWidget()
        self.setCentralWidget(central)
        root = QVBoxLayout(central)

        ctrl_box = QGroupBox("Controls"); ctrl_layout = QHBoxLayout(ctrl_box)

        self.p_spin = QSpinBox(); self.p_spin.setRange(0, 99); self.p_spin.setValue(0)
        self.neon_status = QLabel("Neon: disconnected")
        self.tcp_status = QLabel("TCP: idle")
        self.step_label = QLabel("Step: idle")
        self.btn_connect_neon = QPushButton("Connect Neon")
        self.btn_start_tcp = QPushButton("Start TCP Server")
        ctrl_layout.addWidget(QLabel("trial:")); ctrl_layout.addWidget(self.p_spin); ctrl_layout.addStretch()
        ctrl_layout.addWidget(self.btn_connect_neon); ctrl_layout.addWidget(self.neon_status)
        ctrl_layout.addWidget(self.btn_start_tcp); ctrl_layout.addWidget(self.tcp_status)
        ctrl_layout.addWidget(self.step_label)

        self.canvas = GazeCanvas()

        task_box = QGroupBox("Tasks"); task_layout = QHBoxLayout(task_box)

        self.btn_eval = QPushButton("Start Evaluation")
        self.btn_track = QPushButton("Start Gaze Tracking")
        self.visualize = QCheckBox("Visualize")
        task_layout.addWidget(self.btn_eval); task_layout.addWidget(self.btn_track); task_layout.addWidget(self.visualize)

        root.addWidget(ctrl_box); root.addWidget(self.canvas); root.addWidget(task_box)

        # wiring
        self.btn_connect_neon.clicked.connect(self.on_connect_neon)
        self.btn_start_tcp.clicked.connect(self.on_start_tcp)
        self.btn_eval.clicked.connect(self.on_toggle_evaluation)
        self.btn_track.clicked.connect(self.on_toggle_gaze_tracking)

        # UI update timer
        self.timer = QTimer(self); self.timer.timeout.connect(self.repaint_canvas); self.timer.start(5)

        # shortcut
        self.setFocusPolicy(Qt.StrongFocus)

        self.shortcut_space = QShortcut(QKeySequence(Qt.Key_Space), self)
        self.shortcut_space.setContext(Qt.ApplicationShortcut)
        self.shortcut_space.activated.connect(self.trigger_step)

        self.shortcut_s = QShortcut(QKeySequence(Qt.Key_S), self)
        self.shortcut_s.setContext(Qt.ApplicationShortcut)
        self.shortcut_s.activated.connect(self.on_start_recording)

        self.shortcut_e = QShortcut(QKeySequence(Qt.Key_E), self)
        self.shortcut_e.setContext(Qt.ApplicationShortcut)
        self.shortcut_e.activated.connect(self.on_toggle_evaluation)

        self.shortcut_q = QShortcut(QKeySequence(Qt.Key_Q), self)
        self.shortcut_q.setContext(Qt.ApplicationShortcut)
        self.shortcut_q.activated.connect(self.cleanup_and_close)

    # ---------- Helpers ----------
    def status_tcp(self, msg):
        self.tcp_status.setText(f"TCP: {msg}")

    def repaint_canvas(self):
        with self.state.lock:
            gx_f, gy_f = self.state.latest_gaze_filtered
        self.canvas.draw_frame((gx_f, gy_f) if gx_f is not None else None)

    def ensure_dirs(self):
        p_num = self.p_spin.value()
        participant_id = f"t{p_num:02d}"
        self.participant_dir = participant_id
        self.nine_dir = os.path.join(self.participant_dir, "nine_dot_calibration")
        os.makedirs(self.nine_dir, exist_ok=True)
    
    def stream_gaze_visual(self, ts: float, x_f: float, y_f: float, x_raw: float, y_raw: float):
        with self._gv_lock:
            self._gv_latest_raw = (float(ts), float(x_f), float(y_f))

    def _tick_send_gaze(self):
        if not self.tracking_active: return
        chk = getattr(self, "visualize", None)
        if not (chk and chk.isChecked()): return
        if not (self.tcp_thread and self.tcp_thread.conn): return
        if "ridge_biquadratic" not in self.models: return

        with self._gv_lock:
            latest = self._gv_latest_raw
        if latest is None:
            return

        ts, x_f, y_f = latest

        neon_xy_f = np.array([[float(x_f), float(y_f)]], dtype=float)
        neon_xy_n = normalize_neon_xy(neon_xy_f, CANVAS_W, CANVAS_H)
        xy = predict_ridge_biquad(self.models["ridge_biquadratic"], neon_xy_n)[0]
        self.tcp_thread.send_gaze_visual(ts, float(xy[0]), float(xy[1]))


    # ---------- Neon ----------
    def on_connect_neon(self):
        try:
            self.ensure_dirs()
            self.neon_status.setText("Neon: ")
            device = discover_one_device(max_search_duration_seconds=10)
            if device is None:
                self.neon_status.setText("Neon: ❌")
                QMessageBox.warning(self, "Neon", "No device found.")
                return
            self.device = device
            self.neon_status.setText("Neon: ✅")

            self.state.running = True
            lp = ButterLPFilter(fs=FS, fc=FC, order=LP_ORDER)
            self.gaze_thread = GazeCollector(self.device, self.state, lp_filter=lp)
            self.gaze_thread.on_new_filtered = self.stream_gaze_visual
            self.gaze_thread.start()

        except Exception as e:
            self.neon_status.setText(f"Neon Error: {e}")
            QMessageBox.critical(self, "Neon Error", str(e))

    # ---------- REC ----------
    def on_start_recording(self):
        if self.state.recording:
            QMessageBox.information(self, "Recording", "Already recording.")
            return
        self.ensure_dirs()
        self.state.step = 0
        self.state.ended = False
        self.state.recording = True
        self.step_label.setText("Step: 0")

    # ---------- TCP ----------
    def on_start_tcp(self):
        if self.tcp_thread and self.tcp_thread.conn:
            self.status_tcp("already connected")
            return
        self.tcp_thread = TcpServer(self.status_tcp)
        self.tcp_thread.start()

    # ---------- Evaluation ----------
    def start_evaluation_logging(self):
        self.ensure_dirs()
        self.eval_dir = os.path.join(self.participant_dir, "evaluation")
        os.makedirs(self.eval_dir, exist_ok=True)
        self.eval_log_path = os.path.join(self.eval_dir, "evaluation_log.jsonl")
        self.state.start_evaluation(log_path=self.eval_log_path, models=self.models, canvas_w=CANVAS_W, canvas_h=CANVAS_H)
    
    def stop_evaluation_logging(self):
        self.state.stop_evaluation()

    def on_toggle_evaluation(self):
        if not self.device or not self.gaze_thread:
            QMessageBox.information(self, "Info", "Connect Noen")
            return
        if not (self.tcp_thread and self.tcp_thread.conn):
            QMessageBox.information(self, "Info", "Start TCP server")
            return
    
        if not self.eval_active:
            self.load_models()
            self.start_evaluation_logging()
            plan_path = self.build_and_save_random_saccade_plan()
            self.plan_streamer = EvalPlanStreamer(self.tcp_thread, plan_path, tick_hz=EVAL_RATE_HZ)
            self.plan_streamer.start()
            self.eval_active = True
            self.btn_eval.setText("Stop")
        else:
            self.stop_evaluation_logging()
            if self.plan_streamer:
                try: self.plan_streamer.stop()
                except Exception: pass
                self.plan_streamer = None
            self.eval_active = False
            self.btn_eval.setText("Start Evaluation")
    
    def start_gaze_logging(self):
        self.ensure_dirs()
        self.gaze_tracking_dir = os.path.join(self.participant_dir, "gaze_tracking")
        os.makedirs(self.gaze_tracking_dir, exist_ok=True)
        self.gaze_log_path = os.path.join(self.gaze_tracking_dir, "gaze_log.jsonl")
        self.state.start_tracking(log_path=self.gaze_log_path, models=self.models, canvas_w=CANVAS_W, canvas_h=CANVAS_H)
    
    def stop_gaze_logging(self):
        self.state.stop_tracking()

    def on_toggle_gaze_tracking(self):
        if not self.device or not self.gaze_thread:
            QMessageBox.information(self, "Info", "Connect Neon")
            return
        if not (self.tcp_thread and self.tcp_thread.conn):
            QMessageBox.information(self, "Info", "Start TCP server")
            return
        
        if not self.tracking_active:
            self.load_models()
            self.start_gaze_logging()
            self.tracking_active = True
            self.btn_track.setText("Stop")

            interval_ms = max(1, int(round(1000.0 / float(self.gv_rate_hz))))
            self.gaze_tx_timer.start(interval_ms)
        else:
            self.stop_gaze_logging()
            self.tracking_active = False
            self.btn_track.setText("Start Gaze Tracking")

            self.gaze_tx_timer.stop()
            with self._gv_lock:
                self._gv_latest_raw = None

    def load_models(self):
        model_dir = os.path.join(self.participant_dir, "models")
        if not os.path.isdir(model_dir):
            QMessageBox.warning(self, "Models", f"Models folder not found: {model_dir}")
            return False
        self.models = load_models(model_dir)
        if not self.models:
            QMessageBox.warning(self, "Models", "No valid model JSON found in Models directory.")
            return False
        return True
    
    # --------- Random Saccade Function ---------
    def _deg_to_m(self, deg_x: float, deg_y: float, distance_m: float=1.0):
        rad_x = np.deg2rad(deg_x)
        rad_y = np.deg2rad(deg_y)
        x = distance_m * np.tan(rad_x)
        y = distance_m * np.tan(rad_y)
        return float(x), float(y)

    def _sample_random_deg(self, prev_deg: Optional[tuple[float, float]]=None):
        hx0, hx1 = H_RANGE_DEG
        vy0, vy1 = V_RANGE_DEG
        for _ in range(1000):
            dx = np.random.uniform(hx0, hx1)
            dy = np.random.uniform(vy0, vy1)
            if prev_deg is None:
                return float(dx), float(dy)
            dist = np.hypot(dx - prev_deg[0], dy - prev_deg[1])
            if dist >= MIN_SACCADE_AMP_DEG:
                return float(dx), float(dy)
        return float(dx), float(dy)
    
    def build_and_save_random_saccade_plan(self):
        self.ensure_dirs()

        frames_total = int(EVAL_DRUATION_S * EVAL_RATE_HZ)
        frames_per_target = max(1, int(round(EVAL_DWELL_MS * EVAL_RATE_HZ / 1000)))

        timeline = []
        t_ms = 0
        frame_interval_ms = int(round(1000 / EVAL_RATE_HZ))

        prev_deg = None
        ti = 0
        block_idx = 0

        while ti < frames_total:
            if block_idx == 0: deg_x, deg_y = 0.0, 0.0
            elif ti + frames_per_target >= frames_total: deg_x, deg_y = 0.0, 0.0
            else: deg_x, deg_y = self._sample_random_deg(prev_deg)

            x_m, y_m = self._deg_to_m(deg_x, deg_y, distance_m=1.0)

            for _ in range(frames_per_target):
                if ti >= frames_total:
                    break
                timeline.append({
                    "t_ms": int(t_ms),
                    "pos": {"x": float(x_m), "y": float(y_m)}
                })
                ti += 1
                t_ms += frame_interval_ms

            prev_deg = (deg_x, deg_y)
            block_idx += 1

        plan = {
            "meta": {
                "rate_hz": EVAL_RATE_HZ,
                "dwell_ms": EVAL_DWELL_MS,
                "duration_s": EVAL_DRUATION_S,
                "frames_total": frames_total,
            },
            "timeline": timeline
        }

        self.eval_dir = os.path.join(self.participant_dir, "evaluation")
        os.makedirs(self.eval_dir, exist_ok=True)
        self.plan_path = os.path.join(self.eval_dir, "random_saccade_plan.json")
        with open(self.plan_path, "w", encoding="utf-8") as f:
            json.dump(plan, f, ensure_ascii=False, indent=2)
        print(f"[EVAL] saved: {self.plan_path}")
        return self.plan_path

    # ---------- Step / End ----------
    def trigger_step(self):
        if self.state.ended:
            return
        if not self.state.recording:
            QMessageBox.information(self, "Info", "Press 'S' to start recording first.")
            return
        
        self.state.set_event()
        if self.state.step < 25:
            self.state.step += 1
            self.step_label.setText(f"Step: {self.state.step}")
            if self.tcp_thread:
                self.tcp_thread.send_step(self.state.step)
            if self.state.step == 25:
                if self.tcp_thread:
                    self.tcp_thread.send_end_signal()
                self.state.ended = True
                self.save_csv()
                self.state.recording = False

    # ---------- Save ----------
    def save_csv(self):
        if not self.nine_dir:
            self.ensure_dirs()
        
        gaze_rows = self.state.snapshot_gaze_log()
        if gaze_rows:
            df = pd.DataFrame(
                gaze_rows,
                columns=["timestamp", "gaze_x", "gaze_y", "raw_gaze_x", "raw_gaze_y", "event_log"]
            )
            last = df.index[-1]
            df.at[last, "event_log"] = "event_log"
            gaze_csv = os.path.join(self.nine_dir, "calibration_gaze_log.csv")
            df.to_csv(gaze_csv, index=False)
        else:
            gaze_csv = None

        pairs_csv = self.build_mapping(gaze_csv) if gaze_csv else None
        if pairs_csv:
            self.build_and_save_models(pairs_csv)
        
        datum_path = os.path.join(self.nine_dir, "raw_neon_data.jsonl")
        with self.state.lock:
            lines = list(self.state.datum_lines)
        if lines:
            with open(datum_path, "wt", encoding="utf-8") as f:
                for line in lines:
                    f.write(line+"\n")

    # ---------- Mapping ----------
    def build_mapping(self, gaze_csv_path: str, window: int=WINDOW_WIDTH):
        if not os.path.exists(gaze_csv_path):
            return None
        df = pd.read_csv(gaze_csv_path)
        ev = df["event_log"].astype(str).fillna("")
        event_idxs = list(np.flatnonzero(ev == "event_log"))[:25]
        if not event_idxs:
            return None
        
        rows = []
        for seq_idx, i in enumerate(event_idxs):
            start = max(0, i - window)
            seg = df.iloc[start:i]
            if len(seg) == 0:
                seg = df.iloc[i:i+1]
            avg_gx, avg_gy = float(seg["gaze_x"].mean()), float(seg["gaze_y"].mean())
            target_x, target_y = CALIB_TARGET_COORDINATES[seq_idx]
            rows.append({
                "step": seq_idx + 1,
                "target_x": target_x,
                "target_y": target_y,
                "avg_gaze_x": avg_gx,
                "avg_gaze_y": avg_gy
            })
        out_df = pd.DataFrame(rows)
        out_path = os.path.join(self.nine_dir, "calibration_pair.csv")
        out_df.to_csv(out_path, index=False)
        return out_path
    
    def build_and_save_models(self, pair_csv_path: str):
        df = pd.read_csv(pair_csv_path)
        neon_xy_raw = df[["avg_gaze_x", "avg_gaze_y"]].to_numpy(float)
        vp_xy = df[["target_x", "target_y"]].to_numpy(float)

        neon_xy = normalize_neon_xy(neon_xy_raw, CANVAS_W, CANVAS_H)
        canvas_size = (CANVAS_W, CANVAS_H)

        biquad_model = map_biquadratic(neon_xy, vp_xy, canvas_size)
        ridge_model = map_ridge_biquadratic(neon_xy, vp_xy, canvas_size, alpha=RIDGE_ALPHA)

        model_path = os.path.join(self.participant_dir, "models")
        save_models(model_path, biquad_model, ridge_model)
        
        return model_path

    # ---------- Cleanup ----------
    def cleanup_and_close(self):
        self.state.running = False
        time.sleep(0.1)
        try:
            if self.device:
                self.device.streaming_stop("gaze")
                self.device.close()
        except Exception: pass
        try:
            if self.tcp_thread:
                self.tcp_thread.close()
        except Exception: pass
        self.close()

# ============ main ============
def main():
    app = QApplication(sys.argv)
    w = MainWindow()
    w.resize(800, 500)
    w.show()
    sys.exit(app.exec())

if __name__ == "__main__":
    main()
