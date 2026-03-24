using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(RawImage))]
public class ScratchCardImage : MonoBehaviour, IPointerDownHandler, IDragHandler
{
    private const int EraserRadiusPixels = 80;

    private RawImage targetRawImage;
    private RectTransform rectTransform;
    private Canvas parentCanvas;
    private Texture2D runtimeTexture;
    private Color32[] pixels;
    private bool textureDirty;
    private bool hasLastPoint;
    private Vector2Int lastTexturePoint;

    private void Awake()
    {
        targetRawImage = GetComponent<RawImage>();
        rectTransform = targetRawImage.rectTransform;
        parentCanvas = GetComponentInParent<Canvas>();
        targetRawImage.material = null;
        targetRawImage.color = Color.white;
        targetRawImage.uvRect = new Rect(0f, 0f, 1f, 1f);
        InitializeRuntimeTexture();
    }

    private void Update()
    {
        bool handledTouch = HandleTouchInputFallback();
        bool handledMouse = HandleMouseInputFallback();

        if (!handledTouch && !handledMouse)
        {
            hasLastPoint = false;
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
        if (targetRawImage.texture == null)
        {
            return;
        }

        Texture sourceTexture = targetRawImage.texture;
        runtimeTexture = ExtractTexture(sourceTexture);
        runtimeTexture.filterMode = FilterMode.Point;
        runtimeTexture.wrapMode = TextureWrapMode.Clamp;
        runtimeTexture.anisoLevel = 0;
        pixels = runtimeTexture.GetPixels32();
        targetRawImage.texture = runtimeTexture;
        textureDirty = true;
        hasLastPoint = false;
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
            EraseCircle(currentPoint.x, currentPoint.y);
        }

        lastTexturePoint = currentPoint;
        hasLastPoint = true;
        textureDirty = true;
    }

    private void EraseAlongLine(Vector2Int from, Vector2Int to)
    {
        float distance = Vector2Int.Distance(from, to);
        int steps = Mathf.Max(1, Mathf.CeilToInt(distance / (EraserRadiusPixels * 0.35f)));

        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            int x = Mathf.RoundToInt(Mathf.Lerp(from.x, to.x, t));
            int y = Mathf.RoundToInt(Mathf.Lerp(from.y, to.y, t));
            EraseCircle(x, y);
        }
    }

    private void EraseCircle(int centerX, int centerY)
    {
        int radiusSquared = EraserRadiusPixels * EraserRadiusPixels;
        int minX = Mathf.Max(0, centerX - EraserRadiusPixels);
        int maxX = Mathf.Min(runtimeTexture.width - 1, centerX + EraserRadiusPixels);
        int minY = Mathf.Max(0, centerY - EraserRadiusPixels);
        int maxY = Mathf.Min(runtimeTexture.height - 1, centerY + EraserRadiusPixels);

        for (int y = minY; y <= maxY; y++)
        {
            int yDistance = y - centerY;
            int rowOffset = y * runtimeTexture.width;

            for (int x = minX; x <= maxX; x++)
            {
                int xDistance = x - centerX;
                if (xDistance * xDistance + yDistance * yDistance > radiusSquared)
                {
                    continue;
                }

                int index = rowOffset + x;
                pixels[index] = new Color32(0, 0, 0, 0);
            }
        }
    }
}
