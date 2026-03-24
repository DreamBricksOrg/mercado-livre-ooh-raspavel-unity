using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Collections;
using System.Runtime.CompilerServices;

public class MANUTENCAO : MonoBehaviour
{
    // Singleton
    public static MANUTENCAO Instance { get; private set; }

    [Header("Tela de manutenção")]
    [Tooltip("GameObject que contém a imagem da tela de manutenção.")]
    [SerializeField] public GameObject maintenanceScreen;
    [Tooltip("Tecla para ativar a tela de manutenção.")]
    [SerializeField] private KeyCode triggerKey = KeyCode.M;

    public string ctaScreen;

    [Header("Monitoramento")]
    [Tooltip("URL do servidor para verificar disponibilidade.")]
    [SerializeField] private string serverUrl;
    [Tooltip("Intervalo (segundos) entre verificações de status do servidor.")]
    [Min(0.5f)] public float pollIntervalSeconds = 5f;
    [Tooltip("Timeout (segundos) para cada requisição de verificação.")]
    [Range(1, 60)] public int requestTimeoutSeconds = 5;
    [Tooltip("Número de respostas 400 consecutivas para entrar em manutenção.")]
    [Min(1)] public int badRequestThreshold = 2;
    public bool isMaintenanceHeldActive = false;

    private Coroutine pollingRoutine;

    private ConfigManager cfg;

    public bool isMaintEnable = false;
    private int consecutiveBadRequestCount = 0;


    private void Awake()
    {
        Instance = this;

        cfg = new();
        string maintStr = null;
        maintStr = cfg.GetValue("Config", "isMaintEnable");
        serverUrl = cfg.GetValue("Net", "aliveServer", "/alive");
        if (string.IsNullOrEmpty(maintStr)) maintStr = cfg.GetValue("Config", "IsMaintEnable");
        if (TryParseBoolFlexible(maintStr, out bool parsedMaintEnable))
        {
            this.isMaintEnable = parsedMaintEnable;
        }

        gameObject.SetActive(isMaintEnable);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnEnable()
    {
        StartMonitoring();
    }

    private void OnDisable()
    {
        StopMonitoring();
    }

    void Update()
    {
        EnableMaintenanceScreen();
    }

    void EnableMaintenanceScreen()
    {
        if (Input.GetKeyDown(triggerKey))
        {
            isMaintenanceHeldActive = !isMaintenanceHeldActive;
            if (maintenanceScreen == null)
            {
                Debug.LogWarning("[MANUTENCAO] 'maintenanceScreen' não atribuído no Inspector.");
                return;
            }

            bool isActive = maintenanceScreen.activeSelf;
            var cg = maintenanceScreen.GetComponent<CanvasGroup>();

            if (isActive)
            {
                // Centraliza a desativação da tela de manutenção
                DisableMaintenanceScreen();

                if (UIManager.Instance != null)
                {
                    UIManager.Instance.OpenScreen(ctaScreen);
                }
                else
                {
                    Debug.LogWarning("[MANUTENCAO] UIManager.Instance não disponível para abrir CTA.");
                }
            }
            else
            {
                // Ativa manutenção e desabilita todas as telas
                if (UIManager.Instance != null)
                {
                    UIManager.Instance.DisableAllScreens();
                }

                maintenanceScreen.SetActive(true);
                if (cg != null)
                {
                    cg.alpha = 1f;
                    cg.interactable = true;
                    cg.blocksRaycasts = true;
                }
            }
        }
    }

    // Inicia monitoramento contínuo do servidor
    public void StartMonitoring()
    {
        if (pollingRoutine == null)
        {
            pollingRoutine = StartCoroutine(PollingLoop());
            Debug.Log($"[MANUTENCAO] Monitoramento iniciado. Intervalo={pollIntervalSeconds}s, Timeout={requestTimeoutSeconds}s");
        }
    }

    // Para monitoramento contínuo
    public void StopMonitoring()
    {
        if (pollingRoutine != null)
        {
            StopCoroutine(pollingRoutine);
            pollingRoutine = null;
            Debug.Log("[MANUTENCAO] Monitoramento parado.");
        }
    }

    private IEnumerator PollingLoop()
    {
        while (enabled)
        {
            yield return CheckServerStatusOnce();
            yield return new WaitForSeconds(pollIntervalSeconds);
        }
    }

    private IEnumerator CheckServerStatusOnce()
    {
        using (var uwr = UnityWebRequest.Get(serverUrl))
        {
            // Ainda setamos timeout do UWR (nem sempre respeitado). Implementamos timeout manual abaixo.
            uwr.timeout = requestTimeoutSeconds;
            Debug.Log($"[MANUTENCAO] Verificando servidor: {serverUrl}");

            var op = uwr.SendWebRequest();
            float start = Time.time;
            while (!op.isDone)
            {
                if (Time.time - start > requestTimeoutSeconds)
                {
                    Debug.LogWarning($"[MANUTENCAO] Timeout atingido ({requestTimeoutSeconds}s) ao verificar servidor. Abortando requisição.");
                    uwr.Abort();
                    break;
                }
                yield return null;
            }

            if (uwr.result != UnityWebRequest.Result.Success)
            {
                // Tratar falhas (inclui timeout, DNS, conexão recusada, 5xx sob certas condições)
                Debug.LogWarning($"[MANUTENCAO] Falha ao verificar servidor: result={uwr.result}, erro='{uwr.error}', code={uwr.responseCode}. Entrar em manutenção se CTA ativa.");
                if (IsCTAActive())
                {
                    ActivateMaintenance();
                }
                else
                {
                    Debug.Log("[MANUTENCAO] Falha de verificação. Já em manutenção, mantendo estado.");
                }
                yield break;
            }

            var code = uwr.responseCode;
            Debug.Log($"[MANUTENCAO] Resposta do servidor: HTTP {code}");

            if (code == 200)
            {
                // Reset de contagem ao receber sucesso
                consecutiveBadRequestCount = 0;
                if (IsMaintenanceActive())
                {
                    Debug.Log("[MANUTENCAO] Servidor OK (200). Saindo de manutenção e voltando para CTA.");
                    if (isMaintenanceHeldActive)
                    {
                        Debug.Log("[MANUTENCAO] Ignorando ação de sair de manutenção enquanto segurado.");
                    }
                    else
                    {
                        ActivateCTA();
                    }
                }
                else
                {
                    Debug.Log("[MANUTENCAO] Servidor OK (200). CTA já ativa, nenhuma ação.");
                }
            }
            else if (code == 400)
            {
                // Incrementa e só entra em manutenção após atingir o limiar
                consecutiveBadRequestCount++;
                Debug.Log($"[MANUTENCAO] Servidor 400. Contagem consecutiva={consecutiveBadRequestCount}/{badRequestThreshold}.");
                if (consecutiveBadRequestCount >= badRequestThreshold)
                {
                    consecutiveBadRequestCount = 0; // reseta após acionar
                    if (IsCTAActive())
                    {
                        Debug.Log("[MANUTENCAO] Limiar de 400 consecutivos atingido. Entrando em manutenção e desativando CTA.");
                        ActivateMaintenance();
                    }
                    else
                    {
                        Debug.Log("[MANUTENCAO] Limiar atingido, mas já em manutenção; mantendo estado.");
                    }
                }
                else
                {
                    // Abaixo do limiar: manter estado atual
                    Debug.Log("[MANUTENCAO] Aguardando próxima verificação para confirmar manutenção.");
                }
            }
            else
            {
                // Qualquer outro código diferente de 200/400 reseta contagem
                consecutiveBadRequestCount = 0;
                Debug.LogWarning($"[MANUTENCAO] HTTP {code} não tratado explicitamente. Mantendo estado.");
            }
        }
    }

    private bool IsMaintenanceActive()
    {
        return maintenanceScreen != null && maintenanceScreen.activeSelf;
    }

    // Exposição pública do estado de hold, utilizada por outros scripts
    public bool IsMaintenanceHeldActive()
    {
        return isMaintenanceHeldActive && IsMaintenanceActive();
    }

    private bool IsCTAActive()
    {
        var ui = UIManager.Instance;
        if (ui == null) return false;
        if (ui.currentScreen != null)
        {
            return ui.currentScreen.name == ctaScreen;
        }
        var ctaGo = GetCtaGO();
        return ctaGo != null && ctaGo.activeSelf;
    }

    private GameObject GetCtaGO()
    {
        var ui = UIManager.Instance;
        if (ui != null && ui.screenDictionary != null)
        {
            if (ui.screenDictionary.TryGetValue(ctaScreen, out var go))
            {
                return go;
            }
        }
        return null;
    }

    public void ActivateMaintenance()
    {
        var ui = UIManager.Instance;
        if (ui != null)
        {
            ui.DisableAllScreens();
        }

        if (maintenanceScreen != null)
        {
            maintenanceScreen.SetActive(true);
            var cg = maintenanceScreen.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.alpha = 1f;
                cg.interactable = true;
                cg.blocksRaycasts = true;
            }
        }
        else
        {
            Debug.LogWarning("[MANUTENCAO] 'maintenanceScreen' não atribuído ao ativar manutenção.");
        }
    }

    // Ativa manutenção com retenção (hold): mantém a tela de manutenção ativa e impede saídas automáticas
    public void ActivateMaintenanceHold()
    {
        // Se já está em hold e a tela está ativa, evita trabalho desnecessário
        if (isMaintenanceHeldActive && IsMaintenanceActive())
        {
            Debug.Log("[MANUTENCAO] Manutenção hold já ativa; nenhuma ação necessária.");
            return;
        }

        isMaintenanceHeldActive = true;
        Debug.Log("[MANUTENCAO] Hold de manutenção ativado.");

        // Desabilita demais telas; UIManager já preserva manutenção quando hold está ativo
        var ui = UIManager.Instance;
        if (ui != null)
        {
            ui.DisableAllScreens();
        }

        // Garante que a tela de manutenção está visível e interativa
        if (maintenanceScreen != null)
        {
            maintenanceScreen.SetActive(true);
            var cg = maintenanceScreen.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.alpha = 1f;
                cg.interactable = true;
                cg.blocksRaycasts = true;
            }
        }
        else
        {
            Debug.LogWarning("[MANUTENCAO] 'maintenanceScreen' não atribuído ao ativar manutenção hold.");
        }
    }

    private void ActivateCTA()
    {
        // Centraliza a desativação da manutenção
        DisableMaintenanceScreen();

        var ui = UIManager.Instance;
        if (ui != null)
        {
            ui.OpenScreen(ctaScreen);
        }
        else
        {
            Debug.LogWarning("[MANUTENCAO] UIManager.Instance não disponível para abrir CTA.");
        }
    }

    private void DisableMaintenanceScreen()
    {
        if (maintenanceScreen == null)
        {
            Debug.LogWarning("[MANUTENCAO] 'maintenanceScreen' não atribuído ao desativar manutenção.");
            return;
        }

        var cg = maintenanceScreen.GetComponent<CanvasGroup>();
        if (cg != null)
        {
            cg.alpha = 0f;
            cg.interactable = false;
            cg.blocksRaycasts = false;
        }

        maintenanceScreen.SetActive(false);
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
}
