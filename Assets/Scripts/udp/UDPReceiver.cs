using UnityEngine;
using System;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;


public class UDPReceiver : MonoBehaviour
{
    public static UDPReceiver Instance;
    Thread receiverThread;
    UdpClient client;
    public int port;
    public bool startReceiving = true;
    public bool printToConsole = true;
    public string data;
    public static LockFreeQueue<string> myQueue;
    private ConfigManager config;
    private string udpPort;


    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        config = new();
        udpPort = config.GetValue("Net", "udpReceiverPort");
        if (!int.TryParse(udpPort, out port))
        {
            port = 8000;
            Debug.LogWarning($"[UDPReceiver] Porta inválida em config: '{udpPort}'. Usando {port}.");
        }
        myQueue = new LockFreeQueue<string>();
    }

    public void Start()
    {
        receiverThread = new Thread(
            new ThreadStart(ReceiveData));
        receiverThread.IsBackground = true;
        receiverThread.Start();
    }


    private void ReceiveData()
    {
        client = new UdpClient(port);
        while (startReceiving)
        {
            try
            {
                IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, 0);
                byte[] dataByte = client.Receive(ref anyIP);
                data = Encoding.UTF8.GetString(dataByte);
                myQueue.Enqueue(data);

                if (printToConsole) { print(data); }
            }
            catch (Exception err)
            {
                print(err.ToString());
            }
        }
    }

    public string GetData()
    {
        if (myQueue == null) return "";
        if (myQueue.Empty()) return "";

        return myQueue.Dequeue();
    }

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

    private void OnDisable()
    {
        if (receiverThread != null)
        {
            receiverThread.Abort();
            receiverThread = null;
        }
        if (client != null)
        {
            client.Close();
            client = null;
        }
    }
}
