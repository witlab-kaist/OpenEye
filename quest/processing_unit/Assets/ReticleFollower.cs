using UnityEngine;

public class ReticleFollower : MonoBehaviour
{
    private TCPClient tcpClient;

    public TargetSpawner spawner;

    void Start()
    {
        tcpClient = TCPClient.Instance;
        if (tcpClient == null)
        {
            Debug.Log("No TCPClient instance found!");
        }
    }

    void Update()
    {
        if (tcpClient == null || spawner == null) return;

        float gx = tcpClient.latestGazeX;
        float gy = tcpClient.latestGazeY;

        transform.localPosition = new Vector3(gx, gy, 0f);
        // Debug.Log($"[ReticleFollower] gaze enq x={gx:F3}, y={gy:F3}");
    }
}
