using System;
using System.Collections;
using UnityEngine;

public class OperatingHoursToggle : MonoBehaviour
{
    [Header("Alvo")]
    [Tooltip("Tela / GameObject que será ligado/desligado conforme o horário.")]
    [SerializeField] private GameObject target;

    [Header("Horário de funcionamento (24h)")]
    [Tooltip("Hora de abertura (inclusiva). Ex.: 10 significa 10:00.")]
    [Range(0, 23)] public int openHour = 6;
    [Range(0, 59)] public int openMinute = 0;
    [Tooltip("Hora de fechamento (exclusiva). Ex.: 0:30 significa até 00:29:59.")]
    [Range(0, 23)] public int closeHour = 0;
    [Range(0, 59)] public int closeMinute = 30;

    [Header("Verificação")]
    [Tooltip("Intervalo (em segundos) entre checagens de horário.")]
    [Min(0.1f)] public float checkIntervalSeconds = 100f;

    [Header("Tecla de debug")]
    [Tooltip("Tecla para alternar o modo debug (fora do horário).")]
    public KeyCode debugKey = KeyCode.D;

    private bool debugOverrideActive = false;

    private ConfigManager config;

    private void Reset()
    {
        if (target == null) target = gameObject;
    }

    private void Awake()
    {
        config = new();
        openHour = int.Parse(config.GetValue("OOW", "OPEN_HOUR", "8"));
        openMinute = int.Parse(config.GetValue("OOW", "OPEN_MINUTE", "0"));
        closeHour = int.Parse(config.GetValue("OOW", "CLOSE_HOUR", "22"));
        closeMinute = int.Parse(config.GetValue("OOW", "CLOSE_MINUTE", "0"));


        if (target == null)
        {
            Debug.LogWarning($"{nameof(OperatingHoursToggle)}: 'target' não configurado. Usando o próprio GameObject.");
            target = gameObject;
        }
    }

    private void OnEnable()
    {
        ApplyRule(now: DateTime.Now);
        StartCoroutine(CheckLoop());
    }

    private void Update()
    {
        if (Input.GetKeyDown(debugKey))
        {
            bool within = IsWithinOperatingHours(DateTime.Now);
            if (!within)
            {
                debugOverrideActive = !debugOverrideActive;

                if (debugOverrideActive)
                {
                    ForceOff();
                    Debug.Log($"{nameof(OperatingHoursToggle)}: Modo DEBUG ativado. Lógica automática pausada e alvo desativado.");
                }
                else
                {
                    Debug.Log($"{nameof(OperatingHoursToggle)}: Modo DEBUG desativado. Lógica automática retomada.");
                    ApplyRule(now: DateTime.Now);
                }
            }
        }
    }

    private IEnumerator CheckLoop()
    {
        var wait = new WaitForSeconds(checkIntervalSeconds);

        while (enabled)
        {
            if (!debugOverrideActive)
            {
                ApplyRule(now: DateTime.Now);
            }
            yield return wait;
        }
    }

    /// <summary>
    /// Aplica a regra principal:
    /// - Entre openHour (incl.) e closeHour (excl.): target desativado
    /// - Fora: target ativado
    /// </summary>
    private void ApplyRule(DateTime now)
    {
        bool within = IsWithinOperatingHours(now);

        // Dentro do horário (ex.: 06:00–00:29:59): desativa target
        // Fora do horário: ativa target
        bool shouldBeActive = !within;

        if (target.activeSelf != shouldBeActive)
            target.SetActive(shouldBeActive);
    }

    /// <summary>
    /// Retorna true se o horário atual está dentro da janela de funcionamento.
    /// Suporta janelas que cruzam a meia-noite (ex.: 22–4), embora aqui não seja necessário.
    /// </summary>
    private bool IsWithinOperatingHours(DateTime now)
    {
        TimeSpan open = new TimeSpan(openHour, openMinute, 0);
        TimeSpan close = new TimeSpan(closeHour, closeMinute, 0);
        TimeSpan t = now.TimeOfDay;

        if (open == close)
        {
            // 24h aberto (mesma hora/minuto)
            return true;
        }

        if (open < close)
        {
            // Janela no mesmo dia (ex.: 06:00 -> 00:30)
            return t >= open && t < close;
        }
        else
        {
            // Janela cruzando a meia-noite (ex.: 22:00 -> 04:30)
            return t >= open || t < close;
        }
    }

    /// <summary>
    /// Força o alvo a ficar desativado (usado no modo debug).
    /// </summary>
    private void ForceOff()
    {
        if (target.activeSelf)
            target.SetActive(false);
    }

    // Opcional: helper público para alterar horário em runtime
    public void SetOperatingHours(int openHour24, int closeHour24)
    {
        openHour = Mathf.Clamp(openHour24, 0, 23);
        openMinute = 0;
        closeHour = Mathf.Clamp(closeHour24, 0, 23);
        closeMinute = 0;
        if (!debugOverrideActive)
            ApplyRule(DateTime.Now);
    }

    // Novo helper para definir hora/minuto
    public void SetOperatingHours(int openHour24, int openMin, int closeHour24, int closeMin)
    {
        openHour = Mathf.Clamp(openHour24, 0, 23);
        openMinute = Mathf.Clamp(openMin, 0, 59);
        closeHour = Mathf.Clamp(closeHour24, 0, 23);
        closeMinute = Mathf.Clamp(closeMin, 0, 59);
        if (!debugOverrideActive)
            ApplyRule(DateTime.Now);
    }
}
