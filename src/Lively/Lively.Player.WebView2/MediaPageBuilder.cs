using Lively.Models.Enums;
using Newtonsoft.Json;
using System;

namespace Lively.Player.WebView2
{
    /// <summary>
    /// Builds the in-memory host page used by video, gif and image wallpapers.
    /// Media is rendered by Chromium so every wallpaper type shares one ipc surface,
    /// and LivelyProperties works for media the same way it does for web wallpapers.
    /// </summary>
    public static class MediaPageBuilder
    {
        public static string Build(WebPageType kind, Uri mediaUri, WallpaperScaler scaler, int volume)
        {
            var fit = ToObjectFit(scaler);
            var isVideo = kind is WebPageType.video;
            var src = JsonConvert.SerializeObject(mediaUri.AbsoluteUri);
            var element = isVideo
                ? "<video id=\"livelyMedia\" autoplay loop playsinline preload=\"auto\"></video>"
                : "<img id=\"livelyMedia\" alt=\"\">";
            var volumeInit = isVideo
                ? $"media.volume = {(Math.Clamp(volume, 0, 100) / 100d).ToString(System.Globalization.CultureInfo.InvariantCulture)};"
                : string.Empty;

            return $$"""
                <!doctype html>
                <html lang="en">
                <head>
                    <meta charset="utf-8">
                    <style>
                        html, body { margin: 0; padding: 0; width: 100%; height: 100%; overflow: hidden; background: transparent; }
                        #livelyMedia { display: block; width: 100%; height: 100%; object-fit: {{fit}}; object-position: center; }
                    </style>
                </head>
                <body>
                    {{element}}
                    <svg width="0" height="0" style="position:absolute" aria-hidden="true">
                        <filter id="livelyGamma" color-interpolation-filters="sRGB">
                            <feComponentTransfer>
                                <feFuncR id="livelyGammaR" type="gamma" amplitude="1" exponent="1" offset="0" />
                                <feFuncG id="livelyGammaG" type="gamma" amplitude="1" exponent="1" offset="0" />
                                <feFuncB id="livelyGammaB" type="gamma" amplitude="1" exponent="1" offset="0" />
                            </feComponentTransfer>
                        </filter>
                    </svg>
                    <script>
                        const media = document.getElementById('livelyMedia');
                        media.src = {{src}};
                        {{volumeInit}}

                        const fx = { hue: 0, saturate: 1, brightness: 1, contrast: 1, gamma: 1 };
                        const gammaCurves = ['livelyGammaR', 'livelyGammaG', 'livelyGammaB'].map((id) => document.getElementById(id));
                        // Index order matches WallpaperScaler: none, fill, uniform, uniformFill, auto.
                        const scaleNames = ['none', 'fill', 'contain', 'cover', 'cover'];
                        // Wallpapers written before the scaler dropdown accept the enum name instead.
                        const scaleAliases = { none: 'none', fill: 'fill', uniform: 'contain', uniformfill: 'cover', autofit: 'cover', auto: 'cover', contain: 'contain', cover: 'cover' };

                        function applyFilters() {
                            // SVG gamma curve only when used, it renders slower than plain css filters.
                            const gamma = Math.abs(fx.gamma - 1) < 0.001 ? null : (1 / fx.gamma);
                            if (gamma !== null)
                                gammaCurves.forEach((curve) => curve.setAttribute('exponent', gamma));
                            media.style.filter = (gamma === null ? '' : 'url(#livelyGamma) ')
                                + 'hue-rotate(' + fx.hue + 'deg)'
                                + ' saturate(' + fx.saturate + ')'
                                + ' brightness(' + fx.brightness + ')'
                                + ' contrast(' + fx.contrast + ')';
                        }

                        function clampFraction(value, span) {
                            return 1 + (Math.min(100, Math.max(-100, Number(value) || 0)) / span);
                        }

                        function reportError(detail) {
                            try { chrome.webview.postMessage(JSON.stringify({ Type: 1, Category: 1, Message: detail })); } catch (e) { }
                        }
                        media.addEventListener('error', () => reportError('Media failed to load: ' + media.currentSrc));

                        // Called by the core when the wallpaper is restored/changed.
                        window.livelyPropertyListener = (name, value) => {
                            switch (name) {
                                case 'hue': fx.hue = (Math.min(100, Math.max(-100, Number(value) || 0)) * 3.6); break;
                                case 'saturation': fx.saturate = clampFraction(value, 100); break;
                                case 'brightness': fx.brightness = clampFraction(value, 100); break;
                                case 'contrast': fx.contrast = clampFraction(value, 100); break;
                                case 'gamma': fx.gamma = clampFraction(value, 200); break;
                                case 'speed': if (media.tagName === 'VIDEO') media.playbackRate = Math.max(0.0625, Number(value) || 1); break;
                                case 'mute': media.muted = Boolean(value); break;
                                case 'scaler': window.livelyMediaScaler(value); break;
                                default: return;
                            }
                            applyFilters();
                        };

                        window.livelyMediaScaler = (value) => {
                            const name = typeof value === 'number'
                                ? scaleNames[Math.trunc(value)]
                                : scaleAliases[String(value).toLowerCase().replace(/\s+/g, '')];
                            if (name) media.style.objectFit = name;
                        };

                        window.livelyMediaVolume = (value) => {
                            const fraction = Math.min(100, Math.max(0, Number(value) || 0)) / 100;
                            media.volume = fraction;
                            media.muted = fraction === 0;
                        };

                        window.livelyMediaSeek = (percent, isRelative) => {
                            if (!isFinite(media.duration)) return;
                            const fraction = Math.min(100, Math.max(0, Number(percent) || 0)) / 100;
                            const target = isRelative ? media.currentTime + (fraction * media.duration) : fraction * media.duration;
                            media.currentTime = Math.min(media.duration, Math.max(0, target));
                        };

                        window.livelyWallpaperPlaybackChanged = (payload) => {
                            // The host passes the state as a json string, see Form1.HandleSuspend.
                            let state = payload;
                            if (typeof payload === 'string') { try { state = JSON.parse(payload); } catch (e) { return; } }
                            if (!state) return;
                            if (state.IsPaused) media.pause(); else media.play().catch(() => { });
                        };

                        // Api stubs: media wallpapers report no audio/hardware telemetry.
                        window.livelySystemInformation = () => { };
                        window.livelyCurrentTrack = () => { };
                        window.livelyAudioListener = () => { };
                    </script>
                </body>
                </html>
                """;
        }

        private static string ToObjectFit(WallpaperScaler scaler) => scaler switch
        {
            WallpaperScaler.none => "none",
            WallpaperScaler.fill => "fill",
            WallpaperScaler.uniform => "contain",
            WallpaperScaler.uniformFill => "cover",
            WallpaperScaler.auto => "cover",
            _ => "cover",
        };
    }
}
