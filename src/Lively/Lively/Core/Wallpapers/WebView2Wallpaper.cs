using Lively.Common;
using Lively.Common.Exceptions;
using Lively.Common.Extensions;
using Lively.Common.Helpers;
using Lively.Common.Helpers.Pinvoke;
using Lively.Common.JsonConverters;
using Lively.Models;
using Lively.Models.Enums;
using Lively.Models.Message;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Lively.Core.Wallpapers
{
    /// <summary>
    /// The single player engine: web, stream, video, gif and picture wallpapers
    /// all run through the WebView2 plugin process.
    /// </summary>
    public class WebView2Wallpaper : IWallpaper
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        private readonly TaskCompletionSource<Exception> tcsProcessWait = new();
        private readonly TaskCompletionSource contentReadyTcs = new();
        private readonly Process process;
        private static int globalCount;
        private readonly int uniqueId;
        private readonly bool isMediaWallpaper;
        private int currentVolume = 0;
        private bool isInitialized;
        private bool isMuted;

        public event EventHandler Exited;
        public event EventHandler Loaded;

        public bool IsLoaded { get; private set; } = false;
        public WallpaperType Category => Model.LivelyInfo.Type;
        public LibraryModel Model { get; }
        public IntPtr Handle { get; private set; }
        public IntPtr InputHandle { get; private set; }
        public int? Pid { get; private set; } = null;
        public DisplayMonitor Screen { get; set; }
        public string LivelyPropertyCopyPath { get; }
        public bool IsExited { get; private set; }

        public WebView2Wallpaper(string path,
            LibraryModel model,
            DisplayMonitor display,
            string livelyPropertyPath,
            string userDataDir,
            AppTheme theme,
            int volume,
            string debugPort = null,
            double? scale = null,
            string audioVisualizerId = null,
            WallpaperScaler scaler = WallpaperScaler.fill)
        {
            var filePath = ResolveFilePath(path, model);
            var type = model.LivelyInfo.Type;
            isMediaWallpaper = type.IsMediaWallpaper() && type != WallpaperType.videostream;
            LivelyPropertyCopyPath = livelyPropertyPath;

            var cmdArgs = new StringBuilder();
            cmdArgs.Append(" --wallpaper-pause-media");
            cmdArgs.Append(" --wallpaper-volume " + volume);
            cmdArgs.Append(" --wallpaper-color-scheme " + theme);
            cmdArgs.Append(" --wallpaper-display " + "\"" + display.DeviceId + "\"");
            cmdArgs.Append(" --wallpaper-geometry " + display.Bounds.Width + "x" + display.Bounds.Height);
            cmdArgs.Append(" --wallpaper-user-data " + "\"" + userDataDir + "\"");
            cmdArgs.Append(" --wallpaper-url " + "\"" + filePath + "\"");
            cmdArgs.Append(" --wallpaper-property " + "\"" + LivelyPropertyCopyPath + "\"");
            if (scale != null)
                cmdArgs.Append(" --wallpaper-scale " + JsonConvert.SerializeObject(scale.Value));
            if (!isMediaWallpaper)
            {
                // --wallpaper-audio false: https://github.com/commandlineparser/commandline/issues/702
                if (type == WallpaperType.webaudio)
                    cmdArgs.Append(" --wallpaper-audio true");
                if (!string.IsNullOrEmpty(audioVisualizerId))
                    cmdArgs.Append(" --wallpaper-audio-id " + audioVisualizerId);
                if (!string.IsNullOrWhiteSpace(debugPort))
                    cmdArgs.Append(" --wallpaper-debug " + debugPort);
                cmdArgs.Append(type.IsOnlineWallpaper() ? " --wallpaper-type online" : " --wallpaper-type local");
            }
            else
            {
                cmdArgs.Append(" --wallpaper-type " + MediaPageTypeOf(type));
                cmdArgs.Append(" --wallpaper-scaler " + scaler);
            }
            if (TryParseUserCommandArgs(model.LivelyInfo.Arguments, out string parsedArgs))
                cmdArgs.Append(" " + parsedArgs);

            this.process = new Process
            {
                EnableRaisingEvents = true,
                StartInfo = new ProcessStartInfo
                {
                    Arguments = cmdArgs.ToString(),
                    FileName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Constants.PlayerPartialPaths.WebView2Path),
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = false,
                    UseShellExecute = false,
                    StandardInputEncoding = Encoding.UTF8,
                    WorkingDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Constants.PlayerPartialPaths.WebView2Dir)
                },
            };
            this.Model = model;
            this.Screen = display;

            //for logging purpose
            uniqueId = globalCount++;
        }

        private static string MediaPageTypeOf(WallpaperType type) => type switch
        {
            WallpaperType.video => WebPageType.video.ToString(),
            WallpaperType.gif => WebPageType.gif.ToString(),
            _ => WebPageType.image.ToString(),
        };

        public void Close()
        {
            if (IsExited)
                return;

            SendMessage(new LivelyCloseCmd());
        }

        public void Pause()
        {
            if (!IsContentReady)
                return;

            SendMessage(new LivelySuspendCmd());
        }

        public void Play()
        {
            if (!IsContentReady)
                return;

            SendMessage(new LivelyResumeCmd());
        }

        private void SendMessage(string msg)
        {
            if (IsExited)
                return;

            try
            {
                process?.StandardInput.WriteLine(msg);
            }
            catch (Exception e)
            {
                Logger.Error($"Stdin write fail: {e.Message}");
            }
        }

        public void SendMessage(IpcMessage obj)
        {
            SendMessage(JsonConvert.SerializeObject(obj));
        }

        public void SetVolume(int volume)
        {
            currentVolume = volume;

            if (!isMuted)
                SendMessage(new LivelyVolumeCmd() { Volume = volume });
        }

        public void SetMute(bool mute)
        {
            isMuted = mute;
            // WebView2 only exposes a mute flag, volume is applied inside the media page.
            if (isMuted)
                SendMessage(new LivelyVolumeCmd() { Volume = 0 });
            else
                SendMessage(new LivelyVolumeCmd { Volume = currentVolume });
        }

        public async Task ShowAsync()
        {
            if (process is null)
                return;

            try
            {
                process.Exited += Proc_Exited;
                process.OutputDataReceived += Proc_OutputDataReceived;
                process.Start();
                Pid = process.Id;
                process.BeginOutputReadLine();

                await tcsProcessWait.Task;
                if (tcsProcessWait.Task.Result is not null)
                    throw tcsProcessWait.Task.Result;
            }
            catch (Exception)
            {
                Terminate();

                throw;
            }
        }

        private void Proc_Exited(object sender, EventArgs e)
        {
            Logger.Info($"Wv2{uniqueId}: Process exited with exit code: {process?.ExitCode}");
            if (!isInitialized)
            {
                // 2 = ERROR_FILE_NOT_FOUND (missing webview2 runtime), 87 = ERROR_INVALID_PARAMETER
                // Ref: <https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--0-499->
                if (process is not null && process.ExitCode == 2)
                    tcsProcessWait.TrySetResult(new WallpaperWebView2NotFoundException());
                else if (process is not null && process.ExitCode == 87)
                    tcsProcessWait.TrySetResult(new WallpaperPluginException("Error initializing. Unknown options are passed."));
                else
                    tcsProcessWait.TrySetResult(new InvalidOperationException(Properties.Resources.LivelyExceptionGeneral));
            }
            process.OutputDataReceived -= Proc_OutputDataReceived;
            process?.Dispose();
            IsExited = true;
            Exited?.Invoke(this, EventArgs.Empty);
        }

        private async void Proc_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            //When the redirected stream is closed, a null line is sent to the event handler.
            if (!string.IsNullOrEmpty(e.Data))
            {
                IpcMessage obj = null;
                try
                {
                    obj = JsonConvert.DeserializeObject<IpcMessage>(e.Data, new JsonSerializerSettings() { Converters = { new IpcMessageConverter() } });
                }
                catch (Exception ex)
                {
                    Logger.Error($"Wv2{uniqueId}: Ipc parse error: {e.Data}.\n\nException: {ex.Message}");
                }

                if (obj is null)
                    return;

                switch (obj.Type)
                {
                    case MessageType.msg_console:
                        var msg = obj as LivelyMessageConsole;
                        switch (msg.Category)
                        {
                            case ConsoleMessageType.error:
                                Logger.Error($"Wv2{uniqueId}: {msg.Message}");
                                break;
                            default:
                                Logger.Info($"Wv2{uniqueId}: {msg.Message}");
                                break;
                        }
                        break;
                    default:
                        Logger.Info($"Wv2{uniqueId}: {e.Data}");
                        break;
                }

                switch (obj.Type)
                {
                    case MessageType.msg_hwnd:
                        if (!isInitialized)
                        {
                            Exception error = null;
                            try
                            {
                                var handle = new IntPtr(((LivelyMessageHwnd)obj).Hwnd);
                                //WindowsForms10.Window.8.app.0.141b42a_r9_ad1
                                var chrome_WidgetWin_0 = NativeMethods.FindWindowEx(handle, IntPtr.Zero, "Chrome_WidgetWin_0", null);
                                if (!chrome_WidgetWin_0.Equals(IntPtr.Zero))
                                {
                                    this.InputHandle = NativeMethods.FindWindowEx(chrome_WidgetWin_0, IntPtr.Zero, "Chrome_WidgetWin_1", null);
                                }
                                Handle = process.GetProcessWindow(true);

                                if (IntPtr.Equals(Handle, IntPtr.Zero) || IntPtr.Equals(InputHandle, IntPtr.Zero))
                                    throw new Exception("Browser input/window handle NULL.");
                            }
                            catch (Exception ie)
                            {
                                error = ie;
                            }
                            finally
                            {
                                isInitialized = true;
                                tcsProcessWait.TrySetResult(error);
                            }
                        }
                        break;
                    case MessageType.msg_wploaded:
                        if (!IsLoaded)
                        {
                            IsLoaded = true;
                            Loaded?.Invoke(this, EventArgs.Empty);

                            // Wait before pausing or other internal fn since some pages can have transition.
                            await Task.Delay(1000);
                            if (!IsExited)
                                contentReadyTcs.TrySetResult();
                            else
                                contentReadyTcs.TrySetException(new InvalidOperationException("Process exited."));
                        }
                        break;
                }
            }
        }

        public void Terminate()
        {
            if (IsExited)
                return;

            try
            {
                process.Kill();
            }
            catch { }
        }

        public void SetPlaybackPos(float pos, PlaybackPosType type)
        {
            if (isMediaWallpaper)
            {
                SendMessage(new LivelySeekCmd
                {
                    Percent = pos,
                    IsRelative = type == PlaybackPosType.relativePercent
                });
                return;
            }

            if (pos == 0 && type != PlaybackPosType.relativePercent)
            {
                SendMessage(new LivelyReloadCmd());
            }
        }

        public async Task ScreenCapture(string filePath)
        {
            await WaitForContentReadyAsync(TimeSpan.FromSeconds(5));

            var tcs = new TaskCompletionSource();
            void LocalOutputDataReceived(object sender, DataReceivedEventArgs e)
            {
                if (string.IsNullOrEmpty(e.Data))
                {
                    tcs.TrySetException(new InvalidOperationException("Process exited unexpectedly."));
                }
                else
                {
                    var obj = JsonConvert.DeserializeObject<IpcMessage>(e.Data, new JsonSerializerSettings() { Converters = { new IpcMessageConverter() } });
                    if (obj.Type == MessageType.msg_screenshot)
                    {
                        var msg = (LivelyMessageScreenshot)obj;
                        if (msg.FileName == Path.GetFileName(filePath))
                        {
                            process.OutputDataReceived -= LocalOutputDataReceived;
                            if (msg.Success)
                                tcs.TrySetResult();
                            else
                                tcs.TrySetException(new InvalidOperationException($"Failed to take screenshot."));
                        }
                    }
                }
            }
            process.OutputDataReceived += LocalOutputDataReceived;

            Logger.Info($"Wv2{uniqueId}: Taking screenshot: {filePath}");
            SendMessage(new LivelyScreenshotCmd()
            {
                FilePath = Path.GetExtension(filePath) != ".jpg" ? filePath + ".jpg" : filePath,
                Format = ScreenshotFormat.jpeg,
                Delay = 0 //unused
            });

            // Timeout
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using (cts.Token.Register(() =>
            {
                if (!IsExited)
                    process.OutputDataReceived -= LocalOutputDataReceived;

                tcs.TrySetException(new TimeoutException($"Screenshot timed out."));
            }))

            await tcs.Task;
        }

        public void Dispose()
        {
            // Process object is disposed in Exit event.
            Terminate();
        }

        private async Task WaitForContentReadyAsync(TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            await contentReadyTcs.Task.WaitAsync(cts.Token);
        }

        private bool IsContentReady => contentReadyTcs.Task.IsCompleted;

        private static string ResolveFilePath(string path, LibraryModel model)
        {
            var filePath = path;
            if (PackageUtil.IsRunningAsPackaged)
            {
                try
                {
                    if (!model.LivelyInfo.Type.IsOnlineWallpaper())
                        filePath = PackageUtil.ValidateAndResolvePath(path);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex);
                }
            }

            return filePath;
        }

        /// <summary>
        /// Backward compatibility, appends --wallpaper to arguments if required.
        /// </summary>
        private static bool TryParseUserCommandArgs(string args, out string result)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                result = null;
                return false;
            }

            var words = args.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                if (words[i].StartsWith("--"))
                {
                    words[i] = string.Concat("--wallpaper-", words[i].AsSpan(2));
                }
            }
            result = string.Join(" ", words);
            return true;
        }
    }
}
