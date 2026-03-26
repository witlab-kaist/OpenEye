using UnityEngine;

namespace PupilLabs.Calibration
{
    public class CalibrationTarget : MonoBehaviour
    {
        public EyeTrackingCalibration calibration;

        public void OnTargetSelected()
        {
            calibration.AddObservation(transform.position);
        }
    }
}

