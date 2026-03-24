using UnityEngine;
using System;
using System.Text;
using System.Net;
using System.Net.Sockets;

public class UDPSender : MonoBehaviour
{
    private UdpClient client;
    public string remoteIP = "127.0.0.1";
    public int remotePort = 8000;
    private IPEndPoint remoteEndPoint;
    private ConfigManager config;
    private string udpIP;
    private string udpPort;

    private void Awake()
    {
        config = new();
        udpIP = config.GetValue("Net", "udpSenderIP");
        udpPort = config.GetValue("Net", "udpSenderPort");
        remoteIP = string.IsNullOrEmpty(udpIP) ? "127.0.0.1" : udpIP;
        if (!string.IsNullOrEmpty(udpPort)) remotePort = int.Parse(udpPort);
        remoteEndPoint = new IPEndPoint(IPAddress.Parse(remoteIP), remotePort);
        client = new UdpClient();
    }

    public void Send(string message)
    {
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            client.Send(data, data.Length, remoteEndPoint);
        }
        catch (Exception err)
        {
            Debug.LogError($"UDPSender error: {err}");
        }
    }

    private void OnApplicationQuit()
    {
        client?.Close();
    }
}
