using CefSharp;
using CefSharp.Handler;
using System;
using System.Linq;

namespace Lively.Player.CefSharp.Extensions.CefSharp
{
    public class YoutubeResourceRequestHandler : ResourceRequestHandler
    {
        protected override CefReturnValue OnBeforeResourceLoad(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, IRequestCallback callback)
        {
            request.SetReferrer("https://localhost/", ReferrerPolicy.Default);
            return CefReturnValue.Continue;
        }
    }
}
