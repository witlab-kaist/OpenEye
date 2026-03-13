from __future__ import annotations

from dataclasses import dataclass
from typing import Tuple
from math import sqrt

@dataclass(frozen=True)
class FilterConfig:
    """Low-pass filter parameters"""
    fs_hz: int = 200            # Sampling rate of gaze data (Hz)
    fc_hz: float = 15.0         # Cutoff frequency of low-pass filter (Hz)
    order: int = 2              # Butterworth filter order


@dataclass(frozen=True)
class MappingConfig:
    """Mapping function parameters"""
    ridge_alpha: float = 0.01   # Regularization strength for ridge biquadratic mapping
    window_width: int = 100     # Number of samples averaged before each event (calibration)


@dataclass(frozen=True)
class CanvasConfig:
    """Canvas size for gaze visulize"""
    width_px: int = 1600        # Canvas width for gaze visualize
    height_px: int = 1200       # Canvas height for gaze visualize


@dataclass(frozen=True)
class EvalConfig:
    """Random saccade evaluation task parameters"""
    rate_hz: int = 50           # Sampling rate for evaluation
    duration_s: int = 30        # Duration for evaluation task
    dwell_ms: int = 1000        # Duration for each dwell

    h_range_deg: Tuple[float, float] = (-15.0, 15.0)
    v_range_deg: Tuple[float, float] = (-10.0, 10.0)
    view_distance_m: float = 3.96
    view_diagonal_m: float = 3.91
    ratio_w: int = 16
    ratio_h: int = 9
    calibration_width: int = 1920
    calibration_height: int = 1080
    diagonal_norm: float = sqrt(ratio_w ** 2 + ratio_h ** 2)
    display_width_m: float = view_diagonal_m * (ratio_w / diagonal_norm)
    display_height_m: float = view_diagonal_m * (ratio_h / diagonal_norm)
    min_saccade_amp_deg: float = 3.0


@dataclass(frozen=True)
class AppConfig:
    canvas: CanvasConfig = CanvasConfig()
    filt: FilterConfig = FilterConfig()
    mapping: MappingConfig = MappingConfig()
    eval: EvalConfig = EvalConfig()


DEFAULT_CONFIG = AppConfig()