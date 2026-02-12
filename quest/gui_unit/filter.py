from __future__ import annotations
from typing import Tuple
from scipy.signal import butter, lfilter, lfilter_zi

class ButterLPFilter:
    def __init__(self, fs: float, fc: float, order: int=4) -> None:
        self.b, self.a = butter(order, fc / (fs * 0.5), btype="low")
        self.zi_x = None
        self.zi_y = None

    def step(self, x: float, y: float) -> Tuple[float, float]:
        x = float(x); y = float(y)
        if self.zi_x is None:
            self.zi_x = lfilter_zi(self.b, self.a) * x
        if self.zi_y is None:
            self.zi_y = lfilter_zi(self.b, self.a) * y
        xf_arr, self.zi_x = lfilter(self.b, self.a, [x], zi=self.zi_x)
        yf_arr, self.zi_y = lfilter(self.b, self.a, [y], zi=self.zi_y)
        return float(xf_arr[-1]), float(yf_arr[-1])