using System;
using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Serviço simples para envio de SMS via smsdev.com.br.
/// - Usa JSON: { key, type: 9, number, msg }
/// - Lê configuração via ConfigManager automaticamente na inicialização; permite override via Initialize.
/// - Formatação básica para E.164 focada em Brasil (BR) sem dependências externas.
/// </summary>
public static class SmsService
{
    private static string apiUrl;
    private static string apiKey;

    // Inicialização estática: tenta ler as configs via ConfigManager
    static SmsService()
    {
        try
        {
            var cfg = new ConfigManager();
            var url = cfg.GetValue("SMS", "api_url");
            if (string.IsNullOrEmpty(url)) url = cfg.GetValue("SMS", "SMS_API_URL");
            if (string.IsNullOrEmpty(url)) url = cfg.GetValue("Config", "SMS_API_URL");

            var key = cfg.GetValue("SMS", "api_key");
            if (string.IsNullOrEmpty(key)) key = cfg.GetValue("SMS", "SMS_API_KEY");
            if (string.IsNullOrEmpty(key)) key = cfg.GetValue("Config", "SMS_API_KEY");

            apiUrl = url;
            apiKey = key;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[SmsService] Falha ao ler config: {ex.Message}");
        }
    }

    /// <summary>
    /// Override explícito das credenciais/endpoint da API de SMS.
    /// </summary>
    public static void Initialize(string apiUrl, string apiKey)
    {
        SmsService.apiUrl = apiUrl;
        SmsService.apiKey = apiKey;
    }

    /// <summary>
    /// Envia um SMS com mensagem livre.
    /// Uso: StartCoroutine(service.SendSmsMessageCoroutine(msg, phone, result => { ... }));
    /// </summary>
    public static IEnumerator SendSmsMessageCoroutine(string message, string destinationNumber, Action<bool> onComplete)
    {
        if (string.IsNullOrEmpty(apiUrl) || string.IsNullOrEmpty(apiKey))
        {
            Debug.LogError("[SmsService] API_URL ou API_KEY não configurados.");
            onComplete?.Invoke(false);
            yield break;
        }

        if (!TryFormatToE164(destinationNumber, "BR", out string formatted))
        {
            Debug.LogError($"[SmsService] Número inválido: '{destinationNumber}'");
            onComplete?.Invoke(false);
            yield break;
        }

        var payload = new SmsPayload
        {
            key = apiKey,
            type = 9,
            number = formatted,
            msg = message
        };

        string json = JsonUtility.ToJson(payload);
        using (UnityWebRequest req = new UnityWebRequest(apiUrl, UnityWebRequest.kHttpVerbPOST))
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
            bool ok = req.result == UnityWebRequest.Result.Success;
#else
            bool ok = !(req.isNetworkError || req.isHttpError);
#endif

            if (!ok)
            {
                Debug.LogError($"[SmsService] Falha ao enviar SMS: {req.error}");
                onComplete?.Invoke(false);
                yield break;
            }

            // A API retorna JSON com { status: "success" } em caso de sucesso
            var respText = req.downloadHandler.text;
            bool success = respText != null && respText.ToLowerInvariant().Contains("success");
            if (success)
            {
                Debug.Log($"[SmsService] SMS enviado para {formatted}");
            }
            else
            {
                Debug.LogWarning($"[SmsService] Resposta sem 'success': {respText}");
            }
            onComplete?.Invoke(success);
        }
    }

    /// <summary>
    /// Envia SMS com link de download.
    /// </summary>
    public static IEnumerator SendSmsDownloadMessageCoroutine(string messageUrl, string destinationNumber, Action<bool> onComplete)
    {
        string body = $"Sua imagem ficou pronta: \n{messageUrl}";
        yield return SendSmsMessageCoroutine(body, destinationNumber, onComplete);
    }

    /// <summary>
    /// Formata número para E.164. Implementação simplificada para BR (sem lib externa).
    /// Aceita formatos com símbolos, adiciona +55 quando apropriado.
    /// </summary>
    public static bool TryFormatToE164(string phoneNumber, string countryCode, out string formatted)
    {
        formatted = null;
        if (string.IsNullOrWhiteSpace(phoneNumber)) return false;

        string trimmed = phoneNumber.Trim();
        // Mantém + inicial se presente, remove demais não dígitos
        bool hasPlus = trimmed.StartsWith("+");
        string digits = Regex.Replace(trimmed, "[^0-9]", "");

        if (hasPlus)
        {
            // Se já tem + e dígitos suficientes, considera válido
            if (digits.Length >= 8)
            {
                formatted = "+" + digits;
                return true;
            }
            return false;
        }

        // Brasil (BR)
        if (string.Equals(countryCode, "BR", StringComparison.OrdinalIgnoreCase))
        {
            // Remove zeros à esquerda
            digits = digits.TrimStart('0');

            // Nacionais: 10 ou 11 dígitos (2 de DDD + 8/9 do número)
            if (digits.Length == 10 || digits.Length == 11)
            {
                formatted = "+55" + digits;
                return true;
            }

            // Já inclui 55
            if (digits.StartsWith("55") && (digits.Length == 12 || digits.Length == 13))
            {
                formatted = "+" + digits;
                return true;
            }

            return false;
        }

        // Fallback simples: se tiver 8+ dígitos, retorna com + e sem país
        if (digits.Length >= 8)
        {
            formatted = "+" + digits;
            return true;
        }
        return false;
    }

    [Serializable]
    private class SmsPayload
    {
        public string key;
        public int type;
        public string number;
        public string msg;
    }
}