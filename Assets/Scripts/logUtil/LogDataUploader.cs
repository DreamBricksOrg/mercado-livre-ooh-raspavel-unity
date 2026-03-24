using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

public class LogDataUploader : MonoBehaviour
{
    public string outputFolder;
    private string outputPath;
    private string backupPath;
    private string datalogFolder;

    private string uploadURL;
    public int checkIntervalSeconds;

    public int ALIVE_LOG_INTERVAL;

    private ConfigManager config;

    private static LogDataUploader Instance;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else 
        {
            Destroy(gameObject);
        }

        config = new();
        uploadURL = config.GetValue("Net", "dbutilsOld");
    }


    void Start()
    {
        datalogFolder = Path.Combine(Application.persistentDataPath, outputFolder);
        DataUploaderUtils.CheckIfDirectoryExists(datalogFolder);
        outputPath = DataUploaderUtils.EnsureCSVFilesExist(datalogFolder, "data_logs.csv");
        backupPath = DataUploaderUtils.EnsureCSVFilesExist(datalogFolder, "data_logs_backup.csv");
        StartCoroutine(Worker());

        StartCoroutine(SendAliveLogsPeriodically());
    }

    IEnumerator Worker()
    {
        while (true)
        {
            yield return new WaitForSeconds(checkIntervalSeconds);

            // check if internet is available
            if (!DataUploaderUtils.CheckForInternetConnection())
            {
                Debug.Log("no internet available");
                continue;
            }

            if (File.Exists(outputPath))
            {
                // L� todas as linhas do arquivo CSV
                string[] allLines = File.ReadAllLines(outputPath);
                string header = allLines[0]; // O cabe�alho � a primeira linha
                string[] lines = allLines.Skip(1).ToArray(); // Linhas de dados excluindo o cabe�alho

                // Verifica se h� linhas de dados no arquivo CSV
                if (lines.Length == 0)
                {
                    Debug.Log("O arquivo CSV est� vazio: " + outputPath);
                    continue;
                }

                List<string> updatedLines = new List<string>(lines);

                for (int i = 0; i < updatedLines.Count; i++)
                {
                    string line = updatedLines[i];
                    bool sendSuccess = false;

                    Debug.Log(string.Format("processing line '{0}' de '{1}' ", i + 1, updatedLines.Count));

                    yield return StartCoroutine(SendData(line, success => sendSuccess = success));

                    if (sendSuccess)
                    {

                        File.AppendAllText(backupPath, line + "\n");

                        // Remova a linha do arquivo original
                        updatedLines.RemoveAt(i);

                        // Reduza o valor de i para lidar com a remo��o da linha
                        i--;
                    }
                    else
                    {
                        Debug.LogWarning(string.Format("Falha ao enviar a linha '{0}', n�o ser� removida.", line));
                    }
                }

                // Escreva as linhas restantes de volta no arquivo CSV com o cabe�alho
                if (updatedLines.Count > 0)
                {
                    File.WriteAllLines(outputPath, new[] { header }.Concat(updatedLines).ToArray());
                }
                else
                {
                    // Se n�o houver linhas restantes, escreva apenas o cabe�alho
                    File.WriteAllLines(outputPath, new[] { header });
                }
            }
        }
    }

    virtual protected IEnumerator SendData(string line, Action<bool> callback)
    {

        // Crie um objeto WWWForm para armazenar o arquivo
        WWWForm form = new WWWForm();

        string[] columns = line.Split(',');

        DataLog dataLog = new DataLog();
        dataLog.timePlayed = columns[0];
        dataLog.status = columns[1];
        dataLog.project = columns[2];
        dataLog.additional = columns[3];

        form.AddField("timePlayed", dataLog.timePlayed);
        form.AddField("status", dataLog.status);
        form.AddField("project", dataLog.project);
        form.AddField("additional", dataLog.additional);

        // Crie uma requisicao UnityWebRequest para enviar o arquivo
        using (UnityWebRequest www = UnityWebRequest.Post(uploadURL, form))
        {
            yield return www.SendWebRequest(); // Envie a requisicao

            if (www.result == UnityWebRequest.Result.Success)
            {
                Debug.Log(string.Format("Arquivo '{0}' enviado com sucesso!", line));
                callback(true);
            }
            else
            {
                Debug.Log(string.Format("Erro ao enviar o arquivo '{0}': {1}", line, www.error));
                callback(false);
            }
        }
    }
    private IEnumerator SendAliveLogsPeriodically()
    {
        DataLog dataLogAux = new();
        yield return LogUtil.GetDatalogFromJsonCoroutine((dataLog) =>
        {
            dataLogAux = dataLog;
        });

        while (true)
        {
            // Aguarda o intervalo definido antes de enviar o próximo log
            yield return new WaitForSeconds(ALIVE_LOG_INTERVAL);
            
            if (dataLogAux != null)
            {
                // Atualiza o timestamp para o momento atual
                dataLogAux.timePlayed = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ssZ");
                dataLogAux.status = "ALIVE";
                dataLogAux.additional = "Heartbeat automático";

                string line = string.Format("{0},{1},{2},{3}",
                    dataLogAux.timePlayed,
                    dataLogAux.status,
                    dataLogAux.project,
                    dataLogAux.additional);

                // Aguarda a conclusão do envio antes de continuar o loop
                bool sendComplete = false;
                yield return SendData(line, success =>
                {
                    sendComplete = true;
                    if (success)
                    {
                        Debug.Log("Log ALIVE enviado: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    }
                });
                
                // Garante que o envio foi concluído antes de continuar
                while (!sendComplete)
                {
                    yield return null;
                }
            }
        }
    }
}
