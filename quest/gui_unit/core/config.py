from __future__ import annotations

from dataclasses import dataclass
from typing import Tuple

@dataclass(frozen=True)
class FilterConfig:
    """Low-pass filter parameters"""
    fs_hz: int = 200          # Sampling rate of gaze data (Hz)
    fc_hz: float = 15.0       # Cutoff frequency of low-pass filter (Hz)
    order: int = 2            # Butterworth filter order


@dataclass(frozen=True)
class MappingConfig:
    """Mapping function parameters"""
    ridge_alpha: float = 0.01  # Regularization strength for ridge biquadratic mapping
    window_width: int = 100    # Number of samples averaged before each event (calibration)


@dataclass(frozen=True)
class CanvasConfig:
    """Canvas size for gaze visulize"""
    width_px: int = 1600
    height_px: int = 1200


@dataclass(frozen=True)
class EvalConfig:
    """Random saccade evaluation task parameters"""
    rate_hz: int = 5
    duration_s: int = 30
    dwell_ms: int = 1000

    h_range_deg: Tuple[float, float] = (-15.0, 15.0)
    v_range_deg: Tuple[float, float] = (-10.0, 10.0)
    view_distance_m: float = 1.0
    min_saccade_amp_deg: float = 3.0


@dataclass(frozen=True)
class AppConfig:
    canvas: CanvasConfig = CanvasConfig()
    filt: FilterConfig = FilterConfig()
    mapping: MappingConfig = MappingConfig()
    eval: EvalConfig = EvalConfig()


DEFAULT_CONFIG = AppConfig()