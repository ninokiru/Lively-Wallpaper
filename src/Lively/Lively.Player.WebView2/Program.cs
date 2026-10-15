using System;
using System.Windows.Forms;
using Lively.Models.Message;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;

namespace Lively.Player.WebView2
{
    internal static class Program
    {
        // Ref: <https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--0-499->
        private const int ErrorFileNotFound = 2;
        private const int ErrorUnhandledException = 1;

        [STAThread]
        private static void Main()
        {
            // Stdout is the ipc channel; report crashes as an error message before exiting
            // so the core can log and restart the wallpaper.
            Application.ThreadException += (s, e) => ReportAndExit(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ReportAndExit(e.ExceptionObject as Exception);

            if (!IsWebView2Available())
                Environment.Exit(ErrorFileNotFound);

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }

        private static bool IsWebView2Available()
        {
            try
            {
                return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString());
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void ReportAndExit(Exception ex)
        {
            try
            {
                Console.Out.WriteLine(JsonConvert.SerializeObject(new LivelyMessageConsole
                {
                    Category = ConsoleMessageType.error,
                    Message = "Unhandled exception: " + (ex?.Message ?? "unknown")
                }));
                Console.Out.Flush();
            }
            catch
            {
            }

            Environment.Exit(ErrorUnhandledException);
        }
    }
}
