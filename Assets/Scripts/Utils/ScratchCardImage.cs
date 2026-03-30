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

    [Header("Reset Settings")]
    [SerializeField, Tooltip("Time in seconds to reset after no touch.")]
    private float idleResetTime;
    [SerializeField, Tooltip("If true, resets after idle time.")]
    private bool resetOnIdle = true;

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
    public event Action ScratchStarted;
    public event Action ScratchReset;

    private void Awake()
    {
        config = new();
        idleResetTime = int.Parse(config.GetValue("GameSettings", "idleResetTime", "5"));
        brushSize = int.Parse(config.GetValue("GameSettings", "brushSize", "120"));

        logTags.Add("scratch");

        targetRawImage = GetComponent<RawImage>();
        rectTransform = targetRawImage.rectTransform;
        parentCanvas = GetComponentInParent<Canvas>();
        originalTexture = targetRawImage.texture;
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
                    float scratchedPercentAtReset = scratchedPercent;
                    if (scratchedPercentAtReset > LogThresholdPercent)
                    {
                        SaveLog("PROTEGEU", "INFO", logTags, scratchedPercentAtReset);
                    }

                    ResetScratch();
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
        maxAlphaSum = (long)pixels.Length * 255L;
        currentAlphaSum = 0L;
        for (int i = 0; i < pixels.Length; i++)
        {
            currentAlphaSum += pixels[i].a;
        }
        UpdateScratchedPercent();
        targetRawImage.texture = runtimeTexture;
        textureDirty = true;
        hasLastPoint = false;
        
        isScratched = false;
        currentIdleTime = 0f;
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

        int minX = Mathf.Max(0, startX);
        int maxX = Mathf.Min(runtimeTexture.width, startX + brushWidth);
        int minY = Mathf.Max(0, startY);
        int maxY = Mathf.Min(runtimeTexture.height, startY + brushHeight);

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
