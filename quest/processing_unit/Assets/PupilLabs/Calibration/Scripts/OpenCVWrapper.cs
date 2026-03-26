using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

public static class OpenCVWrapper
{
    [DllImport("OpenCVWrapper")]
    private static extern IntPtr CGetCameraPose(int npoints, float[] objectPoints, float[] imagePoints, float[] cameraMatrix, float[] distCoeffs);

    [DllImport("OpenCVWrapper")]
    private static extern IntPtr CFree(IntPtr ptr);

    public static bool GetCameraPose(Vector3[] objectPoints, Vector3[] imagePoints, float[] cameraMatrix, float[] distCoeffs, out Pose pose)
    {
        List<float> objPoints = new List<float>();
        foreach (var p in objectPoints)
        {
            objPoints.Add(p.x);
            objPoints.Add(-p.y);
            objPoints.Add(p.z);
        }
        List<float> imgPoints = new List<float>();
        foreach (var p in imagePoints)
        {
            imgPoints.Add(p.x);
            imgPoints.Add(-p.y);
        }

        IntPtr ptr = CGetCameraPose(objectPoints.Length, objPoints.ToArray(), imgPoints.ToArray(), cameraMatrix, distCoeffs);
        if (ptr != IntPtr.Zero)
        {
            Debug.Log("[OpenCVWrapper] CGetCameraPose success");
            float[] poseTmp = new float[12];
            Marshal.Copy(ptr, poseTmp, 0, 12);
            CFree(ptr);

            Vector3 pos = new Vector3(poseTmp[9], -poseTmp[10], poseTmp[11]);
            Matrix4x4 m = Matrix4x4.identity;
            m.m00 = poseTmp[0];
            m.m01 = poseTmp[1];
            m.m02 = poseTmp[2];
            m.m10 = poseTmp[3];
            m.m11 = poseTmp[4];
            m.m12 = poseTmp[5];
            m.m20 = poseTmp[6];
            m.m21 = poseTmp[7];
            m.m22 = poseTmp[8];
            Vector3 rot = Vector3.Scale(m.rotation.eulerAngles, new Vector3(-1f, 1f, -1f));
            Debug.Log($"[OpenCVWrapper] CGetCameraPose result pos: {pos} rot: {rot}");
            pose = new Pose(pos, Quaternion.Euler(rot));
            return true;
        }
        else
        {
            Debug.Log("[OpenCVWrapper] CGetCameraPose returned null");
        }
        pose = Pose.identity;
        return false;
    }
}
