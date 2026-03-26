using UnityEngine;

public class DotGridCalibrator : MonoBehaviour
{
    [Header("XR / Camera")]
    public Camera xrCamera;
    [Tooltip("if null, xrCamera.transform")]
    public Transform referenceForward;

    [Header("Dot Visual")]
    public GameObject dotPrefab;
    public float dotRadius = 0.01f;
    public float viewDistM = 1.0f;

    [Header("Grid (deg) for calibration mode")]
    public float hMin = -15f, hMax = 15f;
    public float vMin = -10f, vMax = 10f;
    public int cols = 5, rows = 5;

    GameObject _dot;
    Vector2[,] _angles;
    int _total;

    enum Mode { None, StepAngles, Meters }
    Mode _mode = Mode.None;

    float _curH, _curV;      // deg
    float _curX_m, _curY_m;  // meters on z = viewDistM plane

    void Awake()
    {
        if (xrCamera == null) xrCamera = Camera.main;
        BuildAngles();
        CreateDot();
        HideDot();
    }

    void LateUpdate()
    {
        if (_mode == Mode.None || _dot == null) return;

        var refTf = referenceForward ? referenceForward : (xrCamera ? xrCamera.transform : null);
        if (refTf == null) return;

        Vector3 local;
        if (_mode == Mode.StepAngles)
        {
            float x = viewDistM * Mathf.Tan(_curH * Mathf.Deg2Rad);
            float y = viewDistM * Mathf.Tan(_curV * Mathf.Deg2Rad);
            local = new Vector3(x, y, viewDistM);
        }
        else // Mode.Meters
        {
            float scale = viewDistM / 1.0f;
            local = new Vector3(_curX_m * scale, _curY_m * scale, viewDistM);
        }

        _dot.transform.position = refTf.TransformPoint(local);
        _dot.transform.rotation = Quaternion.LookRotation(refTf.forward, refTf.up);
    }

    void BuildAngles()
    {
        _angles = new Vector2[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            float v = Mathf.Lerp(vMax, vMin, rows == 1 ? 0f : (float)r / (rows - 1));
            for (int c = 0; c < cols; c++)
            {
                float h = Mathf.Lerp(hMin, hMax, cols == 1 ? 0f : (float)c / (cols - 1));
                _angles[r, c] = new Vector2(h, v);
            }
        }
        _total = rows * cols;
    }

    void CreateDot()
    {
        if (_dot != null) Destroy(_dot);
        _dot = Instantiate(dotPrefab);
        _dot.transform.localScale = Vector3.one * (dotRadius * 2f);
        _dot.SetActive(false);
    }

    public void ShowStep(int step)
    {
        if (step < 0 || step >= _total) { HideDot(); return; }
        int r = step / cols;
        int c = step % cols;
        var ang = _angles[r, c];
        _curH = ang.x;
        _curV = ang.y;
        _mode = Mode.StepAngles;
        if (!_dot.activeSelf) _dot.SetActive(true);
        LateUpdate();
    }

    public void ShowByAngles(float hDeg, float vDeg)
    {
        _curH = hDeg;
        _curV = vDeg;
        _mode = Mode.StepAngles;
        if (!_dot.activeSelf) _dot.SetActive(true);
        LateUpdate();
    }

    public void ShowByMeters(float x_m, float y_m)
    {
        _curX_m = x_m;
        _curY_m = y_m;
        _mode = Mode.Meters;
        if (!_dot.activeSelf) _dot.SetActive(true);
        LateUpdate();
    }

    public void HideDot()
    {
        _mode = Mode.None;
        if (_dot != null) _dot.SetActive(false);
    }
}
