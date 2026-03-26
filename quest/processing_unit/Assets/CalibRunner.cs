using UnityEngine;

public class CalibRunner : MonoBehaviour
{
    public TCPClient tcp;
    public DotGridCalibrator grid;

    [Header("Step Range")]
    [SerializeField] int minStep = 0;
    [SerializeField] int maxStep = 24;

    [Header("Eval/Gaze Option")]
    [SerializeField] bool requireCalibEndForEval = true;
    [SerializeField] bool requireCalibEndForGaze = true;

    [SerializeField] bool evalAutoHide = true;
    [SerializeField] float evalHideAfterSec = 1.0f;

    [Header("Gaze Period")]
    [SerializeField] float gazeDisplayHz = 60f;
    [SerializeField] bool gazeSmoothLerp = false;
    [SerializeField, Range(0f,1f)] float gazeLerpFactor = 0.4f;

    bool evalMode = false;
    Coroutine _hideCo;

    long _lastSeenGazeSeq = 0;     // TCPClient.TryGetLatestGaze
    float _nextGazeDrawTime = 0f;
    Vector2 _currentShownGaze_m;

    void OnEnable()
    {
        if (tcp == null) tcp = FindObjectOfType<TCPClient>();
        if (grid == null) grid = FindObjectOfType<DotGridCalibrator>();

        if (tcp != null)
        {
            tcp.OnUpdateStep    += HandleStep;
            tcp.OnCalibrationEnd += HandleEnd;
            tcp.OnEvalTarget    += HandleEvalTarget;
            tcp.OnStateChanged  += HandleState;
        }
    }

    void OnDisable()
    {
        if (tcp != null)
        {
            tcp.OnUpdateStep    -= HandleStep;
            tcp.OnCalibrationEnd -= HandleEnd;
            tcp.OnEvalTarget    -= HandleEvalTarget;
            tcp.OnStateChanged  -= HandleState;
        }
    }

    void HandleState(TCPClient.State s)
    {
        if (s == TCPClient.State.Connected)
        {
            evalMode = false;
            grid.ShowStep(0);
            _lastSeenGazeSeq = 0;
        }
        else if (s == TCPClient.State.Disconnected || s == TCPClient.State.Failed)
        {
            grid?.HideDot();
            _lastSeenGazeSeq = 0;
        }
    }

    void HandleStep(int step)
    {
        if (evalMode || grid == null) return;
        if (step < minStep || step > maxStep) { grid.HideDot(); return; }
        grid.ShowStep(step);
    }

    void HandleEnd()
    {
        evalMode = true;
        grid?.HideDot();
    }

    void HandleEvalTarget(int idx, int t_ms, Vector2 pos_m)
    {
        if ((requireCalibEndForEval && !evalMode) || grid == null) return;
        grid.ShowByMeters(pos_m.x, pos_m.y);
        if (evalAutoHide)
        {
            if (_hideCo != null) StopCoroutine(_hideCo);
            _hideCo = StartCoroutine(_HideLater());
        }
    }

    System.Collections.IEnumerator _HideLater()
    {
        yield return new WaitForSeconds(evalHideAfterSec);
        grid.HideDot();
    }

    void Update()
    {
        if (requireCalibEndForGaze && !evalMode) return;
        if (tcp == null || grid == null) return;

        float interval = (gazeDisplayHz > 0f) ? (1f / gazeDisplayHz) : 0f;
        float now = Time.unscaledTime;
        if (interval > 0f && now < _nextGazeDrawTime) return;
        _nextGazeDrawTime = now + interval;

        if (tcp.TryGetLatestGaze(ref _lastSeenGazeSeq, out var gaze))
        {
            var target = gaze.p; // meters on z=1m

            if (gazeSmoothLerp)
            {
                _currentShownGaze_m = Vector2.Lerp(_currentShownGaze_m, target, gazeLerpFactor);
                grid.ShowByMeters(_currentShownGaze_m.x, _currentShownGaze_m.y);
            }
            else
            {
                _currentShownGaze_m = target;
                grid.ShowByMeters(target.x, target.y);
            }
        }
    }
}
