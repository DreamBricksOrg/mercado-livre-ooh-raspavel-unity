using UnityEngine;

public class AnimateComponent : MonoBehaviour
{
    public enum AnimationType
    {
        SlideFromLeft,
        SlideFromRight,
        SlideFromTop,
        SlideFromBottom
    }

    public enum SimpleEaseType
    {
        Linear,
        EaseOutQuad,
        EaseInQuad,
        EaseInOutQuad,
        EaseOutBack,
        EaseOutBounce
    }

    [Header("Animation Settings")]
    [Tooltip("Selecione o tipo de animação.")]
    public AnimationType animationType = AnimationType.SlideFromLeft;

    [Tooltip("Tempo de espera antes de iniciar a animação.")]
    public float delay = 0f;

    [Tooltip("Duração da animação.")]
    public float duration = 0.5f;

    [Tooltip("Curva de animação (Easing) simplificada.")]
    public SimpleEaseType easeType = SimpleEaseType.EaseOutQuad;

    private RectTransform _rectTransform;
    private Vector2 _originalPosition;
    private Canvas _parentCanvas;

    private void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
        
        if (_rectTransform == null)
        {
            Debug.LogError($"[AnimateComponent] RectTransform não encontrado em {gameObject.name}. Este script precisa de um RectTransform.");
            enabled = false;
            return;
        }

        // Armazena a posição original (do Editor/Start) para ser o alvo da animação
        _originalPosition = _rectTransform.anchoredPosition;

        // Busca o Canvas pai para referência de largura/altura
        _parentCanvas = GetComponentInParent<Canvas>();
    }

    private void OnEnable()
    {
        if (_rectTransform == null) return;

        // Cancela animações anteriores neste objeto para evitar conflitos
        LeanTween.cancel(gameObject);

        PlayAnimation();
    }

    private void PlayAnimation()
    {
        float windowWidth = GetWindowWidth();
        float windowHeight = GetWindowHeight();
        Vector2 startPosition = _originalPosition;

        switch (animationType)
        {
            case AnimationType.SlideFromLeft:
                startPosition.x = _originalPosition.x - windowWidth;
                break;

            case AnimationType.SlideFromRight:
                startPosition.x = _originalPosition.x + windowWidth;
                break;

            case AnimationType.SlideFromTop:
                startPosition.y = _originalPosition.y + windowHeight;
                break;

            case AnimationType.SlideFromBottom:
                startPosition.y = _originalPosition.y - windowHeight;
                break;
        }

        // Posiciona o objeto no local inicial (fora da tela)
        _rectTransform.anchoredPosition = startPosition;

        // Converte o tipo simples para o tipo do LeanTween
        LeanTweenType ltEase = GetLeanTweenType(easeType);

        // Anima de volta para a posição original
        LeanTween.move(_rectTransform, _originalPosition, duration)
            .setDelay(delay)
            .setEase(ltEase);
    }

    private LeanTweenType GetLeanTweenType(SimpleEaseType simpleType)
    {
        switch (simpleType)
        {
            case SimpleEaseType.Linear: return LeanTweenType.linear;
            case SimpleEaseType.EaseOutQuad: return LeanTweenType.easeOutQuad;
            case SimpleEaseType.EaseInQuad: return LeanTweenType.easeInQuad;
            case SimpleEaseType.EaseInOutQuad: return LeanTweenType.easeInOutQuad;
            case SimpleEaseType.EaseOutBack: return LeanTweenType.easeOutBack;
            case SimpleEaseType.EaseOutBounce: return LeanTweenType.easeOutBounce;
            default: return LeanTweenType.easeOutQuad;
        }
    }

    private float GetWindowWidth()
    {
        // Tenta pegar a largura do RectTransform do Canvas
        if (_parentCanvas != null)
        {
            RectTransform canvasRect = _parentCanvas.GetComponent<RectTransform>();
            if (canvasRect != null)
            {
                return canvasRect.rect.width;
            }
        }
        
        // Fallback para largura da tela se algo falhar
        return Screen.width;
    }

    private float GetWindowHeight()
    {
        // Tenta pegar a altura do RectTransform do Canvas
        if (_parentCanvas != null)
        {
            RectTransform canvasRect = _parentCanvas.GetComponent<RectTransform>();
            if (canvasRect != null)
            {
                return canvasRect.rect.height;
            }
        }
        
        // Fallback para altura da tela se algo falhar
        return Screen.height;
    }

#if UNITY_EDITOR
    private Vector2 _editorStartPosition;
    private Vector2 _editorTargetPosition;
    private double _editorAnimationStartTime;

    [ContextMenu("Preview Animation")]
    private void PreviewAnimation()
    {
        if (Application.isPlaying)
        {
            // Se estiver rodando, usa a lógica real
            if (gameObject.activeInHierarchy)
            {
                PlayAnimation();
            }
            return;
        }

        _rectTransform = GetComponent<RectTransform>();
        _parentCanvas = GetComponentInParent<Canvas>();

        if (_rectTransform == null)
        {
            Debug.LogWarning("RectTransform não encontrado.");
            return;
        }

        // Assume que a posição atual no editor é o alvo final
        _editorTargetPosition = _rectTransform.anchoredPosition;

        // Calcula posição inicial
        float windowWidth = GetWindowWidth();
        float windowHeight = GetWindowHeight();
        Vector2 startPos = _editorTargetPosition;

        switch (animationType)
        {
            case AnimationType.SlideFromLeft:
                startPos.x -= windowWidth;
                break;
            case AnimationType.SlideFromRight:
                startPos.x += windowWidth;
                break;
            case AnimationType.SlideFromTop:
                startPos.y += windowHeight;
                break;
            case AnimationType.SlideFromBottom:
                startPos.y -= windowHeight;
                break;
        }

        _editorStartPosition = startPos;
        
        // Posiciona no início
        _rectTransform.anchoredPosition = startPos;
        
        // Marca o tempo de início
        _editorAnimationStartTime = UnityEditor.EditorApplication.timeSinceStartup;

        // Registra o callback de update do editor
        UnityEditor.EditorApplication.update -= EditorUpdateAnimation;
        UnityEditor.EditorApplication.update += EditorUpdateAnimation;
    }

    [ContextMenu("Reset Position")]
    private void ResetPosition()
    {
        UnityEditor.EditorApplication.update -= EditorUpdateAnimation;
        if (_rectTransform != null)
        {
            _rectTransform.anchoredPosition = _editorTargetPosition;
        }
    }

    private void EditorUpdateAnimation()
    {
        if (_rectTransform == null)
        {
            UnityEditor.EditorApplication.update -= EditorUpdateAnimation;
            return;
        }

        double timeSinceStart = UnityEditor.EditorApplication.timeSinceStartup - _editorAnimationStartTime;

        // Aguarda delay
        if (timeSinceStart < delay) return;

        double timeInAnim = timeSinceStart - delay;
        float t = Mathf.Clamp01((float)(timeInAnim / duration));

        // Aplica o easing selecionado para o preview
        float tEased = EvaluateEasing(t, easeType);

        _rectTransform.anchoredPosition = Vector2.LerpUnclamped(_editorStartPosition, _editorTargetPosition, tEased);

        // Repinta a Scene View para ver a animação fluindo
        UnityEditor.SceneView.RepaintAll();

        if (timeInAnim >= duration)
        {
            _rectTransform.anchoredPosition = _editorTargetPosition;
            UnityEditor.EditorApplication.update -= EditorUpdateAnimation;
        }
    }

    // Simulação simples das curvas de easing para o Editor
    private float EvaluateEasing(float t, SimpleEaseType type)
    {
        switch (type)
        {
            case SimpleEaseType.Linear:
                return t;

            case SimpleEaseType.EaseOutQuad:
                return 1f - (1f - t) * (1f - t);

            case SimpleEaseType.EaseInQuad:
                return t * t;

            case SimpleEaseType.EaseInOutQuad:
                return t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f;

            case SimpleEaseType.EaseOutBack:
                float c1 = 1.70158f;
                float c3 = c1 + 1f;
                return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
            
            case SimpleEaseType.EaseOutBounce:
                // Aproximação do Bounce Out
                float n1 = 7.5625f;
                float d1 = 2.75f;
                if (t < 1f / d1) {
                    return n1 * t * t;
                } else if (t < 2f / d1) {
                    return n1 * (t -= 1.5f / d1) * t + 0.75f;
                } else if (t < 2.5f / d1) {
                    return n1 * (t -= 2.25f / d1) * t + 0.9375f;
                } else {
                    return n1 * (t -= 2.625f / d1) * t + 0.984375f;
                }

            default:
                return t;
        }
    }
    
    // Custom Editor para adicionar botões ao Inspector
    [UnityEditor.CustomEditor(typeof(AnimateComponent))]
    public class AnimateComponentEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            AnimateComponent script = (AnimateComponent)target;

            GUILayout.Space(10);
            GUILayout.Label("Editor Preview", UnityEditor.EditorStyles.boldLabel);

            GUILayout.BeginHorizontal();
            
            if (GUILayout.Button("Play Preview"))
            {
                script.PreviewAnimation();
            }

            if (GUILayout.Button("Reset"))
            {
                script.ResetPosition();
            }

            GUILayout.EndHorizontal();
        }
    }
#endif
}
