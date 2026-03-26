using MixedReality.Toolkit.UX;
using UnityEngine;

namespace PupilLabs.MRTK
{
    public class HandMenuGaze : MonoBehaviour
    {
        [SerializeField]
        private PressableButton gazeDebugButton;
        [SerializeField]
        private PressableButton gazePointerButton;

        private void Start()
        {
            GazeDataVisualizer gazeVisualizer = ServiceLocator.Instance.GetComponentInChildren<GazeDataVisualizer>();

            gazeDebugButton.ForceSetToggled(gazeVisualizer.RayVisible);
            gazeDebugButton.OnClicked.AddListener(() => gazeVisualizer.RayVisible = gazeDebugButton.IsToggled);

            gazePointerButton.ForceSetToggled(gazeVisualizer.DoRaycast && gazeVisualizer.RaycastPointerVisible);
            gazePointerButton.OnClicked.AddListener(() =>
            {
                gazeVisualizer.DoRaycast = gazePointerButton.IsToggled;
                gazeVisualizer.RaycastPointerVisible = gazePointerButton.IsToggled;
            });
        }
    }
}
