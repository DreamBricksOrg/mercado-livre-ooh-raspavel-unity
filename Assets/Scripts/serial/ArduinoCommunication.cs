using UnityEngine;
using System.IO.Ports;
using System.Threading;
using System;

public class ArduinoCommunication : MonoBehaviour
{
    private ConfigManager config;
    private SerialPort serialPort;
    Thread receiverThread;
    public string port;
    public int baudrate;
    public static LockFreeQueue<string> myQueue;
    public bool startReceiving = true;
    public bool printToConsole = false;
    public string data;


    void Awake()
    {
        config = new();
        port = config.GetValue("Serial", "port", "COM5");
        baudrate = int.Parse(config.GetValue("Serial", "baudrate", "9600"));
    }

    void OnEnable()
    {
        serialPort = new SerialPort(port, baudrate);
        if (!serialPort.IsOpen)
        {
            serialPort.Open();
        }

        myQueue = new LockFreeQueue<string>();
        receiverThread = new Thread(
            new ThreadStart(ReceiveData));
        receiverThread.IsBackground = true;
        receiverThread.Start();
    }

    private void ReceiveData()
    {
        while (startReceiving)
        {

            if (serialPort.IsOpen)
            {
                try
                {
                    data = serialPort.ReadLine();
                    
                    myQueue.Enqueue(data);

                    if (printToConsole) { print(data); }

                }
                catch (Exception err)
                {
                    print(err.ToString());
                }
            }
        }
    }

    // returns the first item in the queue
    public string GetData()
    {
        if (myQueue.Empty()) return "";

        return myQueue.Dequeue();
    }


    // returns the last item in the queue
    // also clears the queue
    public string GetLastestData()
    {
        string result = "";
        string data = "";
        while ((data = GetData()) != "")
        {
            result = data;
        }

        return result;
    }

    // returns the last item in the queue
    // if it is newer than maxAge seconds
    public string GetLastestNewData(float maxAge)
    {
        //    if (GetCurrentTime() - timeLastDataReceived > maxAge)
        //        return "";
        //
        return GetLastestData();
    }

    public void SendMessageToArduino(string message)
    {
        if (serialPort.IsOpen)
        {
            serialPort.Write(message + "\n"); 
            Debug.Log("Sent to Arduino: " + message);
        }
        else
        {
            Debug.LogWarning("Serial port is not open!");
        }
    }

    void OnApplicationQuit()
    {
        if (serialPort != null && serialPort.IsOpen)
        {
            startReceiving = false;
            serialPort.Close();
        }
    }

    private void OnDisable()
    {
        if (serialPort != null && serialPort.IsOpen)
        {
            startReceiving = false;
            serialPort.Close();
        }
    }

    private void OnDestroy()
    {
        if (serialPort != null && serialPort.IsOpen)
        {
            startReceiving = false;
            serialPort.Close();
        }
    }
}