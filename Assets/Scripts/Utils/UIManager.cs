using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using UnityEngine.SceneManagement;
using System.Security.Cryptography.X509Certificates;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance;

    [System.Serializable]
    public class Screen
    {
        public string name;
        public GameObject screenObject;
        [HideInInspector] public CanvasGroup canvasGroup;
    }

    [Header("Screen Settings")]
    public List<Screen> screens;
    public string mainScreenName;
    
    [Header("Config Object")]
    public GameObject config;
    public RuntimeDebugConsole runtimeDebugConsole;

    [Header("Animation Settings")]
    [Range(0.1f, 2f)]
    public float fadeInDuration = 0.5f;
    [Range(0.1f, 2f)]
    public float fadeOutDuration = 0.3f;
    public LeanTweenType fadeInEase = LeanTweenType.easeOutQuart;
    public LeanTweenType fadeOutEase = LeanTweenType.easeInQuart;

    public Dictionary<string, GameObject> screenDictionary;
    public GameObject currentScreen;
    private bool isTransitioning = false;

    void Awake()
    {
        // Config inicia sempre desativado
        if (config != null)
        {
            config.SetActive(false);
        }

        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        screenDictionary = new Dictionary<string, GameObject>();
        foreach (var screen in screens)
        {
            screenDictionary.Add(screen.name, screen.screenObject);
            
            // Garantir que cada tela tenha um CanvasGroup
            screen.canvasGroup = screen.screenObject.GetComponent<CanvasGroup>();
            if (screen.canvasGroup == null)
            {
                screen.canvasGroup = screen.screenObject.AddComponent<CanvasGroup>();
            }
        }
    }

    void Start()
    {
        foreach (var screen in screens)
        {
            bool isMain = screen.name == mainScreenName;
            screen.screenObject.SetActive(isMain);
            if (isMain)
            {
                currentScreen = screen.screenObject;
                screen.canvasGroup.alpha = 1f;
                screen.canvasGroup.interactable = true;
                screen.canvasGroup.blocksRaycasts = true;
                
                // SaveLog(currentScreen.name);
            }
            else
            {
                screen.canvasGroup.alpha = 0f;
                screen.canvasGroup.interactable = false;
                screen.canvasGroup.blocksRaycasts = false;
            }
        }

        if (currentScreen == null && !string.IsNullOrEmpty(mainScreenName))
        {
            Debug.LogWarning($"Tela principal '{mainScreenName}' não encontrada na lista de telas.");
        }

        ResetPlayerPrerefs();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape) && config != null)
        {
            if (runtimeDebugConsole.visible == true)
                runtimeDebugConsole.visible = false;
            bool nextActive = !config.activeSelf;
            config.SetActive(nextActive);
        }
    }

    private void ResetPlayerPrerefs()
    {
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();
    }

    public void OpenScreen(string screenName)
    {
        if (isTransitioning)
        {
            Debug.LogWarning("Transição já em andamento. Aguarde a conclusão.");
            return;
        }

        if (screenDictionary.TryGetValue(screenName, out GameObject nextScreen))
        {
            if (currentScreen == nextScreen)
            {
                Debug.LogWarning($"A tela '{screenName}' já está ativa.");
                return;
            }

            StartCoroutine(TransitionToScreen(nextScreen, screenName));
        }
        else
        {
            Debug.LogWarning($"Screen '{screenName}' not found!");
        }
    }

    private IEnumerator TransitionToScreen(GameObject nextScreen, string screenName)
    {
        isTransitioning = true;

        // Obter CanvasGroup da tela atual e da próxima
        CanvasGroup currentCanvasGroup = currentScreen?.GetComponent<CanvasGroup>();
        CanvasGroup nextCanvasGroup = nextScreen.GetComponent<CanvasGroup>();

        // Ativar a próxima tela (mas invisível)
        nextScreen.SetActive(true);
        nextCanvasGroup.alpha = 0f;
        nextCanvasGroup.interactable = false;
        nextCanvasGroup.blocksRaycasts = false;

        // Fade out da tela atual (se existir)
        if (currentScreen != null && currentCanvasGroup != null)
        {
            yield return StartCoroutine(FadeOutScreen(currentCanvasGroup));
            currentScreen.SetActive(false);
        }

        // Fade in da nova tela
        yield return StartCoroutine(FadeInScreen(nextCanvasGroup));

        // Atualizar referência da tela atual
        currentScreen = nextScreen;
        isTransitioning = false;

        // SaveLog(currentScreen.name);
    }

    private IEnumerator FadeOutScreen(CanvasGroup canvasGroup)
    {
        // Desabilitar interações durante o fade out
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        // Animar alpha de 1 para 0
        LeanTween.alphaCanvas(canvasGroup, 0f, fadeOutDuration)
            .setEase(fadeOutEase);

        yield return new WaitForSeconds(fadeOutDuration);
    }

    private IEnumerator FadeInScreen(CanvasGroup canvasGroup)
    {
        // Animar alpha de 0 para 1
        LeanTween.alphaCanvas(canvasGroup, 1f, fadeInDuration)
            .setEase(fadeInEase)
            .setOnComplete(() => {
                // Habilitar interações após o fade in
                canvasGroup.interactable = true;
                canvasGroup.blocksRaycasts = true;
            });

        yield return new WaitForSeconds(fadeInDuration);
    }

    public void DisableScreen(string screenName)
    {
        if (screenDictionary.TryGetValue(screenName, out GameObject screenObj))
        {
            CanvasGroup canvasGroup = screenObj.GetComponent<CanvasGroup>();
            if (canvasGroup != null)
            {
                // Fade out antes de desabilitar
                LeanTween.alphaCanvas(canvasGroup, 0f, fadeOutDuration)
                    .setEase(fadeOutEase)
                    .setOnComplete(() => {
                        screenObj.SetActive(false);
                        canvasGroup.interactable = false;
                        canvasGroup.blocksRaycasts = false;
                    });
            }
            else
            {
                screenObj.SetActive(false);
            }
        }
        else
        {
            Debug.LogWarning($"Screen '{screenName}' not found for disable!");
        }
    }

    // Desabilita todas as telas imediatamente (sem animação)
    public void DisableAllScreens()
    {
        // Para quaisquer animações em andamento para evitar estados inconsistentes
        StopAllAnimations();

        foreach (var screen in screens)
        {
            if (screen?.screenObject == null) continue;

            var canvasGroup = screen.screenObject.GetComponent<CanvasGroup>();
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }

            screen.screenObject.SetActive(false);
        }

        currentScreen = null;
    }

    // Método para transição instantânea (sem animação) - útil para casos especiais
    public void OpenScreenInstant(string screenName)
    {
        if (currentScreen != null)
        {
            CanvasGroup currentCanvasGroup = currentScreen.GetComponent<CanvasGroup>();
            if (currentCanvasGroup != null)
            {
                currentCanvasGroup.alpha = 0f;
                currentCanvasGroup.interactable = false;
                currentCanvasGroup.blocksRaycasts = false;
            }
            currentScreen.SetActive(false);
        }

        if (screenDictionary.TryGetValue(screenName, out GameObject nextScreen))
        {
            nextScreen.SetActive(true);
            CanvasGroup nextCanvasGroup = nextScreen.GetComponent<CanvasGroup>();
            if (nextCanvasGroup != null)
            {
                nextCanvasGroup.alpha = 1f;
                nextCanvasGroup.interactable = true;
                nextCanvasGroup.blocksRaycasts = true;
            }
            currentScreen = nextScreen;
        }
        else
        {
            Debug.LogWarning($"Screen '{screenName}' not found!");
        }
    }

    void SaveLog(string screenName)
    {
        StartCoroutine(SaveLogCoroutine(screenName));
        StartCoroutine(LogSaver.SaveLog($"TOTEM_{screenName}", "INFO", new List<string> { "totem" }));
    }

    IEnumerator SaveLogCoroutine(string screenName)
    {
        yield return LogUtil.GetDatalogFromJsonCoroutine((dataLog) =>
        {
            if (dataLog != null)
            {
                dataLog.status = $"TOTEM_{screenName}";
                LogUtil.SaveLog(dataLog);
            }
            else
            {
                Debug.LogError("Erro ao carregar o DataLog do JSON.");
            }

        });
    }


    public void Restart()
    {
        SceneManager.LoadScene("SampleScene");
    }

    // Método para parar todas as animações em caso de necessidade
    public void StopAllAnimations()
    {
        LeanTween.cancelAll();
        isTransitioning = false;
    }

    public void LoadScene(string scene)
    {
        SceneManager.LoadScene(scene);
    }

    void OnDestroy()
    {
        StopAllAnimations();
    }
}


