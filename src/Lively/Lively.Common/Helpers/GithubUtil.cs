using Octokit;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Lively.Common.Helpers
{
    public static class GithubUtil
    {
        /// <summary>
        /// Get latest release from github after given delay.
        /// Returns null when the repository has no published release yet.
        /// </summary>
        public static async Task<Release> GetLatestRelease(string userName, string repositoryName)
        {
            var client = new GitHubClient(new ProductHeaderValue(repositoryName));
            var releases = await client.Repository.Release.GetAll(userName, repositoryName);
            return releases.Count == 0 ? null : releases[0];
        }

        public static (string fileName, string url) GetAssetUrl(Release release, string assetName)
        {
            var asset = release.Assets.FirstOrDefault(x => Contains(x.Name, assetName, StringComparison.OrdinalIgnoreCase));
            return (asset?.Name, asset?.BrowserDownloadUrl);
        }

        public static Version GetVersion(Release release)
        {
            // Suffixes such as v0.0.2-beta leave a trailing separator that Version() rejects.
            var sanitized = Regex.Replace(release.TagName, "[^0-9.]", "");
            return sanitized.Length == 0 ? new Version(0, 0, 0, 0) : new Version(sanitized);
        }

        public static int CompareAssemblyVersion(Version version)
        {
            var appVersion = System.Reflection.Assembly.GetEntryAssembly().GetName().Version;
            return Normalize(version).CompareTo(Normalize(appVersion));
        }

        /// <summary>
        /// Release tags usually carry three parts while the assembly always has four,
        /// and a three part Version reports Revision as -1.
        /// </summary>
        private static Version Normalize(Version version)
        {
            return new Version(version.Major, version.Minor,
                Math.Max(version.Build, 0), Math.Max(version.Revision, 0));
        }

        /// <summary>
        /// String Contains method with StringComparison property.
        /// </summary>
        private static bool Contains(String str, String substring,
                                    StringComparison comp)
        {
            if (substring == null)
                throw new ArgumentNullException("substring",
                                             "substring cannot be null.");
            else if (!Enum.IsDefined(typeof(StringComparison), comp))
                throw new ArgumentException("comp is not a member of StringComparison",
                                         "comp");

            return str.IndexOf(substring, comp) >= 0;
        }
    }
}
