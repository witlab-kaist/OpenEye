from __future__ import annotations
import json
import os
import numpy as np

from typing import Dict, Tuple
from scipy.interpolate import Rbf

def normalize_neon_xy(xy_np: np.ndarray, width: int, height: int) -> np.ndarray:
    out = xy_np.astype(float).copy()
    out[:, 0] = (xy_np[:, 0] / float(width)) * 2.0 - 1.0
    out[:, 1] = (xy_np[:, 1] / float(height)) * 2.0 - 1.0
    return out

# 1) Biquadratic
def _biquad_features(xy: np.ndarray) -> np.ndarray:
    x = xy[:, 0]; y = xy[:, 1]
    return np.stack([x, y, x*x, y*y, x*y, np.ones_like(x)], axis=1)

def map_biquadratic(neon_xy_n: np.ndarray, vp_xy: np.ndarray, canvas_size: Tuple[int,int]) -> Dict:
    F = _biquad_features(neon_xy_n)
    Wx, *_ = np.linalg.lstsq(F, vp_xy[:, 0], rcond=None)
    Wy, *_ = np.linalg.lstsq(F, vp_xy[:, 1], rcond=None)
    W = np.vstack([Wx, Wy])
    w, h = canvas_size
    return {"type": "biquadratic", "W": W.tolist(), "gaze_w": int(w), "gaze_h": int(h)}

def predict_biquad(model: Dict, neon_xy_n: np.ndarray) -> np.ndarray:
    W = np.array(model["W"], dtype=float)
    F = _biquad_features(neon_xy_n)
    return (W @ F.T).T

# 2) Ridge Biquadratic
def _ridge_regression(X: np.ndarray, y: np.ndarray, alpha: float) -> np.ndarray:
    n_features = X.shape[1]
    I = np.eye(n_features)
    return np.linalg.inv(X.T @ X + alpha * I) @ X.T @ y

def map_ridge_biquadratic(neon_xy_n: np.ndarray, vp_xy: np.ndarray, canvas_size: Tuple[int, int], alpha: float) -> Dict:
    F = _biquad_features(neon_xy_n)
    Wx = _ridge_regression(F, vp_xy[:, 0], alpha)
    Wy = _ridge_regression(F, vp_xy[:, 1], alpha)
    W = np.vstack([Wx, Wy])
    w, h = canvas_size
    return {"type": "ridge_biquadratic", "W": W.tolist(), "gaze_w": int(w), "gaze_h": int(h), "alpha": alpha}

def predict_ridge_biquad(model: Dict, neon_xy_n: np.ndarray) -> np.ndarray:
    W = np.array(model["W"], dtype=float)
    F = _biquad_features(neon_xy_n)
    return (W @ F.T).T

# ====== I/O helpers ======

def save_models(model_dir: str, biquad: Dict | None, ridge: Dict | None) -> None:
    os.makedirs(model_dir, exist_ok=True)
    if biquad:
        with open(os.path.join(model_dir, "model_biquadratic.json"), "w", encoding="utf-8") as f:
            json.dump(biquad, f, ensure_ascii=False, indent=2)
    if ridge:
        with open(os.path.join(model_dir, "model_ridge_biquadratic.json"), "w", encoding="utf-8") as f:
            json.dump(ridge, f, ensure_ascii=False, indent=2)

def load_models(model_dir: str) -> Dict[str, Dict]:
    models: Dict[str, Dict] = {}
    # biquadratic
    p_biq = os.path.join(model_dir, "model_biquadratic.json")
    if os.path.isfile(p_biq):
        with open(p_biq, "r", encoding="utf-8") as f:
            models["biquadratic"] = json.load(f)
    # ridge biquadratic
    p_rid = os.path.join(model_dir, "model_ridge_biquadratic.json")
    if os.path.isfile(p_rid):
        with open(p_rid, "r", encoding="utf-8") as f:
            models["ridge_biquadratic"] = json.load(f)
    return models
