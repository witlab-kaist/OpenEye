using System.Collections.Generic;
using UnityEngine;

public class TargetSpawner : MonoBehaviour
{
    [Header("References")]
    public Camera hmdCamera;
    public GameObject targetPrefab;
    public Transform targetRigRoot;

    [Header("Layout Params (per trial)")]
    public float planeDistMeters = 1.0f;
    public float radiusMeters    = 0.20f;
    public float targetDiameterMeters = 0.05f;
    public int numTargets = 8;
    [Header("Vertical offset from eye level (meters)")]
    public float verticalOffsetMeters = 0.0f;

    [Header("Runtime output")]
    public List<GameObject> spawnedTargets = new List<GameObject>();

    public void ClearAll()
    {
        foreach (var go in spawnedTargets)
        {
            if (go != null) Destroy(go);
        }
        spawnedTargets.Clear();
    }

    public void SpawnTargets()
    {
        if (hmdCamera == null || targetPrefab == null || targetRigRoot == null) return;

        ClearAll();

        for (int i = 0; i < numTargets; i++)
        {
            float angle = 2f * Mathf.PI * i / numTargets;
            float x = Mathf.Cos(angle) * radiusMeters;
            float y = Mathf.Sin(angle) * radiusMeters;
            float z = 0f;

            Vector3 localPos = new Vector3(x, y, z);

            GameObject t = Instantiate(targetPrefab, targetRigRoot);
            t.transform.localPosition = localPos;
            t.transform.localRotation = Quaternion.identity;

            t.transform.localScale = Vector3.one * targetDiameterMeters;

            var tb = t.GetComponent<TargetBehavior>();
            if (tb != null)
            {
                tb.targetIndex = i;
            }

            spawnedTargets.Add(t);
        }
    }

    public void UpdateRigPose()
    {
        if (hmdCamera == null || targetRigRoot == null) return;

        Transform camTf = hmdCamera.transform;

        Vector3 rigPos =
            camTf.position +
            camTf.forward * planeDistMeters +
            camTf.up      * verticalOffsetMeters;

        targetRigRoot.position = rigPos;

        targetRigRoot.rotation = camTf.rotation;
    }

    void Update()
    {
        UpdateRigPose();
    }
}
