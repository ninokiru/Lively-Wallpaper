using ICSharpCode.SharpZipLib.Zip;
using Lively.Models;
using Lively.Models.Enums;
using System;
using System.IO;
using System.Linq;

namespace Lively.Common
{
    public static class FileTypes
    {
        // Formats are limited to what the WebView2 (Chromium) engine decodes natively.
        public static readonly FileTypeModel[] SupportedFormats = [
            new (WallpaperType.video, [".mp4", ".m4v", ".mov", ".webm", ".ogv" ]),
            new (WallpaperType.picture, [".jpg", ".jpeg", ".png",
                    ".bmp", ".tif", ".tiff", ".jfif" ]),
            // Rendered by the browser so animation is preserved.
            new (WallpaperType.gif, [".gif", ".webp", ".avif", ".apng", ".svg" ]),
            //new FileData(WallpaperType.heic, new string[] {".heic" }),//, ".heics", ".heif", ".heifs" }),
            new (WallpaperType.web, [".html"]),
            new (WallpaperType.webaudio, [".html"]),
            new (WallpaperType.app, [".exe"]),
            //new FileFilter(WallpaperType.unity,"*.exe"),
            //new FileFilter(WallpaperType.unityaudio,"Unity Audio Visualiser |*.exe"),
            new (WallpaperType.godot, [".exe"])
        ];

        /// <summary>
        /// Identify Lively wallpaper type from file information.
        /// <br>If more than one wallpapertype has same extension, first result is selected.</br>
        /// </summary>
        /// <param name="filePath">Path to file.</param>
        /// <returns>-1 if not supported, 100 if Lively .zip</returns>
        public static WallpaperType GetFileType(string filePath)
        {
            // Note: Use file header to verify filetype instead of extension in the future?
            var item = SupportedFormats.FirstOrDefault(
                x => x.Extentions.Any(y => y.Equals(Path.GetExtension(filePath), StringComparison.OrdinalIgnoreCase)));

            return item != null ? item.Type : (WallpaperType)(-1);
        }

        /// <summary>
        /// Verify whether the archive is lively wallpaper package.
        /// </summary>
        public static bool IsWallpaperPackage(string archivePath)
        {
            try
            {
                using Stream fsInput = File.OpenRead(archivePath);
                using var zf = new ZipFile(fsInput);

                return zf.FindEntry("LivelyInfo.json", true) != -1;
            }
            catch {
                return false;
            }
        }

        public static bool IsWallpaperPackageExtension(string filePath) 
            => Path.GetExtension(filePath).Equals(".zip", StringComparison.OrdinalIgnoreCase);
    }
}
