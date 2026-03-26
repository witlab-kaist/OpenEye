using UnityEngine;
using MixedReality.Toolkit.Input;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEditorInternal;

public class RightHandPinchDetector : MonoBehaviour
{
    [Header("Refs")]
    public ArticulatedHandController rightHandController;
    public bool pinchDownThisFrame { get; private set; }

    public bool pinching { get; private set; }

    private bool prevActive = false;

    void Update()
    {
        pinchDownThisFrame = false;
        pinching = false;

        if (rightHandController == null)
            return;

        XRControllerState state = rightHandController.currentControllerState;

        if (state == null)
            return;

        bool isActiveNow = state.selectInteractionState.active;
        bool activatedNow = state.selectInteractionState.activatedThisFrame;

        pinching = isActiveNow;
        if (activatedNow)
        {
            pinchDownThisFrame = true;
        }
        prevActive = isActiveNow;
    }
}
