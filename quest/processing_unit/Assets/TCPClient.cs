using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

[Serializable] public class PayloadStep { public int step; }
[Serializable] public class Pos { public float x; public float y; } // meters on z=1m plane
[Serializable] public class PayloadEval { public int idx; public int t_ms; public Pos pos; }
[Serializable] public class PayloadGaze { public double t; public float x; public float y; }

[Serializable] class MsgTypeOnly { public string type; }
[Serializable] class MsgUpdateStep { public string type; public PayloadStep payload; }
[Serializable] class MsgEvalTarget { public string type; public PayloadEval payload; }
[Serializable] class MsgGaze { public string type; public PayloadGaze payload; }

public class TCPClient : MonoBehaviour
{
    public static TCPClient Instance;

    [Header("Server")]
    public string serverIp = "XXX.XXX.XXX.XXX"; // Enter your IP address
    public int serverPort = 5051;

    [Header("Auto Connect / Reconnect")]
    [SerializeField] bool autoConnectOnStart = true;
    [SerializeField] bool autoReconnect = true;
    [SerializeField, Tooltip("Reconnect period")] float reconnectIntervalSec = 2.0f;

    [Header("Logging")]
    [SerializeField, Tooltip("Console Logging Period")] int logEveryN = 10;

    public enum State { Disconnected, Connecting, Connected, Failed }
    public State CurrentState { get; private set; } = State.Disconnected;

    TcpClient _client;
    NetworkStream _stream;
    Thread _recvThread;
    CancellationTokenSource _cts;

    public event Action<int> OnUpdateStep;                 // step
    public event Action OnCalibrationEnd;                  // calib end
    public event Action<int,int,Vector2> OnEvalTarget;     // (idx, t_ms, (x_m, y_m))
    public event Action<double,Vector2> OnGazeVisual;
    public event Action<State> OnStateChanged;

    volatile int _pendingStep = -1;
    volatile bool _pendingCalibEnd = false;

    struct EvalMsg { public int idx, tms; public Vector2 p_m; }
    volatile bool _hasLatestEval = false;
    EvalMsg _latestEval;

    public struct GazeMsg { public double t; public Vector2 p; }

    public string nextSceneName = "PL_Calibration_OpenCV";

    public volatile float latestGazeX;
    public volatile float latestGazeY;
    public volatile float GazeTimestamp;

    const int GAZE_BUF_CAP = 10;
    readonly object _gazeLock = new object();
    GazeMsg[] _gazeBuf = new GazeMsg[GAZE_BUF_CAP];
    int _gazeHead = 0;
    long _gazeSeq = 0;

    int _logCounter;

    void Awake()
    {   
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (autoConnectOnStart)
            _ = StartConnectLoop();
            
        
    }
    void OnDestroy()
    {
        CloseConnectionSafely();
    }

    private void CloseConnectionSafely()
    {
        try {
            _recvThread?.Interrupt();
            _recvThread?.Join(100);
        } catch {}

        try { _stream?.Close(); } catch {}
        try { _client?.Close(); } catch {}
    }
    async Task StartConnectLoop()
    {
        while (autoReconnect && Application.isPlaying)
        {
            if (CurrentState == State.Disconnected || CurrentState == State.Failed)
                Connect();

            try { await Task.Delay(TimeSpan.FromSeconds(reconnectIntervalSec)); }
            catch {}
        }
    }

    public void Connect()
    {
        if (CurrentState == State.Connecting || CurrentState == State.Connected) return;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _ = ConnectAsync(_cts.Token);
    }

    public void Disconnect()
    {
        try { _cts?.Cancel(); _stream?.Close(); _client?.Close(); } catch { }
        SetState(State.Disconnected);
    }

    async Task ConnectAsync(CancellationToken token)
    {
        SetState(State.Connecting);
        try
        {
            _client = new TcpClient
            {
                NoDelay = true,
                ReceiveBufferSize = 1 << 20,
                SendBufferSize    = 1 << 20
            };

            var connectTask = _client.ConnectAsync(serverIp, serverPort);
            using (token.Register(() => { try { _client?.Close(); } catch { } }))
            {
                await connectTask;
            }

            if (!_client.Connected) throw new Exception("connect failed");

            _stream = _client.GetStream();
            SetState(State.Connected);

            _ = ReceiveLoop(token);
        }
        catch (Exception e)
        {
            Debug.LogError($"[TCP] connect error: {e.Message}");
            SetState(State.Failed);
        }
    }

    async Task ReceiveLoop(CancellationToken token)
    {
        var headerBuf = new byte[4];

        try
        {
            while (!token.IsCancellationRequested)
            {
                await ReadExactAsync(_stream, headerBuf, 0, 4, token);
                int len = (headerBuf[0] << 24) | (headerBuf[1] << 16) | (headerBuf[2] << 8) | headerBuf[3];
                if (len <= 0 || len > 10_000_000) throw new Exception($"invalid length: {len}");

                // payload
                byte[] payload = new byte[len];
                await ReadExactAsync(_stream, payload, 0, len, token);
                string json = Encoding.UTF8.GetString(payload);

                try
                {
                    var head = JsonUtility.FromJson<MsgTypeOnly>(json);
                    if (head == null || string.IsNullOrEmpty(head.type))
                    {
                        if ((_logCounter++ % logEveryN) == 0)
                            Debug.LogWarning($"[TCP] unknown json: {json}");
                        continue;
                    }

                    switch (head.type)
                    {
                        case "updateStep":
                        {
                            var msg = JsonUtility.FromJson<MsgUpdateStep>(json);
                            if (msg?.payload != null)
                            {
                                _pendingStep = msg.payload.step;
                            }
                            break;
                        }

                        case "calibrationEnd":
                        {
                            _pendingCalibEnd = true;
                            SceneManager.LoadScene(nextSceneName);
                            break;
                        }

                        case "evalTarget":
                        {
                            var msg = JsonUtility.FromJson<MsgEvalTarget>(json);
                            if (msg?.payload != null && msg.payload.pos != null)
                            {
                                _latestEval = new EvalMsg {
                                    idx = msg.payload.idx,
                                    tms = msg.payload.t_ms,
                                    p_m = new Vector2(msg.payload.pos.x, msg.payload.pos.y) // meters at z=1m
                                };
                                _hasLatestEval = true;
                            }
                            break;
                        }

                        case "gazeVisual":
                        {
                            var msg = JsonUtility.FromJson<MsgGaze>(json);
                            if (msg?.payload != null)
                            {
                                    EnqueueGaze(new GazeMsg
                                    {
                                        t = msg.payload.t,
                                        p = new Vector2(msg.payload.x, msg.payload.y)
                                    });

                                    latestGazeX = msg.payload.x;
                                    latestGazeY = msg.payload.y;

                                // if ((_logCounter++ % logEveryN) == 0)
                                //     Debug.Log($"[TCP] gaze enq t={msg.payload.t:F3}, x={msg.payload.x:F3}, y={msg.payload.y:F3}");
                            }
                            break;
                        }

                        default:
                            if ((_logCounter++ % logEveryN) == 0)
                                Debug.Log($"[TCP] unknown type: {head.type}");
                            break;
                    }
                }
                catch (Exception pe)
                {
                    Debug.LogWarning($"[TCP] parse error: {pe.Message}\nJSON={json}");
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[TCP] recv loop end: {e.Message}");
        }

        Disconnect();
    }

    static async Task ReadExactAsync(NetworkStream s, byte[] buf, int off, int len, CancellationToken token)
    {
        int got = 0;
        while (got < len)
        {
            int r = await s.ReadAsync(buf, off + got, len - got, token);
            if (r <= 0) throw new Exception("socket closed");
            got += r;
        }
    }

    void SetState(State s)
    {
        CurrentState = s;
        Debug.Log($"[TCP] state = {s}");
        OnStateChanged?.Invoke(s);
    }

    void OnApplicationQuit() => Disconnect();

    void EnqueueGaze(GazeMsg m)
    {
        lock (_gazeLock)
        {
            _gazeBuf[_gazeHead] = m;
            _gazeHead = (_gazeHead + 1) % GAZE_BUF_CAP;
            _gazeSeq++;
        }
    }

    public bool TryGetLatestGaze(ref long lastSeenSeq, out GazeMsg latest)
    {
        lock (_gazeLock)
        {
            if (_gazeSeq == lastSeenSeq)
            {
                latest = default;
                return false;
            }
            int latestIdx = (_gazeHead - 1 + GAZE_BUF_CAP) % GAZE_BUF_CAP;
            latest = _gazeBuf[latestIdx];
            lastSeenSeq = _gazeSeq;
            return true;
        }
    }

    void OnReceiveGazeVisual(float x, float y, float t)
    {
        latestGazeX = x;
        latestGazeY = y;
        GazeTimestamp = t;
    }

    void Update()
    {
        // 1) calibrationEnd
        if (_pendingCalibEnd)
        {
            _pendingCalibEnd = false;
            OnCalibrationEnd?.Invoke();
        }

        // 2) updateStep
        if (_pendingStep >= 0)
        {
            int step = _pendingStep;
            _pendingStep = -1;
            OnUpdateStep?.Invoke(step);
        }

        // 3) evalTarget
        if (_hasLatestEval)
        {
            _hasLatestEval = false;
            var m = _latestEval;
            OnEvalTarget?.Invoke(m.idx, m.tms, m.p_m); // meters
        }

    }
}
