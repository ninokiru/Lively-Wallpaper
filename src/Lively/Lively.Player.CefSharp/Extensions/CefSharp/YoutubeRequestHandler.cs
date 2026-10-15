using CefSharp;
using CefSharp.Handler;

namespace Lively.Player.CefSharp.Extensions.CefSharp
{
    internal class YoutubeRequestHandler : RequestHandler
    {
        protected override IResourceRequestHandler GetResourceRequestHandler(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, bool isNavigation, bool isDownload, string requestInitiator, ref bool disableDefaultHandling)
        {
            return new YoutubeResourceRequestHandler();
        }
    }
}
