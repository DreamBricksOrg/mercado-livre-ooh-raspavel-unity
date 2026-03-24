using System;
using UnityEngine;
using UnityEngine.UI;

public class volumeButtonManager : MonoBehaviour
{
    public RuntimeDebugConsole runtimeDebugConsole;
    public GameObject Config;
    void Awake()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        gameObject.SetActive(true);
#else
        gameObject.SetActive(false);
#endif
    }
    void Start()
    {
        Debug.Log("Tentando conexão");
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using (var plugin = new AndroidJavaClass("com.dreambricks.volumecontroller.VolumeController"))
                {
                    plugin.CallStatic("receiveUnityActivity",activity);
                    plugin.CallStatic("startWatching");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("VolumeController init failed: " + ex);
        }
#endif
    }
    [ContextMenu("VolumeUp")]
    public void OnVolumeUp()
    {
        Debug.Log("Volume up");

        if (runtimeDebugConsole.visible == true)
        {
            runtimeDebugConsole.visible = false;
        }
        else if (runtimeDebugConsole.visible == false && Config.activeSelf == false)
        {
            runtimeDebugConsole.visible = true;
        }
    }

    [ContextMenu("VolumeDown")]
    public void OnVolumeDown()
    {
        Debug.Log("Volume down");
        if (Config.activeSelf == false)
        {
            runtimeDebugConsole.visible = false;
            Config.SetActive(true);
        }
        else
        {
            Config.SetActive(false);
        }
    }
}
