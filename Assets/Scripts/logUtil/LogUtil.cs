using Newtonsoft.Json;
using System;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using System.Collections;

public class LogUtil : MonoBehaviour
{
    private static string logFilePath;

    public static void SaveLog(DataLog dataLog)
    {
        string folderPath = Path.Combine(Application.persistentDataPath, "datalogs_old");

        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        logFilePath = Path.Combine(folderPath, "data_logs.csv");

        if (!File.Exists(logFilePath))
        {
            using (StreamWriter writer = new StreamWriter(logFilePath))
            {
                writer.WriteLine("timePlayed,status,project,additional");
            }
        }

        string formattedDateTime = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ssZ");
        dataLog.timePlayed = formattedDateTime;

        string csvLine = string.Format("{0},{1},{2},{3}", dataLog.timePlayed, dataLog.status, dataLog.project, dataLog.additional);

        using (StreamWriter writer = File.AppendText(logFilePath))
        {
            writer.WriteLine(csvLine);
        }

        Debug.Log("Log saved at " + logFilePath);
    }

    public static IEnumerator GetDatalogFromJsonCoroutine(Action<DataLog> onComplete)
    {
        string jsonFileName = "datalog.json";
        string filePath = Path.Combine(Application.streamingAssetsPath, jsonFileName);

#if UNITY_ANDROID
        string uri = filePath;
#else
        string uri = "file://" + filePath;
#endif

        UnityWebRequest request = UnityWebRequest.Get(uri);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Failed to read datalog.json: " + request.error);
            onComplete?.Invoke(null);
        }
        else
        {
            string json = request.downloadHandler.text;
            DataLog dataLog = JsonConvert.DeserializeObject<DataLog>(json);
            onComplete?.Invoke(dataLog);
        }
    }
}
