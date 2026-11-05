using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

/// <summary>Reads the editable LAN endpoint without requiring a new build.</summary>
public static class NetworkFileSettings
{
    public const string DefaultText = "# Client connects to the Host PC IPv4 address.\nIP=192.168.31.12\nPort=5678\n";

    public static string FilePath
    {
        get
        {
#if UNITY_EDITOR
            return Path.Combine(Application.dataPath, "sza/NetworkConfig/network.txt");
#else
            return Path.Combine(Path.GetDirectoryName(Application.dataPath), "NetworkConfig/network.txt");
#endif
        }
    }

    public static bool TryLoad(out string ip, out ushort port)
    {
        ip = null;
        port = 0;
        try
        {
            if (!File.Exists(FilePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, DefaultText, new UTF8Encoding(false));
                Debug.LogWarning("[NetworkConfig] Created default configuration. Check the Host IP: " + FilePath);
            }
            string error;
            if (!TryParse(File.ReadAllText(FilePath, Encoding.UTF8), out ip, out port, out error))
            {
                Debug.LogError("[NetworkConfig] " + error + " File: " + FilePath);
                return false;
            }
            Debug.Log("[NetworkConfig] Loaded " + ip + ":" + port + " from " + FilePath);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError("[NetworkConfig] Cannot read configuration: " + FilePath + "\n" + exception.Message);
            return false;
        }
    }

    public static bool TryParse(string text, out string ip, out ushort port, out string error)
    {
        ip = null;
        port = 0;
        error = null;
        bool hasPort = false;
        using (var reader = new StringReader(text ?? ""))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim().TrimStart('\uFEFF');
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                int separator = line.IndexOf('=');
                if (separator <= 0) { error = "Expected IP=... or Port=..."; return false; }
                string key = line.Substring(0, separator).Trim();
                string value = line.Substring(separator + 1).Trim();
                if (key.Equals("IP", StringComparison.OrdinalIgnoreCase))
                {
                    if (ip != null) { error = "Duplicate IP entry."; return false; }
                    ip = value;
                }
                else if (key.Equals("Port", StringComparison.OrdinalIgnoreCase))
                {
                    if (hasPort || !ushort.TryParse(value, out port) || port == 0)
                    { error = "Port must be a single integer from 1 to 65535."; return false; }
                    hasPort = true;
                }
                else { error = "Unknown configuration key: " + key; return false; }
            }
        }

        IPAddress address;
        string[] octets = (ip ?? "").Split('.');
        if (octets.Length != 4)
        { error = "IP must be the Host PC IPv4 address."; return false; }
        foreach (string octet in octets)
        {
            byte parsed;
            if (octet.Length == 0 || !byte.TryParse(octet, out parsed))
            { error = "Invalid IPv4 address."; return false; }
        }
        if (!IPAddress.TryParse(ip, out address) || address.AddressFamily != AddressFamily.InterNetwork ||
            address.Equals(IPAddress.Any) || address.Equals(IPAddress.Broadcast) || address.GetAddressBytes()[0] >= 224)
        { error = "Invalid Host IPv4 address."; return false; }
        if (!hasPort) { error = "Missing Port entry."; return false; }
        return true;
    }
}
