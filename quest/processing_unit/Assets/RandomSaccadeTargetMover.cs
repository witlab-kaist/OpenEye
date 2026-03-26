using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

public class RandomSaccadeTargetMover : MonoBehaviour
{
    [Header("Reference & Placement")]
    public Transform reference;
    public float distanceMeters = 1.0f;

    [Header("Timing")]
    public float dwellDurationSec = 1.0f;
    public float totalDurationSec = 30.0f;
    public bool playOnStart = true;

    [Header("Pre-roll Buffer")]
    public float bufferWaitSec = 5.0f;

    [Header("Angle Ranges (degrees)")]
    public float horizontalMinDeg = -15f;
    public float horizontalMaxDeg =  15f;
    public float verticalMinDeg   = -10f;
    public float verticalMaxDeg   =  10f;

    [Header("Constraints")]
    public float minStepAngularDiffDeg = 3f;

    [Header("Debug")]
    public int randomSeed = 0;
    public bool logSequence = true;

    // ---------------- CSV Logging ----------------
    [Header("CSV Logging")]
    public bool enableCsvLogging = true;
    public float sampleHz = 0f;
    private string _csvPath;
    private StreamWriter _csvWriter;

    private List<Vector2> _sequenceDeg;    // (hDeg, vDeg)
    private Coroutine _runner;

    void OnEnable()
    {
        if (enableCsvLogging) InitCsv();
    }

    void OnDisable()
    {
        CloseCsv();
    }

    void OnDestroy()
    {
        CloseCsv();
    }

    void Start()
    {
        if (!reference) reference = Camera.main ? Camera.main.transform : null;
        if (playOnStart)
            StartTask();
    }

    public void StartTask()
    {
        StopTask();
        BuildSequence();
        _runner = StartCoroutine(RunSequence());
    }

    public void StopTask()
    {
        if (_runner != null) StopCoroutine(_runner);
        _runner = null;
    }

    private void BuildSequence()
    {
        int steps = Mathf.Max(1, Mathf.RoundToInt(totalDurationSec / Mathf.Max(0.001f, dwellDurationSec)));

        _sequenceDeg = new List<Vector2>(steps);
        _sequenceDeg.Add(Vector2.zero);

        System.Random rng = (randomSeed == 0) ? new System.Random() : new System.Random(randomSeed);

        int middleCount = Mathf.Max(0, steps - 2);
        Vector2 prev = _sequenceDeg[0];

        for (int i = 0; i < middleCount; i++)
        {
            Vector2 cand;
            int guard = 0;
            do
            {
                cand = new Vector2(
                    RandRange(rng, horizontalMinDeg, horizontalMaxDeg),
                    RandRange(rng, verticalMinDeg, verticalMaxDeg)
                );
                guard++;
                if (guard > 2000) break;
            }
            while (AngularDistance(prev, cand) < minStepAngularDiffDeg);

            _sequenceDeg.Add(cand);
            prev = cand;
        }

        if (steps >= 2) _sequenceDeg.Add(Vector2.zero);

        if (logSequence)
        {
            Debug.Log($"[RandomSaccade] Steps={_sequenceDeg.Count}, dwell={dwellDurationSec}s, total≈{_sequenceDeg.Count * dwellDurationSec:F1}s");
            for (int i = 0; i < _sequenceDeg.Count; i++)
                Debug.Log($"Step {i+1}: h={_sequenceDeg[i].x:F2}°, v={_sequenceDeg[i].y:F2}°");
        }
    }

    private IEnumerator RunSequence()
    {
        if (reference == null)
        {
            yield break;
        }

        Vector3 preRollPos = GetWorldPositionFromAngles(Vector2.zero);
        transform.position = preRollPos;
        if (bufferWaitSec > 0f)
            yield return new WaitForSeconds(bufferWaitSec);

        for (int i = 0; i < _sequenceDeg.Count; i++)
        {
            Vector2 hv = _sequenceDeg[i];
            Vector3 worldPos = GetWorldPositionFromAngles(hv);
            transform.position = worldPos;

            WriteCsv(i, "step", hv, worldPos);

            if (sampleHz > 0f)
            {
                float dt = 1f / sampleHz;
                float elapsed = 0f;
                while (elapsed < dwellDurationSec)
                {
                    Vector3 curPos = transform.position;
                    WriteCsv(i, "sample", hv, curPos);
                    yield return new WaitForSeconds(dt);
                    elapsed += dt;
                }
            }
            else
            {
                yield return new WaitForSeconds(dwellDurationSec);
            }
        }
        _runner = null;
    }

    private Vector3 GetWorldPositionFromAngles(Vector2 hvDeg)
    {
        float x = distanceMeters * Mathf.Tan(hvDeg.x * Mathf.Deg2Rad);
        float y = distanceMeters * Mathf.Tan(hvDeg.y * Mathf.Deg2Rad);

        Vector3 origin = reference.position + reference.forward * distanceMeters;
        Vector3 worldPos = origin + reference.right * x + reference.up * y;
        return worldPos;
    }

    private static float AngularDistance(Vector2 aDeg, Vector2 bDeg)
    {
        float dh = bDeg.x - aDeg.x;
        float dv = bDeg.y - aDeg.y;
        return Mathf.Sqrt(dh * dh + dv * dv);
    }

    private static float RandRange(System.Random rng, float min, float max)
    {
        return (float)(min + (max - min) * rng.NextDouble());
    }

    void OnDrawGizmosSelected()
    {
        if (!reference) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(reference.position, reference.position + reference.forward * distanceMeters);
        Gizmos.DrawWireSphere(reference.position + reference.forward * distanceMeters, 0.01f);
    }

    // ---------------- CSV helpers ----------------
    private void InitCsv()
    {
        if (!enableCsvLogging) return;
        try
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string baseDir = Path.Combine(desktop, "neonxr_eval");
            Directory.CreateDirectory(baseDir);

            string[] subDirs = Directory.GetDirectories(baseDir, "t??");
            int latestIdx = -1;

            foreach (string dir in subDirs)
            {
                string name = Path.GetFileName(dir);
                if (name.Length == 3 && name.StartsWith("t") && int.TryParse(name.Substring(1), out int num))
                {
                    if (num > latestIdx) latestIdx = num;
                }
            }

            if (latestIdx == -1)
                latestIdx = 0;

            string participantDir = Path.Combine(baseDir, $"t{latestIdx:00}");

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _csvPath = Path.Combine(participantDir, $"SaccadePoints_{stamp}.csv");

            _csvWriter = new StreamWriter(_csvPath, append: false);
            _csvWriter.AutoFlush = true;
            _csvWriter.WriteLine("unix_ms,step_idx,h_deg,v_deg");
            Debug.Log($"[RandomSaccade CSV] Logging to: {_csvPath}");
        }
        catch (Exception e)
        {
            _csvWriter = null;
            Debug.LogError($"[RandomSaccade CSV] Failed to init CSV: {e.Message}");
        }
    }

    private void CloseCsv()
    {
        if (_csvWriter != null)
        {
            _csvWriter.Flush();
            _csvWriter.Close();
            _csvWriter.Dispose();
            _csvWriter = null;
        }
    }

    private void WriteCsv(int stepIdx, string phase, Vector2 hvDeg, Vector3 worldPos)
    {
        if (!enableCsvLogging || _csvWriter == null) return;

        long unixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var inv = CultureInfo.InvariantCulture;

        string line = string.Format(inv,
            "{0},{1},{2:F6},{3:F6}",
            unixMs,            // 0
            stepIdx,           // 1
            hvDeg.x,           // 2 h_deg
            hvDeg.y            // 3 v_deg
        );

        _csvWriter.WriteLine(line);
    }
}
