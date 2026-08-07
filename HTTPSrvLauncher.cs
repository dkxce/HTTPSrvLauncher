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
using System.Runtime.InteropServices;
using System.Net.NetworkInformation;

namespace HTTPSrvLauncher
{
    public class PortConfig
    {
        public string ServerPath { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public bool SendWebRedirect { get; set; }

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
            while (true)
            {
                if (IsPortBusy(pc.port))
                {
                    if (!is_running) return;
                    Thread.Sleep(2500);
                    continue;
                };
                pc.listener = new TcpListener(IPAddress.Any, pc.port);
                pc.listener.Start();

                using (TcpClient client = pc.listener.AcceptTcpClient())
                {
                    if (pc.SendWebRedirect)
                    {
                        using (NetworkStream stream = client.GetStream())
                        {
                            string html = "<html>" +
                                          "<head>" +
                                          "  <meta http-equiv='refresh' content='10'>" +
                                          "  <meta http-equiv='Cache-Control' content='no-cache, no-store, must-revalidate'>" +
                                          "  <meta http-equiv='Pragma' content='no-cache'>" +
                                          "  <meta http-equiv='Expires' content='0'>" +
                                          "</head>" +
                                          "<body style='font-family: Arial, sans-serif; margin: 20px;'>" +
                                         $"  <p>Launching server <strong>{pc.ServerPath}</strong>, please refresh...</p>" +
                                          "  <p>Next refresh in: <strong id='countdown'>10</strong> sec.</p>" +
                                          "  <script>" +
                                          "    let seconds = 10;" +
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
                    Write2Log($"Launch {pc.ServerPath} error: {ex.Message}", EventLogEntryType.Error);
                };

                while (is_running && (proc != null && !proc.HasExited)) Thread.Sleep(5000);
                if (!is_running) return;
                Thread.Sleep(2500);
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

    public static class InteractiveProcessLauncher
    {
        #region Win32 API Structures & Constants

        [StructLayout(LayoutKind.Sequential)]
        private struct STARTUPINFO
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        private const int STARTF_USESHOWWINDOW = 0x00000001;
        private const short SW_SHOWMINNOACTIVE = 7; // Показать свернутым, окно не становится активным
        private const uint WTS_CURRENT_SERVER_HANDLE = 0;

        #endregion

        #region Win32 API Imports

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("wtsapi32.dll", SetLastError = true)]
        private static extern int WTSEnumerateSessions(IntPtr hServer, int Reserved, int Version, ref IntPtr ppSessionInfo, ref int pCount);

        [DllImport("wtsapi32.dll")]
        private static extern void WTSFreeMemory(IntPtr pMemory);

        [DllImport("wtsapi32.dll", SetLastError = true)]
        private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr Token);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool CreateProcessAsUser(
            IntPtr hToken,
            string lpApplicationName,
            string lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        [StructLayout(LayoutKind.Sequential)]
        private struct WTS_SESSION_INFO
        {
            public uint SessionId;
            [MarshalAs(UnmanagedType.LPStr)]
            public string pWinStationName;
            public int State; // 0 = Active
        }

        #endregion

        // Метод для поиска ID активного пользователя за компьютером
        private static uint GetActiveSessionId()
        {
            IntPtr pSessionInfo = IntPtr.Zero;
            int count = 0;
            uint activeSessionId = uint.MaxValue;

            if (WTSEnumerateSessions((IntPtr)WTS_CURRENT_SERVER_HANDLE, 0, 1, ref pSessionInfo, ref count) != 0)
            {
                int arrayElementSize = Marshal.SizeOf(typeof(WTS_SESSION_INFO));
                IntPtr current = pSessionInfo;

                for (int i = 0; i < count; i++)
                {
                    var si = (WTS_SESSION_INFO)Marshal.PtrToStructure(current, typeof(WTS_SESSION_INFO));
                    if (si.State == 0) // WTSActive
                    {
                        activeSessionId = si.SessionId;
                        break;
                    }
                    current += arrayElementSize;
                }
                WTSFreeMemory(pSessionInfo);
            }
            return activeSessionId;
        }

        // Основной метод запуска
        public static bool StartConsoleMinimized(string appPath, string arguments = "")
        {
            uint sessionId = GetActiveSessionId();
            if (sessionId == uint.MaxValue) return false; // Нет активных пользователей

            if (!WTSQueryUserToken(sessionId, out IntPtr userToken)) return false;

            STARTUPINFO si = new STARTUPINFO();
            si.cb = Marshal.SizeOf(si);
            si.lpDesktop = "winsta0\\default"; // Указывает на рабочий стол пользователя
            si.dwFlags = STARTF_USESHOWWINDOW;
            si.wShowWindow = SW_SHOWMINNOACTIVE; // Важный флаг: Свернуто и неактивно

            PROCESS_INFORMATION pi = new PROCESS_INFORMATION();

            // Формируем командную строку (первым аргументом должен идти сам путь)
            string commandLine = $"\"{appPath}\" {arguments}";

            bool result = CreateProcessAsUser(
                userToken,
                null,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                0,
                IntPtr.Zero,
                null,
                ref si,
                out pi
            );

            // Чистим хэндлы
            if (result)
            {
                CloseHandle(pi.hProcess);
                CloseHandle(pi.hThread);
            }
            CloseHandle(userToken);

            return result;
        }
    }
}
