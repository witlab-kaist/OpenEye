using System.Net.Sockets;
using UnityEngine;
using UnityEngine.UI;

public class CalibUI : MonoBehaviour
{
    public TCPClient tcp;
    public DotGridCalibrator grid;

    [Header("UI")]
    public Button connectBtn;
    public Button calibBtn;
    public Text statusText;

    void Awake()
    {
        if (connectBtn) connectBtn.onClick.AddListener(OnConnectClicked);
        if (calibBtn) calibBtn.onClick.AddListener(OnCalibClicked);

        tcp.OnUpdateStep += step =>
        {
            grid.ShowStep(step);
            if (statusText) statusText.text = $"Step: {step}";
        };
        tcp.OnCalibrationEnd += () =>
        {
            grid.HideDot();
            if (statusText) statusText.text = "Calibration End";
        };
    }

    void OnConnectClicked()
    {
        tcp.Connect();
        if (statusText) statusText.text = "Connecting...";
    }

    void OnCalibClicked()
    {
        grid.ShowStep(0);
        if (statusText) statusText.text = "Calib started (waiting steps...)";
    }
}
