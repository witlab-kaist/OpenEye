using System.Net.Sockets;
using UnityEngine;

public class GazeInputManagerPlane : MonoBehaviour
{
    [Header("Refs")]
    public Camera hmdCamera;
    public TargetSpawner spawner;
    public ExperimentManager experimentManager;
    public RightHandPinchDetector pinchDetector;
    public TCPClient tcpClientRefOverride;

    [Header("Params")]
    public float planeDistMeters = 1.0f;
    public float verticalOffsetMeters = 0.0f;
    public float selectRadiusScale = 1.0f;
    public int maxFailedPinchBeforeSkip = 3;
    private int failedPinchCount = 0;
    private TCPClient tcpClient;

    private TargetBehavior currentTB;
    private float gazeStartTime;

    void Start()
    {
        tcpClient = TCPClient.Instance;
        if (tcpClient == null)
        {
            Debug.Log("No TCPClient instance found");
        }
    }

    void Update()
    {
        if (hmdCamera == null || spawner == null || experimentManager == null || tcpClient == null) return;
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

        float gazeX = tcpClient.latestGazeX;
        float gazeY = tcpClient.latestGazeY;

        Vector3 gazeWorld = planeCenter
                          + rightFlat * gazeX
                          + upWorld * gazeY;

        Vector2 hitPos2D = new Vector2(gazeWorld.x, gazeWorld.y);
        
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

            Vector3 targetPos = go.transform.position;

            float d = Vector3.Distance(gazeWorld, targetPos);

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
            experimentManager.OnTargetSelected(hitIndex, hitPos2D);
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
                experimentManager.OnTargetSelected(-1, hitPos2D);
                failedPinchCount = 0;
            }
        }

    }
}
