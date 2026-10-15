using CefSharp;
using Lively.Common.Helpers.Pinvoke;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace Lively.Player.CefSharp.Extensions.CefSharp
{
    public static class BrowserExtensions
    {
        /// <summary>
        /// This should be called after the WebView has finished initializing (e.g. after LoadingStateChanged),
        /// otherwise the internal Chromium window may not yet exist.
        /// </summary>
        public static bool TryGetChrome_WidgetWin_1(this IWebBrowser chromeBrowser, out IntPtr chrome_WidgetWin_1)
        {
            var browserHandle = chromeBrowser.GetBrowser().GetHost().GetWindowHandle();
            chrome_WidgetWin_1 = NativeMethods.FindWindowEx(browserHandle, IntPtr.Zero, "Chrome_WidgetWin_1", null);
            return chrome_WidgetWin_1 != IntPtr.Zero;
        }

        /// <summary>
        /// This should be called after the WebView has finished initializing (e.g. after LoadingStateChanged),
        /// otherwise the internal Chromium window may not yet exist.
        /// </summary>
        public static bool TryGetIntermediateD3DWindow(this IWebBrowser chromeBrowser, out IntPtr intermediateD3DWindow)
        {
            intermediateD3DWindow = IntPtr.Zero;
            if (!TryGetChrome_WidgetWin_1(chromeBrowser, out IntPtr chrome_WidgetWin_1))
                return false;

            intermediateD3DWindow = NativeMethods.FindWindowEx(chrome_WidgetWin_1, IntPtr.Zero, "Intermediate D3D Window", null);
            return intermediateD3DWindow != IntPtr.Zero;
        }

        /// <summary>
        /// This should be called after the WebView has finished initializing (e.g. after LoadingStateChanged),
        /// otherwise the internal Chromium window may not yet exist.
        /// </summary>
        public static bool TryGetCefD3DRenderingSubProcessId(this IWebBrowser chromeBrowser, out int cefD3DRenderingSubProcessId)
        {
            cefD3DRenderingSubProcessId = 0;
            if (!TryGetIntermediateD3DWindow(chromeBrowser, out IntPtr d3d))
                return false;

            var result = NativeMethods.GetWindowThreadProcessId(d3d, out cefD3DRenderingSubProcessId);
            return result != 0;
        }

        /// <summary>
        /// Supports arrays
        /// </summary>
        /// <param name="functionName"></param>
        /// <param name="parameters"></param>
        public static void ExecuteScriptAsyncEx(this IWebBrowser chromeBrowser, string functionName, params object[] parameters)
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
            chromeBrowser?.ExecuteScriptAsync(script.ToString());
        }

        public static async Task<bool> TryHideShaderToyGui(this IWebBrowser browser)
        {
            try
            {
                var script = @"document.querySelector('canvas').style.cursor='auto';
                    document.querySelector('canvas').ondblclick=()=>{};
                    document.querySelector('#shaderInfo').style.display='none';
                    document.querySelector('#playerBar').style.display='none';";

                var result = await browser.EvaluateScriptAsync(script);
                return result.Success;
            }
            catch
            {
                return false;
            }
        }
    }
}
