using Lively.Models.Enums;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;

namespace Lively.Common.Helpers
{
    public static class WebContentUtil
    {
        public static bool IsSupportedVideoStream(Uri uri)
        {
            bool status = false;
            string host, url, tmp = string.Empty;
            try
            {
                url = uri.AbsoluteUri;
                host = uri.Host;
            }
            catch
            {
                return status;
            }

            switch (host)
            {
                case "youtube.com":
                case "youtu.be":
                case "www.youtu.be":
                case "www.youtube.com":
                    if (TryParseYouTubeVideoIdFromUrl(url, ref tmp))
                        status = true;
                    break;
                case "www.bilibili.com":
                    if (url.Contains("bilibili.com/video/"))
                        status = true;
                    break;
            }
            return status;
        }

        public static bool IsSupportedVideoStream(string url)
        {
            try
            {
                return IsSupportedVideoStream(new Uri(url));
            }
            catch 
            { 
                return false; 
            }
        }

        // Ref: https://stackoverflow.com/questions/39777659/extract-the-video-id-from-youtube-url-in-net
        public static bool TryParseYouTubeVideoIdFromUrl(string url, ref string id)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                try
                {
                    uri = new UriBuilder("http", url).Uri;
                }
                catch
                {
                    // invalid url
                    return false;
                }
            }

            string host = uri.Host;
            string[] youTubeHosts = { "www.youtube.com", "youtube.com", "youtu.be", "www.youtu.be" };
            if (!youTubeHosts.Contains(host))
                return false;

            var query = HttpUtility.ParseQueryString(uri.Query);
            if (query.AllKeys.Contains("v"))
            {
                id = Regex.Match(query["v"], @"^[a-zA-Z0-9_-]{11}$").Value;
                return id != string.Empty;
            }
            else if (query.AllKeys.Contains("u"))
            {
                // some urls have something like "u=/watch?v=AAAAAAAAA16"
                id = Regex.Match(query["u"], @"/watch\?v=([a-zA-Z0-9_-]{11})").Groups[1].Value;
                return id != string.Empty;
            }
            else
            {
                // remove a trailing forward space
                var last = uri.Segments.Last().Replace("/", "");
                if (Regex.IsMatch(last, @"^v=[a-zA-Z0-9_-]{11}$"))
                {
                    id = last.Replace("v=", "");
                    return id != string.Empty;
                }

                string[] segments = uri.Segments;
                if (segments.Length > 2 && segments[segments.Length - 2] != "v/" && segments[segments.Length - 2] != "watch/")
                {
                    return false;
                }

                id = Regex.Match(last, @"^[a-zA-Z0-9_-]{11}$").Value;
                return id != string.Empty;
            }
        }

        public static bool TryParseShadertoyIdFromUrl(string url, ref string id)
        {
            if (!url.Contains("shadertoy.com/view"))
                return false;

            if (!LinkUtil.TrySanitizeUrl(url, out _))
                return false;

            try
            {
                var segments = url.Split(["/"], StringSplitOptions.RemoveEmptyEntries);
                int index = Array.IndexOf(segments, "view");

                if (index < 0 || index + 1 >= segments.Length)
                    return false;

                id = segments[index + 1];
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static (WebContentType ContentType, string Id) GetContentType(string url)
        {
            if (string.IsNullOrEmpty(url))
                return (WebContentType.none, null);

            string id = null;

            if (TryParseShadertoyIdFromUrl(url, ref id))
                return (WebContentType.shadertoy, id);

            if (TryParseYouTubeVideoIdFromUrl(url, ref id))
                return (WebContentType.youtube, id);

            return (WebContentType.generic, null);
        }
    }
}
