using UnityEngine;
using System;
using System.Text;
using System.Collections;

public class ArduinoControllerAndroid : MonoBehaviour
{
    private AndroidJavaObject usbSerialActivity;
    private AndroidJavaObject unityActivity;
    public string textDebugLog;
    public bool IsConnected { get; private set; }
    public bool HasPermission { get; private set; }

    private const int BAUD_RATE = 9600;

    // Event callbacks for your game
    public Action<string> OnMessageReceived;
    public Action OnConnected;
    public Action OnDisconnected;

    void fillingTextDebug(string log)
    {
        textDebugLog = log;
    }

    void Awake()
    {        
        InitializeJava();
    }

    private void InitializeJava()
    {
        using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        {
            unityActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
        }

        usbSerialActivity = new AndroidJavaObject("com.example.arduinoconnection.UsbSerialActivity");

        // Send Unity activity to Java
        usbSerialActivity.CallStatic("receiveUnityActivity", unityActivity);
    }

    void Start()
    {
       StartCoroutine(StartUsbCoroutine());
    }

    IEnumerator StartUsbCoroutine()
    {
        yield return new WaitForSeconds(0.5f); // short delay
        usbSerialActivity.Call<string>("start", BAUD_RATE);
    }
    // Called from Java
    public void OnUsbConnected(string unused)
    {
        Debug.Log("[USB] Connected");
        textDebugLog = "[USB] Connected";
        IsConnected = true;
        HasPermission = true;
        OnConnected?.Invoke();
    }

    // Called from Java
    public void OnUsbDisconnected(string msg)
    {
        Debug.Log("[USB] Disconnected: " + msg);
        textDebugLog = "[USB] Disconnected: " + msg;
        IsConnected = false;

        OnDisconnected?.Invoke();
    }

    public byte[] Pop()
    {
        if (usbSerialActivity == null) return null;
        return usbSerialActivity.Call<byte[]>("popLatest");
    }
    // Send to Arduino
    public void Send(string message)
    {
        if (!IsConnected)
        {
            Debug.LogWarning("[USB] Cannot send, not connected");
            textDebugLog = "[USB] Cannot send, not connected";

            return;
        }

        usbSerialActivity.Call<string>("Send", message);
    }

    void OnApplicationQuit()
    {
        usbSerialActivity?.Call("destroy");
    }
}
