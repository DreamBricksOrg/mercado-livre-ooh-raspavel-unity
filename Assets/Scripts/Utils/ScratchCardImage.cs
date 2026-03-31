using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Collections;
using System.Globalization;
using System;

[RequireComponent(typeof(RawImage))]
public class ScratchCardImage : MonoBehaviour, IPointerDownHandler, IDragHandler
{
    public const float LogThresholdPercent = 50f;

    [Header("Brush Settings")]
    [SerializeField, Tooltip("Alpha mask texture. Let it empty to use default circle.")] 
    private Texture2D brushTexture;
    [SerializeField, Tooltip("Size of the brush tip in pixels.")] 
    private int brushSize;

    [Header("Brush Presets")]
    [SerializeField]
    private List<Texture2D> predefinedBrushTextures = new();

    [Header("Scratch Area")]
    [SerializeField]
    private RectTransform scratchAreaRectTransform;
    [SerializeField]
    private RectTransform scratchAreaTintRectTransform;
    [SerializeField]
    private Color32 outsideAreaTintColor = new Color32(170, 170, 170, 255);
    [SerializeField]
    private Color32 scratchAreaTintColor = new Color32(255, 255, 255, 255);
    [SerializeField]
    private bool pulseOutsideAreaTint = true;
    [SerializeField]
    private float outsideAreaTintPulseSpeed = 0.9f;
    [SerializeField]
    private float outsideAreaTintPauseAtHighlight = 2f;
    [SerializeField]
    private float outsideAreaTintPauseAtBase = 0.2f;
    [SerializeField]
    private float outsideAreaTintMatchThresholdPercent = 5f;

    [Header("Reset Settings")]
    [SerializeField, Tooltip("Time in seconds to reset after no touch.")]
    private float idleResetTime;
    [SerializeField, Tooltip("If true, resets after idle time.")]
    private bool resetOnIdle = true;

    [SerializeField]
    private GameObject arrowImage;

    [Header("Scratch Progress")]
    [Range(0f, 100f)]
    public float scratchedPercent;

    private RawImage targetRawImage;
    private RectTransform rectTransform;
    private Canvas parentCanvas;

    private Color32[] brushPixels;
    private int brushWidth;
    private int brushHeight;
    private Texture originalTexture;
    private Texture2D runtimeTexture;
    private Color32[] pixels;
    private bool textureDirty;
    private bool hasLastPoint;
    private Vector2Int lastTexturePoint;
    private float currentIdleTime;
    private bool isScratched;
    private ConfigManager config;
    private List<string> logTags = new();
    private long currentAlphaSum;
    private long maxAlphaSum;
    private int brushTextureIndex;
    private int scratchAreaMinX;
    private int scratchAreaMaxX;
    private int scratchAreaMinY;
    private int scratchAreaMaxY;
    private int scratchAreaTintMinX;
    private int scratchAreaTintMaxX;
    private int scratchAreaTintMinY;
    private int scratchAreaTintMaxY;
    private Color32[] untintedPixels;
    private float outsideAreaTintPulseLerp;
    private int outsideAreaTintPulseDirection = 1;
    private float outsideAreaTintPauseTimer;
    private bool outsideAreaTintMatchedToScratchArea;
    private Color32 currentOutsideAreaTint;
    private bool hasHandledIdleCompletion;
    public event Action ScratchStarted;
    public event Action ScratchReset;

    private void Awake()
    {
        config = new();
        idleResetTime = ReadFloatSetting("idleResetTime", 5f);
        brushSize = ReadIntSetting("brushSize", 120);
        brushTextureIndex = ReadIntSetting("brushTextureIndex", 3);
        outsideAreaTintPulseSpeed = ReadFloatSetting("pulseSpeed", outsideAreaTintPulseSpeed);
        outsideAreaTintPauseAtHighlight = ReadFloatSetting("outsideAreaTintPauseAtHighlight", outsideAreaTintPauseAtHighlight);
        outsideAreaTintPauseAtBase = ReadFloatSetting("outsideAreaTintPauseAtBase", outsideAreaTintPauseAtBase);

        logTags.Add("scratch");

        targetRawImage = GetComponent<RawImage>();
        rectTransform = targetRawImage.rectTransform;
        parentCanvas = GetComponentInParent<Canvas>();
        originalTexture = targetRawImage.texture;
        brushTexture = ResolveBrushTextureByIndex();
        targetRawImage.material = null;
        targetRawImage.color = Color.white;
        targetRawImage.uvRect = new Rect(0f, 0f, 1f, 1f);
        InitializeBrush();
        InitializeRuntimeTexture();
    }

    private void InitializeBrush()
    {
        if (brushTexture != null)
        {
            brushWidth = brushSize > 0 ? brushSize : brushTexture.width;
            float aspect = (float)brushTexture.height / brushTexture.width;
            brushHeight = brushSize > 0 ? Mathf.RoundToInt(brushSize * aspect) : brushTexture.height;

            RenderTexture rt = RenderTexture.GetTemporary(brushWidth, brushHeight, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(brushTexture, rt);
            
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            
            Texture2D readableBrush = new Texture2D(brushWidth, brushHeight, TextureFormat.RGBA32, false);
            readableBrush.ReadPixels(new Rect(0, 0, brushWidth, brushHeight), 0, 0);
            readableBrush.Apply();
            
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            
            brushPixels = readableBrush.GetPixels32();
            Destroy(readableBrush);
        }
        else
        {
            brushWidth = brushSize > 0 ? brushSize : 160;
            brushHeight = brushWidth;
            brushPixels = new Color32[brushWidth * brushHeight];
            int radius = brushWidth / 2;
            int radiusSquared = radius * radius;
            for (int y = 0; y < brushHeight; y++)
            {
                for (int x = 0; x < brushWidth; x++)
                {
                    int dx = x - radius;
                    int dy = y - radius;
                    byte alpha = (dx * dx + dy * dy <= radiusSquared) ? (byte)255 : (byte)0;
                    brushPixels[y * brushWidth + x] = new Color32(0, 0, 0, alpha);
                }
            }
        }
    }

    private void Update()
    {
        UpdateOutsideAreaTintPulse();

        bool handledTouch = HandleTouchInputFallback();
        bool handledMouse = HandleMouseInputFallback();

        if (handledTouch || handledMouse)
        {
            currentIdleTime = 0f;
        }
        else
        {
            hasLastPoint = false;

            if (resetOnIdle && isScratched)
            {
                currentIdleTime += Time.deltaTime;
                if (currentIdleTime >= idleResetTime)
                {
                    if (hasHandledIdleCompletion)
                    {
                        return;
                    }

                    hasHandledIdleCompletion = true;
                    float scratchedPercentAtReset = scratchedPercent;
                    if (scratchedPercentAtReset > LogThresholdPercent)
                    {
                        SaveLog("PROTEGEU", "INFO", logTags, scratchedPercentAtReset);
                    }

                    // ResetScratch();
                    UIManager.Instance.OpenScreen("THANKS");
                }
            }
        }
    }

    private void LateUpdate()
    {
        if (!textureDirty || runtimeTexture == null || pixels == null)
        {
            return;
        }

        runtimeTexture.SetPixels32(pixels);
        runtimeTexture.Apply(false, false);
        textureDirty = false;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        hasLastPoint = false;
        TryScratchAtScreenPoint(eventData.position, eventData.pressEventCamera);
    }

    public void OnDrag(PointerEventData eventData)
    {
        TryScratchAtScreenPoint(eventData.position, eventData.pressEventCamera);
    }

    public void ResetScratch()
    {
        InitializeRuntimeTexture();
        ScratchReset?.Invoke();
    }

    private bool HandleTouchInputFallback()
    {
        if (Input.touchCount == 0)
        {
            return false;
        }

        Camera inputCamera = GetInputCamera();
        bool handled = false;

        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch touch = Input.GetTouch(i);
            if (touch.phase == TouchPhase.Began)
            {
                hasLastPoint = false;
            }

            if (touch.phase != TouchPhase.Began &&
                touch.phase != TouchPhase.Moved &&
                touch.phase != TouchPhase.Stationary)
            {
                continue;
            }

            if (!RectTransformUtility.RectangleContainsScreenPoint(rectTransform, touch.position, inputCamera))
            {
                continue;
            }

            handled = true;
            TryScratchAtScreenPoint(touch.position, inputCamera);
        }

        return handled;
    }

    private bool HandleMouseInputFallback()
    {
        if (!Input.GetMouseButton(0))
        {
            return false;
        }

        Camera inputCamera = GetInputCamera();
        Vector2 mousePosition = Input.mousePosition;

        if (!RectTransformUtility.RectangleContainsScreenPoint(rectTransform, mousePosition, inputCamera))
        {
            return false;
        }

        if (Input.GetMouseButtonDown(0))
        {
            hasLastPoint = false;
        }

        TryScratchAtScreenPoint(mousePosition, inputCamera);
        return true;
    }

    private Camera GetInputCamera()
    {
        if (parentCanvas == null || parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            return null;
        }

        return parentCanvas.worldCamera;
    }

    private void InitializeRuntimeTexture()
    {
        if (originalTexture == null)
        {
            return;
        }

        if (runtimeTexture != null)
        {
            Destroy(runtimeTexture);
        }

        runtimeTexture = ExtractTexture(originalTexture);
        runtimeTexture.filterMode = FilterMode.Point;
        runtimeTexture.wrapMode = TextureWrapMode.Clamp;
        runtimeTexture.anisoLevel = 0;
        pixels = runtimeTexture.GetPixels32();
        untintedPixels = new Color32[pixels.Length];
        Array.Copy(pixels, untintedPixels, pixels.Length);
        UpdateScratchBounds();
        UpdateTintBounds();
        outsideAreaTintPulseLerp = 0f;
        outsideAreaTintPulseDirection = 1;
        outsideAreaTintPauseTimer = 0f;
        outsideAreaTintMatchedToScratchArea = false;
        currentOutsideAreaTint = outsideAreaTintColor;
        ApplyScratchAreaTint(currentOutsideAreaTint);
        maxAlphaSum = (long)(scratchAreaMaxX - scratchAreaMinX + 1) * (scratchAreaMaxY - scratchAreaMinY + 1) * 255L;
        currentAlphaSum = 0L;
        for (int y = scratchAreaMinY; y <= scratchAreaMaxY; y++)
        {
            int rowOffset = y * runtimeTexture.width;
            for (int x = scratchAreaMinX; x <= scratchAreaMaxX; x++)
            {
                currentAlphaSum += pixels[rowOffset + x].a;
            }
        }
        UpdateScratchedPercent();
        targetRawImage.texture = runtimeTexture;
        textureDirty = true;
        hasLastPoint = false;
        
        isScratched = false;
        currentIdleTime = 0f;
        hasHandledIdleCompletion = false;
    }

    private Texture2D ResolveBrushTextureByIndex()
    {
        if (predefinedBrushTextures == null || predefinedBrushTextures.Count == 0)
        {
            return brushTexture;
        }

        int clampedIndex = Mathf.Clamp(brushTextureIndex, 0, predefinedBrushTextures.Count - 1);
        Texture2D selectedTexture = predefinedBrushTextures[clampedIndex];
        if (selectedTexture == null)
        {
            return brushTexture;
        }

        return selectedTexture;
    }

    private Texture2D ExtractTexture(Texture sourceTexture)
    {
        int width = sourceTexture.width;
        int height = sourceTexture.height;
        Texture2D extracted = new Texture2D(width, height, TextureFormat.RGBA32, false);
        RenderTexture renderTexture = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(sourceTexture, renderTexture);

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = renderTexture;
        extracted.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
        extracted.Apply(false, false);
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(renderTexture);

        return extracted;
    }

    private void TryScratchAtScreenPoint(Vector2 screenPoint, Camera eventCamera)
    {
        if (runtimeTexture == null || pixels == null)
        {
            return;
        }

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPoint, eventCamera, out Vector2 localPoint))
        {
            return;
        }

        Rect rect = rectTransform.rect;
        float normalizedX = (localPoint.x - rect.x) / rect.width;
        float normalizedY = (localPoint.y - rect.y) / rect.height;

        if (normalizedX < 0f || normalizedX > 1f || normalizedY < 0f || normalizedY > 1f)
        {
            return;
        }

        int textureX = Mathf.Clamp(Mathf.RoundToInt(normalizedX * runtimeTexture.width), 0, runtimeTexture.width - 1);
        int textureY = Mathf.Clamp(Mathf.RoundToInt(normalizedY * runtimeTexture.height), 0, runtimeTexture.height - 1);
        if (textureX < scratchAreaMinX || textureX > scratchAreaMaxX || textureY < scratchAreaMinY || textureY > scratchAreaMaxY)
        {
            return;
        }

        Vector2Int currentPoint = new Vector2Int(textureX, textureY);

        if (hasLastPoint)
        {
            EraseAlongLine(lastTexturePoint, currentPoint);
        }
        else
        {
            EraseWithBrush(currentPoint.x, currentPoint.y);
        }

        if (!isScratched)
        {
            ScratchStarted?.Invoke();
        }

        lastTexturePoint = currentPoint;
        hasLastPoint = true;
        textureDirty = true;
        isScratched = true;
    }

    private void EraseAlongLine(Vector2Int from, Vector2Int to)
    {
        float distance = Vector2Int.Distance(from, to);
        float stepSize = Mathf.Max(1f, brushWidth * 0.175f);
        int steps = Mathf.Max(1, Mathf.CeilToInt(distance / stepSize));

        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            int x = Mathf.RoundToInt(Mathf.Lerp(from.x, to.x, t));
            int y = Mathf.RoundToInt(Mathf.Lerp(from.y, to.y, t));
            EraseWithBrush(x, y);
        }
    }

    private void EraseWithBrush(int centerX, int centerY)
    {
        int halfWidth = brushWidth / 2;
        int halfHeight = brushHeight / 2;
        int startX = centerX - halfWidth;
        int startY = centerY - halfHeight;

        int minX = Mathf.Max(scratchAreaMinX, startX);
        int maxX = Mathf.Min(scratchAreaMaxX + 1, startX + brushWidth);
        int minY = Mathf.Max(scratchAreaMinY, startY);
        int maxY = Mathf.Min(scratchAreaMaxY + 1, startY + brushHeight);

        for (int y = minY; y < maxY; y++)
        {
            int brushY = y - startY;
            int rowOffset = y * runtimeTexture.width;
            int brushRowOffset = brushY * brushWidth;

            for (int x = minX; x < maxX; x++)
            {
                int brushX = x - startX;
                byte brushAlpha = brushPixels[brushRowOffset + brushX].a;

                if (brushAlpha > 0)
                {
                    int index = rowOffset + x;
                    Color32 currentPixel = pixels[index];
                    
                    float alphaMultiplier = 1f - (brushAlpha / 255f);
                    byte newAlpha = (byte)(currentPixel.a * alphaMultiplier);
                    
                    if (newAlpha < currentPixel.a)
                    {
                        currentAlphaSum -= currentPixel.a - newAlpha;
                        currentPixel.a = newAlpha;
                        pixels[index] = currentPixel;
                        Color32 untintedPixel = untintedPixels[index];
                        untintedPixel.a = newAlpha;
                        untintedPixels[index] = untintedPixel;
                    }
                }
            }
        }

        UpdateScratchedPercent();
    }

    private void UpdateScratchedPercent()
    {
        if (maxAlphaSum <= 0)
        {
            scratchedPercent = 0f;
            return;
        }

        float visiblePercent = (float)currentAlphaSum / maxAlphaSum;
        scratchedPercent = Mathf.Clamp01(1f - visiblePercent) * 100f;
    }

    private void UpdateScratchBounds()
    {
        ResolveRectTransformBounds(
            scratchAreaRectTransform,
            out scratchAreaMinX,
            out scratchAreaMaxX,
            out scratchAreaMinY,
            out scratchAreaMaxY
        );
    }

    private void UpdateTintBounds()
    {
        RectTransform tintReferenceRectTransform = scratchAreaTintRectTransform != null
            ? scratchAreaTintRectTransform
            : scratchAreaRectTransform;

        ResolveRectTransformBounds(
            tintReferenceRectTransform,
            out scratchAreaTintMinX,
            out scratchAreaTintMaxX,
            out scratchAreaTintMinY,
            out scratchAreaTintMaxY
        );
    }

    private void ResolveRectTransformBounds(
        RectTransform targetAreaRectTransform,
        out int minX,
        out int maxX,
        out int minY,
        out int maxY)
    {
        if (runtimeTexture == null)
        {
            minX = 0;
            maxX = 0;
            minY = 0;
            maxY = 0;
            return;
        }

        Rect areaRect = rectTransform.rect;
        if (targetAreaRectTransform != null)
        {
            Vector3[] worldCorners = new Vector3[4];
            targetAreaRectTransform.GetWorldCorners(worldCorners);
            Camera inputCamera = GetInputCamera();
            bool hasPoint = false;
            float minXLocal = float.MaxValue;
            float maxXLocal = float.MinValue;
            float minYLocal = float.MaxValue;
            float maxYLocal = float.MinValue;

            for (int i = 0; i < worldCorners.Length; i++)
            {
                Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(inputCamera, worldCorners[i]);
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPoint, inputCamera, out Vector2 localPoint))
                {
                    continue;
                }

                hasPoint = true;
                minXLocal = Mathf.Min(minXLocal, localPoint.x);
                maxXLocal = Mathf.Max(maxXLocal, localPoint.x);
                minYLocal = Mathf.Min(minYLocal, localPoint.y);
                maxYLocal = Mathf.Max(maxYLocal, localPoint.y);
            }

            if (hasPoint)
            {
                areaRect = Rect.MinMaxRect(minXLocal, minYLocal, maxXLocal, maxYLocal);
            }
        }

        Rect imageRect = rectTransform.rect;
        float minNormX = Mathf.Clamp01((areaRect.xMin - imageRect.x) / imageRect.width);
        float maxNormX = Mathf.Clamp01((areaRect.xMax - imageRect.x) / imageRect.width);
        float minNormY = Mathf.Clamp01((areaRect.yMin - imageRect.y) / imageRect.height);
        float maxNormY = Mathf.Clamp01((areaRect.yMax - imageRect.y) / imageRect.height);

        minX = Mathf.Clamp(Mathf.FloorToInt(minNormX * (runtimeTexture.width - 1)), 0, runtimeTexture.width - 1);
        maxX = Mathf.Clamp(Mathf.CeilToInt(maxNormX * (runtimeTexture.width - 1)), 0, runtimeTexture.width - 1);
        minY = Mathf.Clamp(Mathf.FloorToInt(minNormY * (runtimeTexture.height - 1)), 0, runtimeTexture.height - 1);
        maxY = Mathf.Clamp(Mathf.CeilToInt(maxNormY * (runtimeTexture.height - 1)), 0, runtimeTexture.height - 1);

        if (minX > maxX)
        {
            minX = maxX;
        }

        if (minY > maxY)
        {
            minY = maxY;
        }
    }

    private void ApplyScratchAreaTint(Color32 currentOutsideTintColor)
    {
        if (pixels == null || runtimeTexture == null || untintedPixels == null)
        {
            return;
        }

        int textureWidth = runtimeTexture.width;
        for (int y = 0; y < runtimeTexture.height; y++)
        {
            int rowOffset = y * textureWidth;
            bool isInsideY = y >= scratchAreaTintMinY && y <= scratchAreaTintMaxY;
            for (int x = 0; x < textureWidth; x++)
            {
                bool isInsideArea = isInsideY && x >= scratchAreaTintMinX && x <= scratchAreaTintMaxX;
                Color32 tintColor = isInsideArea ? scratchAreaTintColor : currentOutsideTintColor;
                int index = rowOffset + x;
                Color32 untintedPixel = untintedPixels[index];
                Color32 pixel = untintedPixel;
                pixel.r = (byte)(pixel.r * tintColor.r / 255);
                pixel.g = (byte)(pixel.g * tintColor.g / 255);
                pixel.b = (byte)(pixel.b * tintColor.b / 255);
                pixel.a = untintedPixel.a;
                pixels[index] = pixel;
            }
        }
    }

    private void UpdateOutsideAreaTintPulse()
    {
        if (runtimeTexture == null || pixels == null || untintedPixels == null)
        {
            return;
        }

        Color32 nextOutsideColor;
        if (outsideAreaTintMatchedToScratchArea || scratchedPercent >= outsideAreaTintMatchThresholdPercent)
        {
            outsideAreaTintPulseLerp = Mathf.Clamp01(outsideAreaTintPulseLerp + Time.deltaTime * outsideAreaTintPulseSpeed);
            if (outsideAreaTintPulseLerp >= 1f)
            {
                outsideAreaTintMatchedToScratchArea = true;
            }

            Color matched = Color.Lerp((Color)outsideAreaTintColor, (Color)scratchAreaTintColor, outsideAreaTintPulseLerp);
            nextOutsideColor = (Color32)matched;

            arrowImage.SetActive(false);
            
        }
        else if (pulseOutsideAreaTint)
        {
            if (outsideAreaTintPauseTimer > 0f)
            {
                outsideAreaTintPauseTimer -= Time.deltaTime;
                if (outsideAreaTintPauseTimer < 0f)
                {
                    outsideAreaTintPauseTimer = 0f;
                }
            }
            else
            {
                float step = Time.deltaTime * outsideAreaTintPulseSpeed;
                outsideAreaTintPulseLerp += step * outsideAreaTintPulseDirection;

                if (outsideAreaTintPulseLerp >= 1f)
                {
                    outsideAreaTintPulseLerp = 1f;
                    outsideAreaTintPulseDirection = -1;
                    outsideAreaTintPauseTimer = Mathf.Max(0f, outsideAreaTintPauseAtHighlight);
                }
                else if (outsideAreaTintPulseLerp <= 0f)
                {
                    outsideAreaTintPulseLerp = 0f;
                    outsideAreaTintPulseDirection = 1;
                    outsideAreaTintPauseTimer = Mathf.Max(0f, outsideAreaTintPauseAtBase);
                }
            }

            Color interpolated = Color.Lerp((Color)outsideAreaTintColor, (Color)scratchAreaTintColor, outsideAreaTintPulseLerp);
            nextOutsideColor = (Color32)interpolated;
        }
        else
        {
            nextOutsideColor = outsideAreaTintColor;
        }

        if (!nextOutsideColor.Equals(currentOutsideAreaTint))
        {
            currentOutsideAreaTint = nextOutsideColor;
            ApplyScratchAreaTint(currentOutsideAreaTint);
            textureDirty = true;
        }
    }

    private int ReadIntSetting(string key, int fallbackValue)
    {
        string rawValue = config.GetValue("GameSettings", key, fallbackValue.ToString(CultureInfo.InvariantCulture));
        if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedInvariant))
        {
            return parsedInvariant;
        }

        if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.CurrentCulture, out int parsedCurrent))
        {
            return parsedCurrent;
        }

        return fallbackValue;
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

    void SaveLog(string status, string level, List<string> tags, float scratchedPercentValue = -1f)
    {
        StartCoroutine(SaveLogCoroutine(status, scratchedPercentValue));
        StartCoroutine(SaveLogInNewLogCenterCoroutine(status, level, tags, scratchedPercentValue));
    }

    IEnumerator SaveLogCoroutine(string status, float scratchedPercentValue)
    {
        yield return LogUtil.GetDatalogFromJsonCoroutine((dataLog) =>
        {
            if (dataLog != null)
            {
                dataLog.status = status;
                if (scratchedPercentValue >= 0f)
                {
                    dataLog.additional = scratchedPercentValue.ToString("F2", CultureInfo.InvariantCulture);
                }
                LogUtil.SaveLog(dataLog);
            }
            else
            {
                Debug.LogError("Erro ao carregar o DataLog do JSON.");
            }
        });
    }
    IEnumerator SaveLogInNewLogCenterCoroutine(string message, string level, List<string> tags, float scratchedPercentValue)
    {
        yield return LogUtilSdk.GetDatalogFromJsonCoroutine((dataLog) =>
       {
           if (dataLog != null)
           {
               dataLog.message = message;
               dataLog.level = level;
               dataLog.tags = tags;
               dataLog.data = scratchedPercentValue >= 0f
                   ? new { scratchedPercent = scratchedPercentValue }
                   : new { };
               LogUtilSdk.SaveLogToJson(dataLog);
           }
           else
           {
               Debug.LogError("Erro ao carregar o DataLog do JSON.");
           }
       });
    }
}
