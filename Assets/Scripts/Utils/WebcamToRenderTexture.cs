using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections;

[DisallowMultipleComponent]
public class WebcamToRenderTexture : MonoBehaviour
{
    [Header("Dispositivo")]
    [Tooltip("Nome do dispositivo de câmera. Deixe vazio para usar o primeiro.")]
    public string deviceName;
    [Tooltip("Índice do dispositivo, usado se deviceName estiver vazio.")]
    public int deviceIndex = 0;

    [Header("Parâmetros de captura")]
    public int requestedWidth = 720;
    public int requestedHeight = 1280;
    public int requestedFPS = 30;

    public enum RotationMode { None, Rotate90, Rotate180, Rotate270 }
    [Header("Orientação")]
    [Tooltip("Rotação a aplicar no frame.")]
    public RotationMode rotation;
    [Tooltip("Espelhar horizontalmente (flip X).")]
    public bool flipHorizontal = false;
    [Tooltip("Espelhar verticalmente (flip Y).")]
    public bool flipVertical = false;
    [Tooltip("Inicializar rotação/espelhamento baseado nos metadados da webcam.")]
    public bool useWebcamOrientationMetadata = true;
    [Tooltip("Usar shader para rotacionar/flipar pixels (GPU). Se falso, rotaciona/flip via Transform do consumidor.")]
    public bool useShaderRotation = true;

    [Header("Saída")]
    [Tooltip("RenderTexture alvo onde os frames da webcam serão escritos.")]
    public RenderTexture target;
    [Tooltip("Criar automaticamente um RenderTexture se não for atribuído.")]
    public bool autoCreateTarget = true;

    [Tooltip("Aplicar rotação/flip no Transform do consumidor ao invés de shader.")]
    public bool rotateConsumerTransform = false;
    [Tooltip("Transform que exibe o RenderTexture (RawImage, Quad, etc.). Se vazio, usa o próprio Transform.")]
    public Transform consumerTransform;

    [Tooltip("Iniciar a captura automaticamente no Start().")]
    public bool playOnStart = true;

    private WebCamTexture webcamTexture;
    private Material blitMaterial;
    private bool ownsTarget = false;
    private Vector3 originalConsumerScale = Vector3.one;
    private ConfigManager config;

    [Header("Shader")]
    [Tooltip("Referência ao shader de rotação/flip para garantir inclusão no build.")]
    [SerializeField] private Shader rotateBlitShader;

    [Header("Config")]
    [Tooltip("Usar rotação vinda de StreamingAssets/config.ini -> [Cam] Rotation.")]
    public bool useRotationFromConfig = true;

    private string val;

    void Awake()
    {
        ApplyCamSettingsFromConfig();

        // Carrega rotação do config.ini o mais cedo possível
        if (useRotationFromConfig)
        {
            ApplyRotationFromConfig();
        }
    }

    void Start()
    {
        // Primeiro, aplica configurações vindas do config.ini (garante que a webcam
        // seja inicializada com os parâmetros corretos no build).
        ApplyCamSettingsFromConfig();
        if (useRotationFromConfig)
        {
            ApplyRotationFromConfig();
        }

        if (consumerTransform == null)
        {
            consumerTransform = transform;
        }
        if (consumerTransform != null)
        {
            originalConsumerScale = consumerTransform.localScale;
        }

        if (playOnStart)
        {
            InitializeAndPlay();
        }
    }

    /// <summary>
    /// Inicializa a webcam e inicia a captura.
    /// </summary>
    public void InitializeAndPlay()
    {
        if (webcamTexture != null && webcamTexture.isPlaying) return;

        var devices = WebCamTexture.devices;
        if (devices == null || devices.Length == 0)
        {
            Debug.LogWarning("[WebcamToRenderTexture] Nenhuma webcam encontrada.");
            return;
        }

        string selectedName = deviceName;
        if (string.IsNullOrEmpty(selectedName))
        {
            deviceIndex = Mathf.Clamp(deviceIndex, 0, devices.Length - 1);
            selectedName = devices[deviceIndex].name;
        }

        webcamTexture = new WebCamTexture(selectedName, requestedWidth, requestedHeight, requestedFPS);
        webcamTexture.Play();

        // Define orientação inicial a partir dos metadados (se habilitado e não usando config)
        if (useWebcamOrientationMetadata && !useRotationFromConfig)
        {
            ApplyInitialOrientationFromMetadata();
        }
    }

    void Update()
    {
        if (webcamTexture == null || !webcamTexture.isPlaying) return;
        if (!webcamTexture.didUpdateThisFrame) return;

        EnsureTargetSize();

        if (target != null)
        {
            if (useShaderRotation)
            {
                // Material para aplicar rotação/flip no blit
                if (blitMaterial == null)
                {
                    // Tenta carregar de referência serializada, depois Resources, depois Shader.Find
                    var shader = rotateBlitShader
                                 ?? Resources.Load<Shader>("WebcamRotateBlit")
                                 ?? Shader.Find("Hidden/WebcamRotateBlit");
                    if (shader == null)
                    {
                        Debug.LogWarning("[WebcamToRenderTexture] Shader de rotação/flip não encontrado; aplicando blit sem rotação.");
                        Graphics.Blit(webcamTexture, target);
                        return;
                    }
                    blitMaterial = new Material(shader);
                }

                blitMaterial.SetFloat("_Rotation", GetRotationDegrees());
                blitMaterial.SetFloat("_FlipX", flipHorizontal ? 1f : 0f);
                blitMaterial.SetFloat("_FlipY", flipVertical ? 1f : 0f);

                Graphics.Blit(webcamTexture, target, blitMaterial);
            }
            else
            {
                // Sem shader: apenas copia pixels como estão
                Graphics.Blit(webcamTexture, target);
            }
        }

        // Aplica rotação/flip ao Transform do consumidor, se configurado
        if (rotateConsumerTransform && consumerTransform != null)
        {
            ApplyOrientationToConsumerTransform();
        }
    }

    /// <summary>
    /// Garante que o RenderTexture existe e tem o mesmo tamanho do feed da webcam.
    /// </summary>
    private void EnsureTargetSize()
    {
        // Em muitos dispositivos, width/height retornam valores pequenos até a primeira atualização real (~16x16).
        if (webcamTexture.width <= 16 || webcamTexture.height <= 16)
        {
            return; // Aguarda dimensões válidas.
        }

        // Quando rotacionado 90/270 com shader, a saída ideal troca largura/altura
        int outWidth = webcamTexture.width;
        int outHeight = webcamTexture.height;
        int rot = GetRotationDegrees();
        if (useShaderRotation && (rot == 90 || rot == 270))
        {
            outWidth = webcamTexture.height;
            outHeight = webcamTexture.width;
        }

        if (target == null && autoCreateTarget)
        {
            target = new RenderTexture(outWidth, outHeight, 0, RenderTextureFormat.ARGB32)
            {
                useDynamicScale = false
            };
            ownsTarget = true;
        }
        else if (target != null)
        {
            if (target.width != outWidth || target.height != outHeight)
            {
                if (ownsTarget)
                {
                    target.Release();
                    Destroy(target);
                    target = new RenderTexture(outWidth, outHeight, 0, RenderTextureFormat.ARGB32)
                    {
                        useDynamicScale = false
                    };
                }
            }
        }
    }

    /// <summary>
    /// Para a captura da webcam.
    /// </summary>
    public void Stop()
    {
        if (webcamTexture != null)
        {
            webcamTexture.Stop();
        }
    }

    void OnDisable()
    {
        Stop();
    }

    void OnDestroy()
    {
        Stop();
        if (ownsTarget && target != null)
        {
            target.Release();
            Destroy(target);
            target = null;
        }

        if (blitMaterial != null)
        {
            Destroy(blitMaterial);
            blitMaterial = null;
        }
    }

    /// <summary>
    /// Retorna o RenderTexture de saída.
    /// </summary>
    public RenderTexture GetOutput()
    {
        return target;
    }

    private void ApplyInitialOrientationFromMetadata()
    {
        // Alguns drivers informam ângulos de 0/90/180/270
        int angle = webcamTexture.videoRotationAngle;

        

        switch (angle)
        {
            case 90: rotation = RotationMode.Rotate90; break;
            case 180: rotation = RotationMode.Rotate180; break;
            case 270: rotation = RotationMode.Rotate270; break;
            default: rotation = RotationMode.None; break;
        }

        // Espelhamento vertical conforme metadados
        flipVertical = webcamTexture.videoVerticallyMirrored;
    }

    private int GetRotationDegrees()
    {
        switch (rotation)
        {
            case RotationMode.Rotate90: return 90;
            case RotationMode.Rotate180: return 180;
            case RotationMode.Rotate270: return 270;
            default: return 0;
        }
    }

    private void ApplyRotationFromConfig()
    {
        try
        {
            if (config == null) config = new ConfigManager();
            string val = config.GetValue("Cam", "rotation");
            if (string.IsNullOrEmpty(val))
            {
                val = config.GetValue("Cam", "Rotation");
            }
            Debug.Log($"[WebcamToRenderTexture] rotation key raw value: '{val}'");
            if (string.IsNullOrEmpty(val))
            {
                val = ReadIniValueDirect("Cam", "rotation");
                if (string.IsNullOrEmpty(val))
                {
                    val = ReadIniValueDirect("Cam", "Rotation");
                }
                Debug.Log($"[WebcamToRenderTexture] rotation key raw value (fallback): '{val}'");
            }
            if (!string.IsNullOrEmpty(val) && int.TryParse(val, out int rotationValue))
            {
                switch (rotationValue)
                {
                    case 90: rotation = RotationMode.Rotate90; break;
                    case 180: rotation = RotationMode.Rotate180; break;
                    case 270: rotation = RotationMode.Rotate270; break;
                    default: rotation = RotationMode.None; break;
                }
                Debug.Log($"[WebcamToRenderTexture] Rotação aplicada via config.ini: {rotationValue} graus.");
            }
            else
            {
                Debug.LogWarning("[WebcamToRenderTexture] Chave 'rotation/Rotation' ausente ou inválida no config.ini; mantendo configuração do Inspector.");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[WebcamToRenderTexture] Falha ao ler rotação do config.ini: {ex.Message}");
        }
    }

    private void ApplyCamSettingsFromConfig()
    {
        try
        {
            if (config == null) config = new ConfigManager();

            // deviceIndex
            string di = config.GetValue("Cam", "deviceIndex");
            if (string.IsNullOrEmpty(di)) di = config.GetValue("Cam", "DeviceIndex");
            if (string.IsNullOrEmpty(di))
            {
                di = ReadIniValueDirect("Cam", "deviceIndex") ?? ReadIniValueDirect("Cam", "DeviceIndex");
            }
            if (!string.IsNullOrEmpty(di) && int.TryParse(di, out int deviceIdx))
            {
                deviceIndex = deviceIdx;
            }

            // width
            string w = config.GetValue("Cam", "width");
            if (string.IsNullOrEmpty(w)) w = config.GetValue("Cam", "Width");
            if (string.IsNullOrEmpty(w))
            {
                w = ReadIniValueDirect("Cam", "width") ?? ReadIniValueDirect("Cam", "Width");
            }
            if (!string.IsNullOrEmpty(w) && int.TryParse(w, out int widthVal))
            {
                requestedWidth = widthVal;
            }

            // height
            string h = config.GetValue("Cam", "height");
            if (string.IsNullOrEmpty(h)) h = config.GetValue("Cam", "Height");
            if (string.IsNullOrEmpty(h))
            {
                h = ReadIniValueDirect("Cam", "height") ?? ReadIniValueDirect("Cam", "Height");
            }
            if (!string.IsNullOrEmpty(h) && int.TryParse(h, out int heightVal))
            {
                requestedHeight = heightVal;
            }

            // fps
            string f = config.GetValue("Cam", "fps");
            if (string.IsNullOrEmpty(f)) f = config.GetValue("Cam", "FPS");
            if (string.IsNullOrEmpty(f))
            {
                f = ReadIniValueDirect("Cam", "fps") ?? ReadIniValueDirect("Cam", "FPS");
            }
            if (!string.IsNullOrEmpty(f) && int.TryParse(f, out int fpsVal))
            {
                requestedFPS = fpsVal;
            }

            // flipHorizontal
            string fh = config.GetValue("Cam", "flipHorizontal");
            if (string.IsNullOrEmpty(fh)) fh = config.GetValue("Cam", "FlipHorizontal");
            if (string.IsNullOrEmpty(fh))
            {
                fh = ReadIniValueDirect("Cam", "flipHorizontal") ?? ReadIniValueDirect("Cam", "FlipHorizontal") ?? ReadIniValueDirect("Cam", "flipX") ?? ReadIniValueDirect("Cam", "FlipX");
            }
            if (TryParseBoolFlexible(fh, out bool fhVal))
            {
                flipHorizontal = fhVal;
            }

            // flipVertical
            string fv = config.GetValue("Cam", "flipVertical");
            if (string.IsNullOrEmpty(fv)) fv = config.GetValue("Cam", "FlipVertical");
            if (string.IsNullOrEmpty(fv))
            {
                fv = ReadIniValueDirect("Cam", "flipVertical") ?? ReadIniValueDirect("Cam", "FlipVertical") ?? ReadIniValueDirect("Cam", "flipY") ?? ReadIniValueDirect("Cam", "FlipY");
            }
            if (TryParseBoolFlexible(fv, out bool fvVal))
            {
                flipVertical = fvVal;
            }

            Debug.Log($"[WebcamToRenderTexture] Config aplicada: deviceIndex={deviceIndex}, size={requestedWidth}x{requestedHeight}, fps={requestedFPS}, flipX={flipHorizontal}, flipY={flipVertical}");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[WebcamToRenderTexture] Falha ao ler parâmetros da câmera do config.ini: {ex.Message}");
        }
    }

    private bool TryParseBoolFlexible(string s, out bool value)
    {
        value = false;
        if (string.IsNullOrEmpty(s)) return false;
        var t = s.Trim().ToLowerInvariant();
        if (t == "true" || t == "1" || t == "yes" || t == "on") { value = true; return true; }
        if (t == "false" || t == "0" || t == "no" || t == "off") { value = false; return true; }
        return false;
    }

    private string ReadIniValueDirect(string section, string key)
    {
        try
        {
            var path = System.IO.Path.Combine(Application.streamingAssetsPath, "config.ini");
            if (!System.IO.File.Exists(path))
            {
                Debug.LogWarning($"[WebcamToRenderTexture] config.ini não encontrado em: {path}");
                return null;
            }

            string currentSection = null;
            foreach (var line in System.IO.File.ReadAllLines(path))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#") || trimmed.StartsWith(";"))
                    continue;

                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    currentSection = trimmed.Substring(1, trimmed.Length - 2).Trim();
                    continue;
                }

                int eqIdx = trimmed.IndexOf('=');
                if (eqIdx <= 0) continue;
                string k = trimmed.Substring(0, eqIdx).Trim();
                string v = trimmed.Substring(eqIdx + 1).Trim();

                if (!string.IsNullOrEmpty(currentSection) &&
                    string.Equals(currentSection, section, System.StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(k, key, System.StringComparison.OrdinalIgnoreCase))
                {
                    return v;
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[WebcamToRenderTexture] Fallback de leitura ini falhou: {ex.Message}");
        }
        return null;
    }

    private void ApplyOrientationToConsumerTransform()
    {
        // Rotação no eixo Z para UI/quad
        int rot = GetRotationDegrees();
        var euler = consumerTransform.localEulerAngles;
        euler.z = rot;
        consumerTransform.localEulerAngles = euler;

        // Flip via escala negativa
        float sx = flipHorizontal ? -Mathf.Abs(originalConsumerScale.x) : Mathf.Abs(originalConsumerScale.x);
        float sy = flipVertical ? -Mathf.Abs(originalConsumerScale.y) : Mathf.Abs(originalConsumerScale.y);
        consumerTransform.localScale = new Vector3(sx, sy, originalConsumerScale.z);
    }
}