using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace PupilLabs.Calibration
{
    public class OpenCVSolver : PoseSolver
    {
        public override void AddSample(Vector3 referencePoint, Vector3 observedDirection)
        {
            observedDirections.Add(observedDirection / observedDirection.z); //sensor space, z must be 1 since it will be ignored, we will be using identity camera matrix and zero dist coeffs
            referencePoints.Add(referencePoint); //camera space

            if (pointStash != null)
            {
                if (referencePoints.Count * 2 > pointStash.Count)
                {
                    GameObject tmpGo = GameObject.Instantiate(referencePointPrefab, pointParent);
                    tmpGo.SetActive(false);
                    pointStash.Add(tmpGo);
                    tmpGo = GameObject.Instantiate(transformedPointPrefab, pointParent);
                    tmpGo.SetActive(false);
                    pointStash.Add(tmpGo);
                }
            }
        }

        protected override async Task<Matrix4x4> Solve(Vector3[] refPoints, Vector3[] obsDirections, CancellationToken token)
        {
            return await Task.Run(() =>
            {
                Matrix4x4 tm = Matrix4x4.identity;
                if (OpenCVWrapper.GetCameraPose(refPoints, obsDirections, new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }, new float[] { 0, 0, 0, 0, 0, 0, 0, 0 }, out Pose p))
                {
                    tm.SetTRS(p.position, p.rotation, Vector3.one);
                }
                return tm;
            });
        }
    }
}
