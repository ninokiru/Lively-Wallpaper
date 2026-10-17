using CommandLine;
using Lively.Common;
using Lively.Common.Extensions;
using Lively.Common.Helpers;
using Lively.Common.Helpers.Pinvoke;
using Lively.Common.JsonConverters;
using Lively.Common.Services;
using Lively.Models.Enums;
using Lively.Models.Message;
using Lively.Player.WebView2.Extensions.WebView2;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using WebView = Microsoft.Web.WebView2.WinForms.WebView2;

namespace Lively.Player.WebView2
{
    public partial class Form1 : Form
    {
        private WebView webView;
        private StartArgs startArgs;
        private bool isPaused = false;
        private bool isVideoStream = false;
        private bool isMediaWallpaper = false;
        private int cefD3DRenderingSubProcessId;

        private bool initializedServices = false; //delay API init till loaded page
        private IAudioVisualizerService visualizerService;
        private IHardwareUsageService hardwareUsageService;
        private INowPlayingService nowPlayingService;

        private bool IsDebugging { get; } = BuildInfoUtil.IsDebugBuild();

        public Form1()
        {
            InitializeComponent();
            if (IsDebugging)
            {
                startArgs = new StartArgs
                {
                    // .html fullpath
                    Url = "https://google.com/",
                    //online or local(file)
                    Type = WebPageType.online,
                    // LivelyProperties.json path if any
                    Properties = @"",
                    SysInfo = false,
                    NowPlaying = false,
                    AudioVisualizer = false,
                    PauseEvent = false
                };

                this.FormBorderStyle = FormBorderStyle.Sizable;
                this.WindowState = FormWindowState.Normal;
                this.StartPosition = FormStartPosition.Manual;
                this.Size = new Size(1920, 1080);
                this.ShowInTaskbar = true;
                this.MaximizeBox = true;
                this.MinimizeBox = true;
            }
            else
            {
                Parser.Default.ParseArguments<StartArgs>(Environment.GetCommandLineArgs())
                    .WithParsed((x) => startArgs = x)
                    .WithNotParsed(HandleParseError);

                this.WindowState = FormWindowState.Minimized;
                this.StartPosition = FormStartPosition.Manual;
                this.Location = new Point(-9999, 0);

                if (startArgs.Geometry != null)
                {
                    var msg = startArgs.Geometry.Split('x');
                    if (msg.Length >= 2 && int.TryParse(msg[0], out int width) && int.TryParse(msg[1], out int height))
                    {
                        this.Size = new Size(width, height);
                    }
                }

                var darkColor = Color.FromArgb(30, 30, 30);
                var lightColor = Color.FromArgb(240, 240, 240);
                this.BackColor = startArgs.Theme switch
                {
                    AppTheme.Auto => ThemeUtil.GetWindowsTheme() == AppTheme.Dark ? darkColor : lightColor,
                    AppTheme.Light => lightColor,
                    AppTheme.Dark => darkColor,
                    _ => darkColor,
                };
            }

            InitializeWebView2Async().Await(() => {
                _ = ListenToParent();
            }, 
            (err) =>
            {
                err.SendError(SendToParent, "Failed to initialize WebView2");
                // Exit or display custom error page.
                Environment.Exit(1);
            });
        }

        // Hide from taskview and taskbar.
        // ShowInTaskbar = true does not create TOOLWINDOW.
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                return cp;
            }
        }

        private void HandleParseError(IEnumerable<Error> errs)
        {
            if (errs != null)
                string.Join(Environment.NewLine, errs).SendError(SendToParent, "Error parsing launch arguments");

            // ERROR_INVALID_PARAMETER
            // Ref: <https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--0-499->
            Environment.Exit(87);
        }

        public async Task InitializeWebView2Async()
        {
            webView = new WebView() {
                DefaultBackgroundColor = Color.Transparent
            };
            webView.NavigationCompleted += WebView_NavigationCompleted;

            var webViewStartArgs =
                // Allow media autoplay even if not muted.
                "--autoplay-policy=no-user-gesture-required " +
                // Disable SMTC.
                "--disable-features=HardwareMediaKeyHandling " +
                // Integrated GPUs (Intel UHD 610/620/710/730, Iris Xe, AMD Radeon) sit on the
                // Chromium blocklist, which drops the wallpaper to CPU compositing and software
                // video decode.
                "--ignore-gpu-blocklist --enable-accelerated-video-decode " +
                // Route H.265 through the Windows decoder so the iGPU media engine is used.
                "--enable-features=PlatformHEVCDecoderSupport";
            CoreWebView2EnvironmentOptions options = new CoreWebView2EnvironmentOptions(webViewStartArgs);
            // WebView2 does not have in-memory mode, ref: https://github.com/MicrosoftEdge/WebView2Feedback/issues/3637
            // Custom user data folder, ref: https://docs.microsoft.com/en-us/microsoft-edge/webview2/concepts/user-data-folder
            $"Setting UserData path: {startArgs.UserDataPath}".SendLog(SendToParent);
            var env = await CoreWebView2Environment.CreateAsync(null, startArgs.UserDataPath, options);
            await webView.EnsureCoreWebView2Async(env);

            // Defaults
            webView.CoreWebView2.IsMuted = startArgs.Volume == 0;
            webView.CoreWebView2.Profile.PreferredColorScheme = startArgs.Theme.GetPreferredColorScheme();
            if (startArgs.Scale != null)
            {
                $"Attempt to set scale: {startArgs.Scale.Value}".SendLog(SendToParent);
                try
                {
                    // RasterizationScale is better choice than ZoomFactor since it is not affected by system text size but not exposed in .NET, ref:
                    // https://github.com/MicrosoftEdge/WebView2Feedback/issues/4775
                    // https://github.com/MicrosoftEdge/WebView2Feedback/issues/3839
                    // https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2controller.rasterizationscale
                    var field = typeof(WebView).GetField("_coreWebView2Controller", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (field?.GetValue(webView) is CoreWebView2Controller controller)
                    {
                        controller.ShouldDetectMonitorScaleChanges = false;
                        // Bug: Initial value is always 1, ref: https://github.com/MicrosoftEdge/WebView2Feedback/issues/5329
                        $"Current scale: {controller.RasterizationScale}".SendLog(SendToParent);
                        controller.RasterizationScale = startArgs.Scale.Value;
                        $"Updated scale: {controller.RasterizationScale}".SendLog(SendToParent);
                    }
                    else
                    {
                        "CoreWebView2Controller not found".SendError(SendToParent);
                    }
                }
                catch (Exception ex)
                {
                    ex.SendError(SendToParent, "Failed to set scale");
                }
            }

            if (!IsDebugging)
            {
                // Don't allow contextmenu and devtools.
                webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            }

            if (!string.IsNullOrWhiteSpace(startArgs.DebugPort) && int.TryParse(startArgs.DebugPort, out int _))
            {
                // In WebView2 --remote-debugging-port=XXXX is not opening up a direct connection, we will just ignore port and open DevTools instead.
                webView.CoreWebView2.OpenDevToolsWindow();
                webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            }

            webView.CoreWebView2.ProcessFailed += CoreWebView2_ProcessFailed;
            webView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
            webView.CoreWebView2.DownloadStarting += CoreWebView2_DownloadStarting;
            webView.CoreWebView2.DocumentTitleChanged += CoreWebView2_DocumentTitleChanged;
            webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

            if (startArgs.FrameLimit > 0 && IsFrameLimitPage(startArgs.Type))
            {
                // Chromium has no frame rate switch, so the page is paced from the
                // inside by wrapping requestAnimationFrame before any script runs.
                // Media pages pace themselves, they don't need this.
                await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                    GetFrameLimitScript(startArgs.FrameLimit));
            }

            switch (startArgs.Type)
            {
                case WebPageType.online:
                    {
                        var (contentType, id) = WebContentUtil.GetContentType(startArgs.Url);
                        switch (contentType)
                        {
                            case WebContentType.shadertoy:
                                {
                                    $"Opening shadertoy shader: {id}".SendLog(SendToParent);
                                    webView.CoreWebView2.Navigate($"https://www.shadertoy.com/embed/{id}?gui=false&t=10&paused=false&muted=true");
                                }
                                break;
                            case WebContentType.youtube:
                                {
                                    isVideoStream = true;
                                    $"Opening yt stream: {id}".SendLog(SendToParent);
                                    // Error 153, ref: https://github.com/rocksdanister/lively/issues/2991
                                    webView.CoreWebView2.AddWebResourceRequestedFilter("*youtube.com/*", CoreWebView2WebResourceContext.All);
                                    webView.CoreWebView2.WebResourceRequested += (s, e) =>
                                    {
                                        var headers = e.Request.Headers;
                                        if (!headers.Contains("Referer"))
                                            headers.SetHeader("Referer", "https://localhost/");
                                    };
                                    webView.CoreWebView2.Navigate($"https://www.youtube.com/embed/{id}?version=3&rel=0&autoplay=1&loop=1&controls=0&playlist={id}");
                                }
                                break;
                            case WebContentType.generic:
                                {
                                    $"Opening address: {startArgs.Url}".SendLog(SendToParent);
                                    webView.CoreWebView2.Navigate(startArgs.Url);
                                }
                                break;
                            case WebContentType.none:
                                {
                                    "Failed to parse url".SendError(SendToParent);
                                    throw new ArgumentException(startArgs.Url);
                                }
                            default:
                                throw new NotImplementedException();
                        }
                    }
                    break;
                case WebPageType.local:
                    {
                        $"Opening local project: {startArgs.Url}".SendLog(SendToParent);
                        webView.NavigateToLocalPath(startArgs.Url);
                    }
                    break;
                case WebPageType.video:
                case WebPageType.gif:
                case WebPageType.image:
                    NavigateToMediaFile(startArgs.Url, startArgs.Type);
                    break;
            }
            this.Controls.Add(webView);
            webView.Dock = DockStyle.Fill;

            // Make sure to do this after WebView control is attached to form to avoid issues.
            SendToParent(new LivelyMessageHwnd() {
                Hwnd = webView.Handle.ToInt32()
            });
        }

        /// <summary>
        /// Pages the player cannot pace by itself: the media host page throttles its own
        /// animation loop, everything else needs the injected wrapper.
        /// </summary>
        private static bool IsFrameLimitPage(WebPageType kind)
        {
            return kind switch
            {
                WebPageType.video => false,
                WebPageType.gif => false,
                WebPageType.image => false,
                _ => true,
            };
        }

        private static string GetFrameLimitScript(int frameLimit)
        {
            var minimum = (1000d / frameLimit).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return FrameLimitScript.Replace("__minFrameMs__", minimum);
        }

        // Batches every callback and only releases them on the paced frame, so a wallpaper
        // that asks for 30 fps gets one paint per 33ms instead of one per vsync.
        private const string FrameLimitScript = """
            (() => {
              const minimum = __minFrameMs__;
              if (!isFinite(minimum) || minimum <= 0)
                return;
              const paint = window.requestAnimationFrame.bind(window);
              const abort = window.cancelAnimationFrame.bind(window);
              const pending = new Map();
              let token = 1;
              let last = 0;
              let handle = 0;
              let armed = false;
              const flush = (time) => {
                armed = false;
                handle = 0;
                if (time - last < minimum - 1) {
                  arm();
                  return;
                }
                last = time;
                const batch = Array.from(pending.values());
                pending.clear();
                for (const callback of batch) {
                  try {
                    callback(time);
                  } catch (error) {
                    setTimeout(() => { throw error; }, 0);
                  }
                }
                if (pending.size > 0)
                  arm();
              };
              const arm = () => {
                if (armed)
                  return;
                armed = true;
                handle = paint(flush);
              };
              window.requestAnimationFrame = (callback) => {
                const id = token++;
                pending.set(id, callback);
                arm();
                return id;
              };
              window.cancelAnimationFrame = (id) => {
                if (pending.delete(id)) {
                  if (pending.size === 0 && armed) {
                    armed = false;
                    abort(handle);
                  }
                  return;
                }
                abort(id);
              };
            })();
            """;

        private void CoreWebView2_DocumentTitleChanged(object sender, object e)
        {
            this.Invoke((System.Windows.Forms.MethodInvoker)(() => this.Text = webView.CoreWebView2.DocumentTitle));
        }

        private void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            // Media host page reports decode/load failures that navigation completion cannot see.
            try
            {
                SendToParent(JsonConvert.DeserializeObject<IpcMessage>(e.WebMessageAsJson,
                    new JsonSerializerSettings() { Converters = { new IpcMessageConverter() } }));
            }
            catch (Exception ie)
            {
                ie.SendError(SendToParent, "Failed to parse page message");
            }
        }

        private void NavigateToMediaFile(string filePath, WebPageType kind)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                $"Media file not found: {filePath}".SendError(SendToParent);
                // Exit code 5: distinct from 2 (webview2 runtime missing) handled by the core.
                Environment.Exit(5);
                return;
            }

            var hostName = $"{LinkUtil.GetStableHostName(filePath)}.localhost";
            webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                hostName,
                Path.GetDirectoryName(filePath),
                CoreWebView2HostResourceAccessKind.Allow);

            var mediaUri = new Uri($"https://{hostName}/{Uri.EscapeDataString(Path.GetFileName(filePath))}");
            isMediaWallpaper = true;
            $"Opening media {kind}: {mediaUri}".SendLog(SendToParent);
            webView.CoreWebView2.NavigateToString(MediaPageBuilder.Build(kind, mediaUri, startArgs.Scaler, startArgs.Volume, startArgs.FrameLimit));
        }

        private void CoreWebView2_ProcessFailed(object sender, CoreWebView2ProcessFailedEventArgs e)
        {
            // Expected behavior: DebugActiveProcess(CEF_D3DRenderingSubProcess)
            // Ref: https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2processfailedkind
            if (isPaused && e.Reason == CoreWebView2ProcessFailedReason.Unresponsive)
                return;

            e.Reason.ToString().SendError(SendToParent, "CoreWebView2 process failed");
        }

        private void CoreWebView2_NewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            // Avoid popups.
            if (e.IsUserInitiated)
            {
                e.Handled = true;
                // Open new tab/window  hyperlink (target="_blank") in default browser.
                LinkUtil.OpenBrowser(e.Uri);
            }
        }

        private void CoreWebView2_DownloadStarting(object sender, CoreWebView2DownloadStartingEventArgs e)
        {
            // Cancel user requested downloads.
            e.Cancel = true;
        }

        private async void WebView_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            // Restore default.
            webView.DefaultBackgroundColor = Color.White;

            if (!e.IsSuccess)
            {
                e.WebErrorStatus.ToString().SendError(SendToParent, "WebView navigation failed");
                return;
            }

            if (!webView.TryGetCefD3DRenderingSubProcessId(out cefD3DRenderingSubProcessId))
                "Failed to retrieve GetCefD3DRenderingSubProcessId".SendError(SendToParent);

            switch (startArgs.Type)
            {
                case WebPageType.online:
                    switch (WebContentUtil.GetContentType(startArgs.Url).ContentType)
                    {
                        case WebContentType.shadertoy:
                            await webView.TryHideShaderToyGui();
                            break;
                    }
                    break;
                case WebPageType.local:
                    await RestoreLivelyProperties(startArgs.Properties);
                    break;
                case WebPageType.video:
                case WebPageType.gif:
                case WebPageType.image:
                    // Media wallpapers keep their own saved LivelyProperties (filters, fit, speed).
                    await RestoreLivelyProperties(startArgs.Properties);
                    break;
            }
            SendToParent(new LivelyMessageWallpaperLoaded() { Success = e.IsSuccess });

            if (!initializedServices)
            {
                initializedServices = true;
                if (startArgs.NowPlaying)
                {
                    nowPlayingService = new NpsmNowPlayingService();
                    nowPlayingService.NowPlayingTrackChanged += (s, e) => {
                        try
                        {
                            if (isPaused)
                                return;

                            // CefSharp CanExecuteJavascriptInMainFrame equivalent in webview required?
                            this.Invoke((Action)(() =>
                            {
                                _ = webView.ExecuteScriptFunctionAsync("livelyCurrentTrack", JsonConvert.SerializeObject(e, Formatting.Indented));
                            }));

                        }
                        catch (Exception ex)
                        {
                            ex.SendError(SendToParent, "Error sending track information");

                        }
                    };
                    nowPlayingService.Start();
                }


                if (startArgs.SysInfo)
                {
                    hardwareUsageService = new HardwareUsageService();
                    hardwareUsageService.HWMonitor += (s, e) => {
                        try
                        {
                            if (isPaused)
                                return;

                            // CefSharp CanExecuteJavascriptInMainFrame equivalent in webview required?
                            this.Invoke((Action)(() =>
                            {
                                _ = webView.ExecuteScriptFunctionAsync("livelySystemInformation", JsonConvert.SerializeObject(e, Formatting.Indented));
                            }));
                        }
                        catch { }
                    };
                    hardwareUsageService.Start();
                }

                // Web wallpapers own their visualiser code, so they always get the feed.
                // Media pages are given the option instead: the loopback capture and FFT
                // stay off until the wallpaper actually raises the audio reactive slider.
                if (startArgs.AudioVisualizer && !isMediaWallpaper)
                    StartAudioVisualizer();
            }
        }

        private void StartAudioVisualizer()
        {
            if (visualizerService != null)
                return;

            try
            {
                visualizerService = new NAudioVisualizerService();
                visualizerService.AudioDataAvailable += (s, e) => {
                    try
                    {
                        if (isPaused)
                            return;

                        this.Invoke((Action)(() =>
                        {
                            _ = webView.ExecuteScriptFunctionAsync("livelyAudioListener", e);
                        }));
                    }
                    catch { }
                };
                visualizerService.Start(startArgs.AudioVisualizerDeviceId);
            }
            catch (Exception ex)
            {
                // No playback device, or the service is unavailable: the wallpaper still runs.
                visualizerService = null;
                ex.SendError(SendToParent, "Failed to start audio capture");
            }
        }

        /// <summary>
        /// Resumes the suspended CEF Direct3D rendering subprocess by detaching the debugger.
        /// Must be called on the same thread that previously attached, since debugger state is thread-specific.
        /// </summary>
        private void ResumeRenderingSubProcess()
        {
            if (cefD3DRenderingSubProcessId == 0)
                return;

            _ = NativeMethods.DebugActiveProcessStop((uint)cefD3DRenderingSubProcessId);
        }

        /// <summary>
        /// Suspends the CEF Direct3D rendering subprocess by attaching a debugger.
        /// Must be called on the same thread to ensure proper detachment later.
        /// </summary>
        private void SuspendRenderingSubProcess()
        {
            // The "System Idle Process" is given process ID 0, Kernel is 1.
            if (cefD3DRenderingSubProcessId == 0)
                return;

            // DebugSetProcessKillOnExit by default is TRUE.
            // Ref: https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-debugsetprocesskillonexit
            _ = NativeMethods.DebugActiveProcess((uint)cefD3DRenderingSubProcessId);
        }

        public async Task ListenToParent()
        {
            if (IsDebugging)
                return;

            var reader = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);

            try
            {
                await Task.Run(async () =>
                {
                    while (true)
                    {
                        // Since UTF8 is backward compatible, will work without this reader for non unicode characters.
                        string text = await reader.ReadLineAsync();
                        if (startArgs.VerboseLog)
                            Console.WriteLine(text);

                        if (string.IsNullOrEmpty(text))
                        {
                            // When the redirected stream is closed, a null line is sent to the event handler. 
                            break;
                        }
                        else
                        {
                            try
                            {
                                var obj = JsonConvert.DeserializeObject<IpcMessage>(text, new JsonSerializerSettings() { Converters = { new IpcMessageConverter() } });
                                if (obj.Type == MessageType.cmd_close)
                                    break;

                                // Ensure all processing is on the same thread.
                                // Some WebView2 calls require UI thread.
                                this.Invoke((Action)(async () =>
                                {
                                    try
                                    {
                                        await ProcessMessage(obj);
                                    }
                                    catch (Exception ie1)
                                    {
                                        ie1.SendError(SendToParent);
                                    }
                                }));
                            }
                            catch (Exception ie2)
                            {
                                ie2.SendError(SendToParent);
                            }
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                ex.SendError(SendToParent);
            }
            finally
            {
                reader?.Dispose();
                try
                {
                    this.Invoke((Action)Application.Exit);
                }
                catch (Exception ex)
                {
                    // Invoke throws once the form handle is gone; the caller waits on a task
                    // nobody observes, so without this the process keeps running as a wallpaper
                    // with no parent after Lively exits.
                    ex.SendError(SendToParent, "Graceful exit failed, forcing shutdown");
                    Environment.Exit(0);
                }
            }
        }

        private async Task ProcessMessage(IpcMessage obj)
        {
            switch (obj.Type)
            {
                case MessageType.cmd_reload:
                    // Web wallpapers keep their own state and some pages break on reload
                    // (ConnectionAborted), so only media is restarted here.
                    if (isMediaWallpaper)
                    {
                        try
                        {
                            webView.CoreWebView2.Reload();
                        }
                        catch (Exception ie)
                        {
                            ie.SendError(SendToParent, "Reload failed");
                        }
                    }
                    break;
                case MessageType.cmd_suspend:
                    await HandleSuspend();
                    break;
                case MessageType.cmd_resume:
                    await HandleResume();
                    break;
                case MessageType.cmd_volume:
                    var vc = (LivelyVolumeCmd)obj;
                    webView.CoreWebView2.IsMuted = vc.Volume == 0;
                    if (isMediaWallpaper)
                        await webView.ExecuteScriptFunctionAsync("livelyMediaVolume", vc.Volume);
                    break;
                case MessageType.cmd_seek:
                    var sc = (LivelySeekCmd)obj;
                    if (isMediaWallpaper)
                        await webView.ExecuteScriptFunctionAsync("livelyMediaSeek", sc.Percent, sc.IsRelative);
                    break;
                case MessageType.cmd_screenshot:
                    var success = true;
                    var scr = (LivelyScreenshotCmd)obj;
                    try
                    {
                        await webView.CaptureScreenshot(scr.Format, scr.FilePath);
                    }
                    catch (Exception ie)
                    {
                        success = false;
                        ie.SendError(SendToParent, "Failed to capture screenshot");
                    }
                    finally
                    {
                        SendToParent(new LivelyMessageScreenshot()
                        {
                            FileName = Path.GetFileName(scr.FilePath),
                            Success = success
                        });
                    }
                    break;
                case MessageType.lp_slider:
                    var sl = (LivelySlider)obj;
                    // Dragging the audio reactive slider is what switches the capture on,
                    // so the wallpaper does not have to be restarted for it to start pulsing.
                    if (isMediaWallpaper && string.Equals(sl.Name, "audio", StringComparison.OrdinalIgnoreCase) && sl.Value > 0)
                        StartAudioVisualizer();
                    await webView.ExecuteScriptFunctionAsync("livelyPropertyListener", sl.Name, sl.Value);
                    break;
                case MessageType.lp_textbox:
                    var tb = (LivelyTextBox)obj;
                    await webView.ExecuteScriptFunctionAsync("livelyPropertyListener", tb.Name, tb.Value);
                    break;
                case MessageType.lp_dropdown:
                    var dd = (LivelyDropdown)obj;
                    await webView.ExecuteScriptFunctionAsync("livelyPropertyListener", dd.Name, dd.Value);
                    break;
                case MessageType.lp_dropdown_scaler:
                    var sd = (LivelyDropdownScaler)obj;
                    await webView.ExecuteScriptFunctionAsync("livelyMediaScaler", sd.Value);
                    break;
                case MessageType.lp_cpicker:
                    var cp = (LivelyColorPicker)obj;
                    await webView.ExecuteScriptFunctionAsync("livelyPropertyListener", cp.Name, cp.Value);
                    break;
                case MessageType.lp_chekbox:
                    var cb = (LivelyCheckbox)obj;
                    await webView.ExecuteScriptFunctionAsync("livelyPropertyListener", cb.Name, cb.Value);
                    break;
                case MessageType.lp_fdropdown:
                    var fd = (LivelyFolderDropdown)obj;
                    var filePath = fd.Value is null ? null : Path.Combine(Path.GetDirectoryName(startArgs.Url), fd.Value);
                    await webView.ExecuteScriptFunctionAsync("livelyPropertyListener", fd.Name, File.Exists(filePath) ? fd.Value : null);
                    break;
                case MessageType.lp_button:
                    var btn = (LivelyButton)obj;
                    if (btn.IsDefault)
                        await RestoreLivelyProperties(startArgs.Properties);
                    else
                        await webView.ExecuteScriptFunctionAsync("livelyPropertyListener", btn.Name, true);
                    break;
                case MessageType.lsp_perfcntr:
                    await webView.ExecuteScriptFunctionAsync("livelySystemInformation", JsonConvert.SerializeObject(((LivelySystemInformation)obj).Info, Formatting.Indented));

                    break;
                case MessageType.lsp_nowplaying:
                    await webView.ExecuteScriptFunctionAsync("livelyCurrentTrack", JsonConvert.SerializeObject(((LivelySystemNowPlaying)obj).Info, Formatting.Indented));
                    break;
                case MessageType.cmd_close:
                    // Handle on caller
                    break;
            }
        }

        private async Task HandleSuspend()
        {
            SuspendRenderingSubProcess();
            if (!isPaused)
            {
                if (startArgs.PauseWebMedia || isVideoStream || isMediaWallpaper)
                    await webView.TryPauseMedia();

                if (startArgs.PauseEvent)
                    await webView.ExecuteScriptFunctionAsync("livelyWallpaperPlaybackChanged",
                        JsonConvert.SerializeObject(new WallpaperPlaybackState() { IsPaused = true }),
                        Formatting.Indented);
            }
            isPaused = true;
        }

        private async Task HandleResume()
        {
            ResumeRenderingSubProcess();
            if (isPaused)
            {
                if (startArgs.PauseWebMedia || isVideoStream || isMediaWallpaper)
                    await webView.TryPlayMedia();

                if (startArgs.PauseEvent)
                    await webView.ExecuteScriptFunctionAsync("livelyWallpaperPlaybackChanged",
                            JsonConvert.SerializeObject(new WallpaperPlaybackState() { IsPaused = false }),
                            Formatting.Indented);

                if (startArgs.NowPlaying)
                    await webView.ExecuteScriptFunctionAsync("livelyCurrentTrack", JsonConvert.SerializeObject(nowPlayingService?.CurrentTrack, Formatting.Indented));
            }
            isPaused = false;
        }

        private async Task RestoreLivelyProperties(string propertyPath)
        {
            try
            {
                await LivelyPropertyUtil.LoadProperty(propertyPath, Path.GetDirectoryName(startArgs.Url), async (key, value) =>
                {
                    // A media wallpaper that asks for audio is what turns the capture on,
                    // so a wallpaper that never touches the slider never costs anything.
                    if (isMediaWallpaper && string.Equals(key, "audio", StringComparison.OrdinalIgnoreCase) && IsPositiveValue(value))
                        StartAudioVisualizer();

                    await webView.ExecuteScriptFunctionAsync("livelyPropertyListener", key, value);
                });
            }
            catch (Exception ex)
            {
                ex.SendError(SendToParent);
            }
        }

        private static bool IsPositiveValue(object value)
        {
            return double.TryParse(value?.ToString(),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var number) && number > 0;
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            visualizerService?.Dispose();
            hardwareUsageService?.Stop();
            nowPlayingService?.Stop();
            webView?.Dispose();
        }

        private void SendToParent(IpcMessage obj)
        {
            if (!IsDebugging)
                Console.WriteLine(JsonConvert.SerializeObject(obj));

            Debug.WriteLine(JsonConvert.SerializeObject(obj));
        }

        private sealed class WallpaperPlaybackState
        {
            public bool IsPaused { get; set; }
        }
    }
}
