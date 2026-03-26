using PupilLabs;
using UnityEngine;

public class NeonGazeInputManagerPlane : MonoBehaviour
{
    [Header("Refs")]
    public Camera hmdCamera;
    public TargetSpawner spawner;
    public NeonExperimentManager experimentManager;
    public RightHandPinchDetector pinchDetector;
    public GazeDataVisualizer gazeViz;

    [Header("Params")]
    public float planeDistMeters = 1.0f;
    public float verticalOffsetMeters = 0.0f;
    public float selectRadiusScale = 1.0f;
    public int maxFailedPinchBeforeSkip = 3;
    private int failedPinchCount = 0;
    private TargetBehavior currentTB;
    private float gazeStartTime;
    void Awake()
    {
        EnsureGazeViz();
    }
    void Update()
    {
        if (gazeViz == null)
        {
            EnsureGazeViz();
            if (gazeViz == null) return;
        }

        if (hmdCamera == null || spawner == null || experimentManager == null) return;
        if (spawner.spawnedTargets.Count == 0) return;

        Vector3 camPos = hmdCamera.transform.position;

        Vector3 forwardFlat = hmdCamera.transform.forward;
        forwardFlat.y = 0f;
        if (forwardFlat.sqrMagnitude < 1e-6f)
            forwardFlat = hmdCamera.transform.forward;
        forwardFlat = forwardFlat.normalized;

        Vector3 rightFlat = hmdCamera.transform.right;
        rightFlat.y = 0f;
        if (rightFlat.sqrMagnitude < 1e-6f)
            rightFlat = hmdCamera.transform.right;
        rightFlat = rightFlat.normalized;

        Vector3 upWorld = Vector3.up;

        Vector3 planeCenter = camPos
                            + forwardFlat * planeDistMeters
                            + upWorld * verticalOffsetMeters;

        Vector2 gazePlanePos = gazeViz.lastPlaneHitPos;

        bool pinchNow = (pinchDetector != null && pinchDetector.pinchDownThisFrame);
        if (!pinchNow) return;
        Debug.Log($"[PinchDetector] Pinch Detected at {Time.time:F3}s");

        TargetBehavior nearestTB = null;
        float nearestDist = float.MaxValue;

        foreach (var go in spawner.spawnedTargets)
        {
            if (go == null) continue;
            var tb = go.GetComponent<TargetBehavior>();
            if (tb == null) continue;

            Vector3 toTarget = go.transform.position - planeCenter;
            float tx = Vector3.Dot(toTarget, rightFlat);
            float ty = Vector3.Dot(toTarget, upWorld);

            float d = Vector2.Distance(gazePlanePos, new Vector2(tx, ty));
            if (d < nearestDist)
            {
                nearestDist = d;
                nearestTB = tb;
            }
        }

        float dynamicRadius = spawner.targetDiameterMeters * selectRadiusScale;

        if (nearestTB != null && nearestDist <= 0.05)
        {
            int hitIndex = nearestTB.targetIndex;
            experimentManager.OnTargetSelected(hitIndex, gazePlanePos, planeCenter, rightFlat, upWorld);
            failedPinchCount = 0;
        }
        else
        {
            failedPinchCount++;
            Debug.LogWarning(
                $"[InputManager] Miss pinch ({failedPinchCount}/{maxFailedPinchBeforeSkip})"
            );
            if (failedPinchCount >= maxFailedPinchBeforeSkip)
            {
                Debug.LogWarning("[InputManager] Reached max failed pinches in a row");
                experimentManager.OnTargetSelected(-1, gazePlanePos, planeCenter, rightFlat, upWorld);
                failedPinchCount = 0;
            }
        }

    }
    
    private void EnsureGazeViz()
    {
        if (gazeViz != null) return;
        gazeViz = FindObjectOfType<GazeDataVisualizer>();
        if (gazeViz == null)
        {
            Debug.LogWarning("[NEON] GazeVisualizer not found");
        }
        else
        {
            Debug.Log("[NEON] GazeVisualizer hooked");
        }
    }
}
