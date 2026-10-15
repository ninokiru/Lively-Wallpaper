using Lively.Common;
using Lively.Common.Helpers.Pinvoke;
using Lively.Models.Enums;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using WebView = Microsoft.Web.WebView2.WinForms.WebView2;

namespace Lively.Player.WebView2.Extensions.WebView2
{
    public static class CoreWebView2Extensions
    {
        public static void NavigateToLocalPath(this WebView webView, string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException(nameof(filePath));

            var fileName = Path.GetFileName(filePath);
            // Hex format to creates valid hostname and prevent cache conflicts between folders.
            // Append `.localhost` to trigger immediate NXDOMAIN, bypassing DNS delay in WebView2.
            // Issue: https://github.com/MicrosoftEdge/WebView2Feedback/issues/2381
            var hostName = $"{LinkUtil.GetStableHostName(filePath)}.localhost";
            var directoryPath = Path.GetDirectoryName(filePath);
            webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                hostName,
                directoryPath,
                CoreWebView2HostResourceAccessKind.Allow);

            webView.CoreWebView2.Navigate($"https://{hostName}/{fileName}");
        }

        // Ref: https://stackoverflow.com/questions/62835549/equivalent-of-webbrowser-invokescriptstring-object-in-webview2
        public static async Task<string> ExecuteScriptFunctionAsync(this WebView webView, string functionName, params object[] parameters)
        {
            var script = new StringBuilder();
            script.Append(functionName);
            script.Append("(");
            for (int i = 0; i < parameters.Length; i++)
            {
                script.Append(JsonConvert.SerializeObject(parameters[i]));
                if (i < parameters.Length - 1)
                {
                    script.Append(", ");
                }
            }
            script.Append(");");
            return await webView?.ExecuteScriptAsync(script.ToString());
        }

        // No official API.
        // Ref: https://github.com/MicrosoftEdge/WebView2Feedback/issues/3348
        public static async Task<bool> TryPauseMedia(this WebView webView)
        {
            try
            {
                var script = @"document.querySelectorAll('video, audio').forEach(mediaElement => mediaElement.pause());";
                await webView.ExecuteScriptAsync(script);
            }
            catch
            {
                return false;
            }
            return true;
        }

        // No official API.
        // Ref: https://github.com/MicrosoftEdge/WebView2Feedback/issues/3348
        public static async Task<bool> TryPlayMedia(this WebView webView)
        {
            try
            {
                var script = @"document.querySelectorAll('video, audio').forEach(mediaElement => mediaElement.play());";
                await webView.ExecuteScriptAsync(script);
            }
            catch
            {
                return false;
            }
            return true;
        }

        public static async Task<bool> TryHideShaderToyGui(this WebView webView)
        {
            try
            {
                var script = @"document.querySelector('canvas').style.cursor='auto';
                    document.querySelector('canvas').ondblclick=()=>{};
                    document.querySelector('#shaderInfo').style.display='none';
                    document.querySelector('#playerBar').style.display='none';";
                await webView.ExecuteScriptAsync(script);
            }
            catch
            {
                return false;
            }
            return true;
        }

        public static CoreWebView2PreferredColorScheme GetPreferredColorScheme(this AppTheme theme)
        {
            return theme switch
            {
                AppTheme.Auto => CoreWebView2PreferredColorScheme.Auto,
                AppTheme.Dark => CoreWebView2PreferredColorScheme.Dark,
                AppTheme.Light => CoreWebView2PreferredColorScheme.Light,
                _ => CoreWebView2PreferredColorScheme.Auto,
            };
        }

        /// <summary>
        /// This should be called after the WebView has finished initializing (e.g. after NavigationCompleted),
        /// otherwise the internal Chromium window may not yet exist.
        /// </summary>
        public static bool TryGetChrome_WidgetWin_0(this WebView webView, out IntPtr chrome_WidgetWin_0)
        {
            // WindowsForms10.Window.8.app.0.141b42a_r9_ad1
            chrome_WidgetWin_0 = NativeMethods.FindWindowEx(webView.Handle, IntPtr.Zero, "Chrome_WidgetWin_0", null);
            return chrome_WidgetWin_0 != IntPtr.Zero;
        }

        /// <summary>
        /// This should be called after the WebView has finished initializing (e.g. after NavigationCompleted),
        /// otherwise the internal Chromium window may not yet exist.
        /// </summary>
        public static bool TryGetIntermediateD3DWindow(this WebView webView, out IntPtr intermediateD3DWindow)
        {
            intermediateD3DWindow = IntPtr.Zero;
            if (!TryGetChrome_WidgetWin_0(webView, out IntPtr chrome_WidgetWin_0))
                return false;

            var chrome_WidgetWin_1 = NativeMethods.FindWindowEx(chrome_WidgetWin_0, IntPtr.Zero, "Chrome_WidgetWin_1", null);
            if (chrome_WidgetWin_1 == IntPtr.Zero)
                return false;

            intermediateD3DWindow = NativeMethods.FindWindowEx(chrome_WidgetWin_1, IntPtr.Zero, "Intermediate D3D Window", null);
            return intermediateD3DWindow != IntPtr.Zero;
        }

        /// <summary>
        /// This should be called after the WebView has finished initializing (e.g. after NavigationCompleted),
        /// otherwise the internal Chromium window may not yet exist.
        /// </summary>
        public static bool TryGetCefD3DRenderingSubProcessId(this WebView webView, out int cefD3DRenderingSubProcessId)
        {
            cefD3DRenderingSubProcessId = 0;
            if (!TryGetIntermediateD3DWindow(webView, out IntPtr intermediateD3DWindow))
                return false;

            var result = NativeMethods.GetWindowThreadProcessId(intermediateD3DWindow, out cefD3DRenderingSubProcessId);
            return result != 0;
        }
    }
}
