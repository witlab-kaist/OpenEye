using MixedReality.Toolkit;
using UnityEngine;

namespace PupilLabs.MRTK
{
    public class MRTKInputNeon : MonoBehaviour
    {
        private MRTKNeonEyeGaze neonEyeGaze = null;
        private Vector3 localGazeDirection = Vector3.forward;
        private Vector3 localGazeOrigin = Vector3.zero;

        private void Awake()
        {
            neonEyeGaze = new MRTKNeonEyeGaze();
        }

        public void OnGazeDataReady(GazeDataProvider gazeDataProvider)
        {
            localGazeOrigin = gazeDataProvider.GazeRay.origin;
            localGazeDirection = gazeDataProvider.GazeRay.direction;
        }

        public Vector3 CameraFloorGazeDirection { get { return PlayspaceUtilities.XROrigin.Camera.transform.localRotation * localGazeDirection; } }

        public Vector3 CameraFloorGazeOrigin { get { return PlayspaceUtilities.XROrigin.Camera.transform.localPosition + (PlayspaceUtilities.XROrigin.Camera.transform.localRotation * localGazeOrigin); } }

        private void Update()
        {
            var gazeOriginCf = CameraFloorGazeOrigin;
            var lookRotCf = Quaternion.LookRotation(CameraFloorGazeDirection);
            neonEyeGaze.Update(true, lookRotCf, gazeOriginCf);
        }

        private void OnDestroy()
        {
            neonEyeGaze.Dispose();
        }
    }
}
