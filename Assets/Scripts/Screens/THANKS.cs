using UnityEngine;
using UnityEngine.SceneManagement;

public class THANKS : MonoBehaviour
{
    private float totalTime;
    private float currentTime;

    private ConfigManager configManager;

    private void Awake()
    {
        configManager = new();
        if (configManager != null)
        {
            totalTime = float.Parse(configManager.GetValue("TIME", "THANKS", "3"));
        }
    }

    private void OnEnable()
    {
        currentTime = totalTime;
    }

    private void Update()
    {
        currentTime -= Time.deltaTime;
        if (currentTime <= 0)
        {
            SceneManager.LoadScene("SampleScene");
        }
    }
}
