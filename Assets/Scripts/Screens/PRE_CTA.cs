using UnityEngine;

public class PRE_CTA : MonoBehaviour
{
    private float totalTime = 0.5f;
    private float currentTime;

    private void OnEnable()
    {
        currentTime = totalTime;
    }

    private void Update()
    {
        currentTime -= Time.deltaTime;
        if (currentTime <= 0)
        {
            UIManager.Instance.OpenScreen("CTA");
        }
    }
}
