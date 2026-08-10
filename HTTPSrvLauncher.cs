using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.ServiceProcess;
using System.Text;
using Newtonsoft.Json;
using System.Net;
using System.Threading;
using System.Net.NetworkInformation;

namespace HTTPSrvLauncher
{
    public class PortConfig
    {
        public string ServerPath { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public bool SendWebRedirect { get; set; }
        public int RedirectDelay { get; set; } = 10;
        public bool LoopOnAppExit { get; set; } = true;
        public bool WaitForExit { get; set; } = true;
        public bool CheckPortBusy { get; set; } = true;
        public bool AppLog { get; set; } = true;

        [JsonIgnore]
        public TcpListener listener = null;

        [JsonIgnore]
        public Thread thread = null;

        [JsonIgnore]
        public int port = 0;
    }

    public partial class HTTPSrvLauncher : ServiceBase
    {
        private Dictionary<string, PortConfig> pcfg = null;
        private bool is_running = false;

        public HTTPSrvLauncher()
        {
            InitializeComponent();
            this.ServiceName = "HTTPSrvLauncher";
        }

        protected override void OnStart(string[] args)
        {
            if (!LoadConfig()) throw new Exception("Can't run");

            is_running = true;
            foreach (KeyValuePair<string,PortConfig> kvp in pcfg)
            {
                if (!int.TryParse(kvp.Key, out kvp.Value.port) || kvp.Value.port <= 0 || kvp.Value.port > ushort.MaxValue)
                {
                    Write2Log($"Config {kvp.Key} is bad!", EventLogEntryType.Warning);
                    continue;
                };
                (kvp.Value.thread = new Thread(() => RunTcpListener(kvp.Value))).Start();
            };
        }

        private void RunTcpListener(PortConfig pc)
        {
            string fileLog = pc.AppLog ? Path.GetFileNameWithoutExtension(pc.ServerPath) : null;
            Append2Log("---", fileLog);
            Append2Log($"Start {fileLog}:{pc.port} thread", fileLog);

            while (true)
            {
                if (pc.CheckPortBusy && IsPortBusy(pc.port))
                {
                    if (!is_running) return;
                    Thread.Sleep(2500);
                    continue;
                };

                pc.listener = new TcpListener(IPAddress.Any, pc.port);
                pc.listener.Start();
                using (TcpClient client = pc.listener.AcceptTcpClient())
                {
                    Append2Log($"GetClient {fileLog}:{pc.port} - {client.Client.RemoteEndPoint}", fileLog);
                    if (pc.SendWebRedirect)
                    {
                        using (NetworkStream stream = client.GetStream())
                        {
                            Append2Log($"Send HTML Redirect {fileLog}:{pc.port} - {client.Client.RemoteEndPoint}", fileLog);
                            string html = "<html>" +
                                          "<head>" +
                                         $"  <meta http-equiv='refresh' content='{pc.RedirectDelay}'>" +
                                          "  <meta http-equiv='Cache-Control' content='no-cache, no-store, must-revalidate'>" +
                                          "  <meta http-equiv='Pragma' content='no-cache'>" +
                                          "  <meta http-equiv='Expires' content='0'>" +
                                          "</head>" +
                                          "<body style='font-family: Arial, sans-serif; margin: 20px;'>" +
                                         $"  <p>Launching server <strong>{pc.ServerPath}</strong>, please refresh...</p>" +
                                         $"  <p>Next refresh in: <strong id='countdown'>{pc.RedirectDelay}</strong> sec.</p>" +
                                          "  <script>" +
                                         $"    let seconds = {pc.RedirectDelay};" +
                                          "    const countdownElement = document.getElementById('countdown');" +
                                          "    const interval = setInterval(() => {" +
                                          "      seconds--;" +
                                          "      countdownElement.textContent = seconds;" +
                                          "      if (seconds <= 0) clearInterval(interval);" +
                                          "    }, 1000);" +
                                          "  </script>" +
                                          "</body>" +
                                          "</html>";

                            string response = "HTTP/1.1 200 OK\r\n" +
                                              "Content-Type: text/html; charset=UTF-8\r\n" +
                                              $"Content-Length: {Encoding.UTF8.GetByteCount(html)}\r\n" +
                                              "Cache-Control: no-cache, no-store, must-revalidate\r\n" +
                                              "Pragma: no-cache\r\n" +
                                              "Expires: 0\r\n" +
                                              "Connection: close\r\n\r\n" +
                                              html;



                            byte[] responseBytes = Encoding.UTF8.GetBytes(response);
                            stream.Write(responseBytes, 0, responseBytes.Length);
                            stream.Flush();
                        };
                    };
                    client.Close();
                };
                pc.listener.Stop();

                Process proc = new Process();
                try
                {
                    Append2Log($"Starting {fileLog}:{pc.port}", fileLog);
                    proc.StartInfo.FileName = pc.ServerPath;
                    proc.StartInfo.Arguments = pc.Arguments;
                    proc.StartInfo.UseShellExecute = false;
                    proc.StartInfo.CreateNoWindow = false;
                    proc.StartInfo.WindowStyle = ProcessWindowStyle.Minimized;
                    Thread.Sleep(2500);
                    proc.Start();
                    Thread.Sleep(2500);
                }
                catch (Exception ex)
                {
                    Append2Log($"Launch {fileLog}:{pc.port} error: {ex.Message}", fileLog);
                    Write2Log($"Launch {pc.ServerPath} error: {ex.Message}", EventLogEntryType.Error);
                };
                if(proc != null && !proc.HasExited)
                    Append2Log($"Started {fileLog}:{pc.port} error: {pc.ServerPath}", fileLog);

                while (is_running && (pc.WaitForExit && proc != null && !proc.HasExited)) Thread.Sleep(5000);
                if (!is_running) return;
                if (!pc.LoopOnAppExit) return;
                Thread.Sleep(2500);
                Append2Log($"Loop {fileLog}:{pc.port} thread", fileLog);
            };           
        }

        protected override void OnStop()
        {
            is_running = false;
            foreach (KeyValuePair<string, PortConfig> kvp in pcfg)
                if (kvp.Value == null || kvp.Value.thread == null) continue;
                else
                {
                    try
                    {
                        if (kvp.Value.listener != null) kvp.Value.listener.Stop();
                        kvp.Value.thread.Abort();
                    }
                    catch (Exception ex) { Write2Log(ex.Message, EventLogEntryType.Error); };
                };
        }

        public static bool IsPortBusy(int port)
        {
            IPGlobalProperties ipProperties = IPGlobalProperties.GetIPGlobalProperties();

            IPEndPoint[] tcpListeners = ipProperties.GetActiveTcpListeners();
            foreach (IPEndPoint endpoint in tcpListeners) if (endpoint.Port == port) return true;
            TcpConnectionInformation[] tcpConnections = ipProperties.GetActiveTcpConnections();
            foreach (TcpConnectionInformation connection in tcpConnections) if (connection.LocalEndPoint.Port == port) return true;
            IPEndPoint[] udpListeners = ipProperties.GetActiveUdpListeners();
            foreach (IPEndPoint endpoint in udpListeners) if (endpoint.Port == port) return true;
            return false;
        }

        private static void Write2Log(string text, EventLogEntryType tpe = EventLogEntryType.Information)
        {
            string logName = "Application";
            string sourceName = "HTTPSrvLauncher";

            try {
                if (!EventLog.SourceExists(sourceName)) EventLog.CreateEventSource(sourceName, logName);
                EventLog.WriteEntry(sourceName, text, tpe); }
            catch { };
        }

        private static void Append2Log(string text, string file)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(file)) return;
            string fn = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location), $"-{file}-log.txt");
            try
            {
                FileStream fs = new FileStream(fn, FileMode.Append, FileAccess.Write, FileShare.Read);
                StreamWriter sw = new StreamWriter(fs);
                sw.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + $": {text}");
                sw.Close();
                fs.Close();
            }
            catch { };
        }

        private bool LoadConfig()
        {
            string cfg = Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location), "HTTPSrvLauncher.json");
            if (!File.Exists(cfg))
            {
                Write2Log($"{cfg} not found!", EventLogEntryType.Error);
                return false;
            };

            try
            {
                string jsonString = File.ReadAllText(cfg);
                pcfg = JsonConvert.DeserializeObject<Dictionary<string, PortConfig>>(jsonString);                               
            }
            catch (Exception ex)
            {
                Write2Log(ex.Message, EventLogEntryType.Error);
                return false;
            };

            if (pcfg == null || pcfg.Count == 0)
            {
                Write2Log($"No setup in cfg", EventLogEntryType.Error);
                return false;
            };

            return true;
        }
    }
}
