using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Lively.Common;
using Lively.Common.Helpers;
using Lively.Common.Services;
using Lively.Grpc.Common.Proto.Update;
using Lively.Models.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Lively.RPC
{
    internal class AppUpdateServer : UpdateService.UpdateServiceBase
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        private readonly IAppUpdaterService updater;
        private readonly IDownloadService downloader;

        public AppUpdateServer(IAppUpdaterService updater, IDownloadService downloader)
        {
            this.updater = updater;
            this.downloader = downloader;
        }

        public override async Task<Empty> CheckUpdate(Empty _, ServerCallContext context)
        {
            await updater.CheckUpdate(0);
            return await Task.FromResult(new Empty());
        }

        public override async Task<GetLatestReleaseResponse> GetLatestRelease(GetLatestReleaseRequest request, ServerCallContext context)
        {
            try
            {
                var (SetupUri, SetupFileName, SetupVersion) = await updater.GetLatestRelease(request.Channel == ReleaseChannel.Beta);

                return await Task.FromResult(new GetLatestReleaseResponse()
                {
                    Url = SetupUri?.OriginalString ?? string.Empty,
                    FileName = SetupFileName ?? string.Empty,
                    Version = SetupVersion?.ToString() ?? string.Empty
                });
            }
            catch (Exception ex)
            {
                throw new RpcException(new Status(StatusCode.Internal, $"Update lookup failed: {ex.Message}"));
            }
        }

        public override Task<Empty> StartUpdate(Empty _, ServerCallContext context)
        {
            if (updater.Status != AppUpdateStatus.available)
                return Task.FromResult(new Empty());

            try
            {
                try
                {
                    // Main user interface downloads the setup.
                    var fileName = updater.LastCheckFileName;
                    var filePath = Path.Combine(Constants.CommonPaths.TempDir, fileName);
                    if (!File.Exists(filePath))
                        throw new FileNotFoundException(filePath);

                    // Run setup in silent mode.
                    Process.Start(filePath, "/SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS");
                    // Inno installer will auto retry, waiting for application exit.
                    App.QuitApp();
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new ThreadStart(delegate
                    {
                        MessageBox.Show($"{Properties.Resources.LivelyExceptionAppUpdateFail}\n\nException:\n{ex}", Properties.Resources.TextError, MessageBoxButton.OK, MessageBoxImage.Error);
                    }));
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
            }
            return Task.FromResult(new Empty());
        }

        public override async Task<Empty> SwitchReleaseChannel(SwitchReleaseChannelRequest request, ServerCallContext context)
        {
            try
            {
                if (PackageUtil.IsRunningAsPackaged)
                    throw new InvalidOperationException("msix not supported.");

                var isRequestedBetaChannel = request.Channel == ReleaseChannel.Beta;
                var isCurrentBetaChannel = Constants.ApplicationType.IsTestBuild;

                if (isCurrentBetaChannel && isRequestedBetaChannel || !(isCurrentBetaChannel || isRequestedBetaChannel))
                    return await Task.FromResult(new Empty());

                var (SetupUri, SetupFileName, _) = await updater.GetLatestRelease(isRequestedBetaChannel);
                var filePath = Path.Combine(Constants.CommonPaths.TempDir, SetupFileName);

                await downloader.DownloadFile(SetupUri, filePath, null, context.CancellationToken);
                // Run setup in silent mode.
                Process.Start(filePath, "/SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS");
                // Inno installer will auto retry, waiting for application exit.
                App.QuitApp();
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                throw new RpcException(new Status(StatusCode.Internal, ex.Message));
            }
            return await Task.FromResult(new Empty());
        }

        public override Task<UpdateResponse> GetUpdateStatus(Empty _, ServerCallContext context)
        {
            return Task.FromResult(new UpdateResponse()
            {
                Status = (UpdateStatus)((int)updater.Status),
                Changelog = string.Empty,
                Url = updater.LastCheckUri?.OriginalString ?? string.Empty,
                FileName = updater.LastCheckFileName ?? string.Empty,
                Version = updater.LastCheckVersion?.ToString() ?? string.Empty,
                Time = Timestamp.FromDateTime(updater.LastCheckTime.ToUniversalTime()),
            });
        }

        public override async Task SubscribeUpdateChecked(Empty _, IServerStreamWriter<Empty> responseStream, ServerCallContext context)
        {
            try
            {
                while (!context.CancellationToken.IsCancellationRequested)
                {
                    var tcs = new TaskCompletionSource<bool>();
                    updater.UpdateChecked += Updater_UpdateChecked;
                    void Updater_UpdateChecked(object sender, AppUpdaterEventArgs e)
                    {
                        updater.UpdateChecked -= Updater_UpdateChecked;
                        tcs.TrySetResult(true);
                    }
                    using var item = context.CancellationToken.Register(() => { tcs.TrySetResult(false); });
                    await tcs.Task;

                    if (context.CancellationToken.IsCancellationRequested)
                    {
                        updater.UpdateChecked -= Updater_UpdateChecked;
                        break;
                    }

                    await responseStream.WriteAsync(new Empty());
                }
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }
    }
}
