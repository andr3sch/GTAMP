using System;
using System.IO;
using System.Windows.Forms;
using GTAMP.Shared;

namespace GTAMP.Client
{
    /// <summary>
    /// Le e grava scripts\mp_config.ini (compartilhado com o Launcher Desktop).
    /// Formato INI simples sem depender de APIs externas.
    /// </summary>
    public sealed class ClientConfig
    {
        public string ServerIp = "127.0.0.1";
        public int ServerPort = Protocol.DefaultPort;
        public string ServerPassword = "";
        public bool AutoConnect = true;
        public string Nickname = "Player";
        public Keys ConnectKey = Keys.F9;
        public Keys ChatKey = Keys.T;
        public Keys VoiceKey = Keys.N;
        public Keys VoiceModeKey = Keys.Z;
        public Keys InteractKey = Keys.E;
        public Keys MenuKey = Keys.M;

        // ---- Voz ----
        public bool VoiceActivation = false;
        public float VadThreshold = 0.03f;
        public int MicDeviceIndex = 0;
        public int OutputDeviceIndex = -1;

        public static string GetDefaultPath()
        {
            string dir = Path.GetDirectoryName(typeof(ClientConfig).Assembly.Location);
            if (string.IsNullOrEmpty(dir)) dir = "scripts";
            return Path.Combine(dir, "mp_config.ini");
        }

        public static ClientConfig LoadOrCreate(string path)
        {
            var cfg = new ClientConfig();
            if (!File.Exists(path))
            {
                cfg.Nickname = "Player" + new Random().Next(100, 999);
                cfg.Save(path);
                return cfg;
            }

            try
            {
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#") || line.StartsWith("["))
                        continue;

                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;

                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string val = line.Substring(eq + 1).Trim();

                    switch (key)
                    {
                        case "ip":
                        case "serverip":
                            if (val.Length > 0) cfg.ServerIp = val;
                            break;
                        case "port":
                        case "serverport":
                            int p;
                            if (int.TryParse(val, out p) && p > 0 && p < 65536) cfg.ServerPort = p;
                            break;
                        case "password":
                        case "serverpassword":
                            cfg.ServerPassword = val;
                            break;
                        case "autoconnect":
                            bool ac;
                            if (bool.TryParse(val, out ac)) cfg.AutoConnect = ac;
                            break;
                        case "nickname":
                        case "name":
                            if (val.Length > 0) cfg.Nickname = val;
                            break;
                        case "connectkey":
                            Keys ck;
                            if (Enum.TryParse(val, true, out ck)) cfg.ConnectKey = ck;
                            break;
                        case "chatkey":
                            Keys tk;
                            if (Enum.TryParse(val, true, out tk)) cfg.ChatKey = tk;
                            break;
                        case "voicekey":
                            Keys vk;
                            if (Enum.TryParse(val, true, out vk)) cfg.VoiceKey = vk;
                            break;
                        case "voicemodekey":
                            Keys vmk;
                            if (Enum.TryParse(val, true, out vmk)) cfg.VoiceModeKey = vmk;
                            break;
                        case "interactkey":
                            Keys ik;
                            if (Enum.TryParse(val, true, out ik)) cfg.InteractKey = ik;
                            break;
                        case "menukey":
                            Keys mk;
                            if (Enum.TryParse(val, true, out mk)) cfg.MenuKey = mk;
                            break;
                        case "voiceactivation":
                            bool va;
                            if (bool.TryParse(val, out va)) cfg.VoiceActivation = va;
                            break;
                        case "vadthreshold":
                            float vt;
                            if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out vt))
                                cfg.VadThreshold = Math.Max(0.005f, Math.Min(0.5f, vt));
                            break;
                        case "micdeviceindex":
                            int md;
                            if (int.TryParse(val, out md)) cfg.MicDeviceIndex = md;
                            break;
                        case "outputdeviceindex":
                            int od;
                            if (int.TryParse(val, out od)) cfg.OutputDeviceIndex = od;
                            break;
                    }
                }
            }
            catch
            {
                // Mantem valores padrao se o arquivo estiver corrompido
            }

            if (string.IsNullOrEmpty(cfg.Nickname) || cfg.Nickname.Length < Protocol.MinNicknameLength)
                cfg.Nickname = "Player" + new Random().Next(100, 999);

            return cfg;
        }

        public void Save(string path)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string content =
                    "[Server]\r\n" +
                    "IP=" + ServerIp + "\r\n" +
                    "Port=" + ServerPort + "\r\n" +
                    "Password=" + ServerPassword + "\r\n" +
                    "AutoConnect=" + (AutoConnect ? "true" : "false") + "\r\n" +
                    "\r\n" +
                    "[Player]\r\n" +
                    "Nickname=" + Nickname + "\r\n" +
                    "\r\n" +
                    "[Keys]\r\n" +
                    "ConnectKey=" + ConnectKey + "\r\n" +
                    "ChatKey=" + ChatKey + "\r\n" +
                    "VoiceKey=" + VoiceKey + "\r\n" +
                    "VoiceModeKey=" + VoiceModeKey + "\r\n" +
                    "InteractKey=" + InteractKey + "\r\n" +
                    "MenuKey=" + MenuKey + "\r\n" +
                    "\r\n" +
                    "[Voice]\r\n" +
                    "VoiceActivation=" + (VoiceActivation ? "true" : "false") + "\r\n" +
                    "VadThreshold=" + VadThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n" +
                    "MicDeviceIndex=" + MicDeviceIndex + "\r\n" +
                    "OutputDeviceIndex=" + OutputDeviceIndex + "\r\n";

                File.WriteAllText(path, content);
            }
            catch
            {
                // Ignora erro de escrita (ex.: pasta somente leitura)
            }
        }
    }
}
