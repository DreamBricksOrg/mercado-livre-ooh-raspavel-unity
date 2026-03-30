using UnityEngine;
using UnityEngine.UI;
using System.Globalization;

public class Tittle : MonoBehaviour
{
    [SerializeField] private ScratchCardImage scratchCardImage;
    [SerializeField] private float pulseSpeed = 2f;
    [SerializeField] private float minAlpha = 0.2f;
    [SerializeField] private float maxAlpha = 1f;
    [SerializeField] private float stopPulseAtScratchedPercent = 10f;
    [SerializeField] private Graphic targetGraphic;

    private float pulseTime;
    private bool isPulsing = true;

    private ConfigManager config;

    private void Awake()
    {
        config = new();
        pulseSpeed = ReadFloatSetting("pulseSpeed", 0.9f);
        minAlpha = ReadFloatSetting("minAlpha", 0.2f);
        maxAlpha = ReadFloatSetting("maxAlpha", 1f);
        stopPulseAtScratchedPercent = ReadFloatSetting("stopPulseAtScratchedPercent", 10f);

        if (targetGraphic == null)
        {
            targetGraphic = GetComponent<Graphic>();
        }

        ApplyAlpha(maxAlpha);
    }

    private void OnEnable()
    {
        if (scratchCardImage != null)
        {
            scratchCardImage.ScratchReset += OnScratchReset;
        }
    }

    private void OnDisable()
    {
        if (scratchCardImage != null)
        {
            scratchCardImage.ScratchReset -= OnScratchReset;
        }
    }

    private void Update()
    {
        if (isPulsing && scratchCardImage != null && scratchCardImage.scratchedPercent > stopPulseAtScratchedPercent)
        {
            StopPulse();
        }

        if (!isPulsing)
        {
            return;
        }

        pulseTime += Time.deltaTime * pulseSpeed;
        float t = Mathf.PingPong(pulseTime, 1f);
        float alpha = Mathf.Lerp(minAlpha, maxAlpha, t);
        ApplyAlpha(alpha);
    }

    private void StopPulse()
    {
        isPulsing = false;
        ApplyAlpha(0f);
    }

    private void OnScratchReset()
    {
        pulseTime = 0f;
        isPulsing = true;
        ApplyAlpha(maxAlpha);
    }

    private void ApplyAlpha(float alpha)
    {
        if (targetGraphic == null)
        {
            return;
        }

        Color color = targetGraphic.color;
        color.a = Mathf.Clamp01(alpha);
        targetGraphic.color = color;
    }

    private float ReadFloatSetting(string key, float fallbackValue)
    {
        string rawValue = config.GetValue("GameSettings", key, fallbackValue.ToString(CultureInfo.InvariantCulture));
        if (float.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedInvariant))
        {
            return parsedInvariant;
        }

        if (float.TryParse(rawValue, NumberStyles.Float, CultureInfo.CurrentCulture, out float parsedCurrent))
        {
            return parsedCurrent;
        }

        return fallbackValue;
    }
}
