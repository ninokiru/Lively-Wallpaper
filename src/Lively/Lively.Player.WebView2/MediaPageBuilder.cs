using Lively.Models.Enums;
using Newtonsoft.Json;
using System;

namespace Lively.Player.WebView2
{
    /// <summary>
    /// Builds the in-memory host page used by video, gif and image wallpapers.
    /// Media is rendered by Chromium so every wallpaper type shares one ipc surface,
    /// and LivelyProperties works for media the same way it does for web wallpapers.
    /// The page also carries the effect stack (blur, grading, vignette, chromatic
    /// aberration, water ripple), the mouse parallax layer and the audio reactive
    /// drive; every one of them is a LivelyProperties slider, so they are all off
    /// until a wallpaper asks for them and cost nothing when untouched.
    /// </summary>
    public static class MediaPageBuilder
    {
        public static string Build(WebPageType kind, Uri mediaUri, WallpaperScaler scaler, int volume, int frameLimit)
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
            // 0 keeps Chromium at the monitor refresh rate, anything else paces the
            // animation loop the effects run in.
            var frameLimitMs = frameLimit > 0
                ? (1000d / frameLimit).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "0";

            return $$"""
                <!doctype html>
                <html lang="en">
                <head>
                    <meta charset="utf-8">
                    <style>
                        html, body { margin: 0; padding: 0; width: 100%; height: 100%; overflow: hidden; background: transparent; }
                        /* The stage owns the svg filter chain, the media the css fast path filters. */
                        #livelyStage { position: fixed; inset: 0; overflow: hidden; }
                        #livelyMedia { display: block; width: 100%; height: 100%; object-fit: {{fit}}; object-position: center; transform-origin: center center; }
                        #livelyVignette { position: fixed; inset: 0; pointer-events: none; opacity: 0;
                            background: radial-gradient(ellipse 90% 80% at center, rgba(0,0,0,0) 38%, rgba(0,0,0,0.42) 74%, rgba(0,0,0,0.92) 100%); }
                    </style>
                </head>
                <body>
                    <div id="livelyStage">
                        {{element}}
                    </div>
                    <div id="livelyVignette"></div>
                    <svg width="0" height="0" style="position:absolute" aria-hidden="true">
                        <filter id="livelyGamma" color-interpolation-filters="sRGB">
                            <feComponentTransfer>
                                <feFuncR id="livelyGammaR" type="gamma" amplitude="1" exponent="1" offset="0" />
                                <feFuncG id="livelyGammaG" type="gamma" amplitude="1" exponent="1" offset="0" />
                                <feFuncB id="livelyGammaB" type="gamma" amplitude="1" exponent="1" offset="0" />
                            </feComponentTransfer>
                        </filter>
                        <!-- Split the channels, shift red and blue in opposite directions, screen them back. -->
                        <filter id="livelyChroma" color-interpolation-filters="sRGB" x="-10%" y="-10%" width="120%" height="120%">
                            <feColorMatrix in="SourceGraphic" type="matrix" values="1 0 0 0 0  0 0 0 0 0  0 0 0 0 0  0 0 0 1 0" result="chR" />
                            <feColorMatrix in="SourceGraphic" type="matrix" values="0 0 0 0 0  0 1 0 0 0  0 0 0 0 0  0 0 0 1 0" result="chG" />
                            <feColorMatrix in="SourceGraphic" type="matrix" values="0 0 0 0 0  0 0 0 0 0  0 0 1 0 0  0 0 0 1 0" result="chB" />
                            <feOffset id="livelyChromaR" in="chR" dx="0" dy="0" result="offR" />
                            <feOffset id="livelyChromaB" in="chB" dx="0" dy="0" result="offB" />
                            <feBlend in="offR" in2="chG" mode="screen" result="rg" />
                            <feBlend in="rg" in2="offB" mode="screen" />
                        </filter>
                        <!-- Turbulence is generated once per change, the scroll offset makes the ripple travel. -->
                        <filter id="livelyRipple" color-interpolation-filters="sRGB" x="-20%" y="-20%" width="140%" height="140%">
                            <feTurbulence id="livelyNoise" type="fractalNoise" baseFrequency="0.012 0.021" numOctaves="2" seed="3" result="noise" />
                            <feOffset id="livelyNoiseScroll" in="noise" dx="0" dy="0" result="flow" />
                            <feDisplacementMap id="livelyWarp" in="SourceGraphic" in2="flow" scale="0" xChannelSelector="R" yChannelSelector="G" />
                        </filter>
                    </svg>
                    <script>
                        const media = document.getElementById('livelyMedia');
                        const stage = document.getElementById('livelyStage');
                        const shade = document.getElementById('livelyVignette');
                        media.src = {{src}};
                        {{volumeInit}}

                        // Index order matches WallpaperScaler: none, fill, uniform, uniformFill, auto.
                        const scaleNames = ['none', 'fill', 'contain', 'cover', 'cover'];
                        // Wallpapers written before the scaler dropdown accept the enum name instead.
                        const scaleAliases = { none: 'none', fill: 'fill', uniform: 'contain', uniformfill: 'cover', autofit: 'cover', auto: 'cover', contain: 'contain', cover: 'cover' };

                        // Colour keeps the -100..100 slider mapping, overlays are normalised to 0..1.
                        const fx = { hue: 0, saturate: 1, brightness: 1, contrast: 1, gamma: 1, temp: 0, blur: 0, vignette: 0, chroma: 0, ripple: 0, parallax: 0, audio: 0 };
                        const gammaCurves = ['livelyGammaR', 'livelyGammaG', 'livelyGammaB'].map((id) => document.getElementById(id));
                        const chromaR = document.getElementById('livelyChromaR');
                        const chromaB = document.getElementById('livelyChromaB');
                        const noiseScroll = document.getElementById('livelyNoiseScroll');
                        const warp = document.getElementById('livelyWarp');

                        const frameLimitMs = {{frameLimitMs}};
                        let rafId = 0;
                        let lastPaint = 0;
                        let lastMove = 0;
                        const pointer = { x: 0, y: 0, easeX: 0, easeY: 0 };
                        const pulse = { bass: 0, mid: 0, high: 0, peak: 0.00001, ready: false, zoom: 0, sat: 1, bright: 1 };

                        function needsMotion() {
                            return fx.parallax > 0 || fx.ripple > 0 || fx.audio > 0;
                        }

                        function kick() {
                            if (needsMotion() && rafId === 0)
                                rafId = requestAnimationFrame(animate);
                        }

                        function animate(now) {
                            rafId = 0;
                            if (!needsMotion()) {
                                media.style.transform = 'none';
                                return;
                            }
                            rafId = requestAnimationFrame(animate);
                            if (frameLimitMs > 0 && now - lastPaint < frameLimitMs - 1)
                                return;
                            lastPaint = now;
                            step(now);
                        }

                        function step(now) {
                            if (fx.parallax > 0) {
                                // The core only forwards moves the desktop receives, so drift
                                // back to the centre when the pointer stops arriving.
                                if (now - lastMove > 2000) {
                                    pointer.x = 0;
                                    pointer.y = 0;
                                }
                                pointer.easeX += (pointer.x - pointer.easeX) * 0.08;
                                pointer.easeY += (pointer.y - pointer.easeY) * 0.08;
                            } else {
                                pointer.easeX = 0;
                                pointer.easeY = 0;
                            }

                            if (fx.audio > 0 && pulse.ready) {
                                const b = Math.min(1, pulse.bass / pulse.peak);
                                const m = Math.min(1, pulse.mid / pulse.peak);
                                const h = Math.min(1, pulse.high / pulse.peak);
                                pulse.zoom += (b * 0.05 * fx.audio - pulse.zoom) * 0.3;
                                pulse.sat += (1 + m * 0.6 * fx.audio - pulse.sat) * 0.2;
                                pulse.bright += (1 + h * 0.35 * fx.audio - pulse.bright) * 0.2;
                            } else if (pulse.zoom !== 0 || pulse.sat !== 1 || pulse.bright !== 1) {
                                pulse.zoom = 0;
                                pulse.sat = 1;
                                pulse.bright = 1;
                            }

                            if (fx.ripple > 0) {
                                const t = now / 1000;
                                const amp = 16 + fx.ripple * 28;
                                noiseScroll.setAttribute('dx', (Math.sin(t * 0.7) * amp).toFixed(2));
                                noiseScroll.setAttribute('dy', (Math.cos(t * 0.47) * amp * 0.6).toFixed(2));
                            }

                            paintMedia();
                        }

                        function paintMedia() {
                            const parts = [];
                            if (fx.blur > 0)
                                parts.push('blur(' + (fx.blur * 0.22).toFixed(2) + 'px)');
                            if (fx.hue !== 0)
                                parts.push('hue-rotate(' + fx.hue.toFixed(2) + 'deg)');
                            const saturate = fx.saturate * pulse.sat;
                            if (Math.abs(saturate - 1) > 0.002)
                                parts.push('saturate(' + saturate.toFixed(3) + ')');
                            const brightness = fx.brightness * pulse.bright;
                            if (Math.abs(brightness - 1) > 0.002)
                                parts.push('brightness(' + brightness.toFixed(3) + ')');
                            if (Math.abs(fx.contrast - 1) > 0.002)
                                parts.push('contrast(' + fx.contrast.toFixed(3) + ')');
                            media.style.filter = parts.length > 0 ? parts.join(' ') : 'none';

                            const shift = fx.parallax * 4;
                            // Parallax slides the layer, so it is oversized just enough to never show a gap.
                            const cover = fx.parallax > 0 ? 1 + fx.parallax * 0.1 : 1;
                            const scale = cover * (1 + pulse.zoom);
                            if (shift === 0 && Math.abs(scale - 1) < 0.001) {
                                media.style.transform = 'none';
                                return;
                            }
                            media.style.transform = 'translate3d(' + (-pointer.easeX * shift).toFixed(2) + '%, '
                                + (-pointer.easeY * shift).toFixed(2) + '%, 0) scale(' + scale.toFixed(4) + ')';
                        }

                        // Svg filters and the vignette only change with the sliders, not every frame.
                        function paintStage() {
                            const chain = [];
                            if (Math.abs(fx.gamma - 1) > 0.001 || Math.abs(fx.temp) > 0.001) {
                                const exponent = (1 / fx.gamma).toFixed(3);
                                gammaCurves[0].setAttribute('exponent', exponent);
                                gammaCurves[1].setAttribute('exponent', exponent);
                                gammaCurves[2].setAttribute('exponent', exponent);
                                // Warm lifts red and dims blue, cool does the reverse; 12% is the extreme.
                                gammaCurves[0].setAttribute('amplitude', (1 + fx.temp * 0.12).toFixed(3));
                                gammaCurves[1].setAttribute('amplitude', '1');
                                gammaCurves[2].setAttribute('amplitude', (1 - fx.temp * 0.12).toFixed(3));
                                chain.push('url(#livelyGamma)');
                            }
                            if (fx.chroma > 0) {
                                const split = (fx.chroma * 10).toFixed(2);
                                chromaR.setAttribute('dx', split);
                                chromaB.setAttribute('dx', '-' + split);
                                chain.push('url(#livelyChroma)');
                            }
                            if (fx.ripple > 0) {
                                warp.setAttribute('scale', (fx.ripple * 26).toFixed(1));
                                chain.push('url(#livelyRipple)');
                            }
                            stage.style.filter = chain.length > 0 ? chain.join(' ') : 'none';
                            shade.style.opacity = fx.vignette.toFixed(3);
                        }

                        function clampFraction(value, span) {
                            return 1 + (Math.min(100, Math.max(-100, Number(value) || 0)) / span);
                        }

                        function clampUnit(value) {
                            return Math.min(100, Math.max(0, Number(value) || 0)) / 100;
                        }

                        // Posts the value as an object: WebMessageAsJson must start with a
                        // brace or the player's IpcMessage converter rejects it.
                        function reportError(detail) {
                            try { chrome.webview.postMessage({ Type: 1, Category: 1, Message: detail }); } catch (e) { }
                        }
                        media.addEventListener('error', () => reportError('Media failed to load: ' + media.currentSrc));

                        window.addEventListener('pointermove', (event) => {
                            if (fx.parallax <= 0)
                                return;
                            const width = window.innerWidth || 1;
                            const height = window.innerHeight || 1;
                            pointer.x = Math.min(1, Math.max(-1, (event.clientX / width - 0.5) * 2));
                            pointer.y = Math.min(1, Math.max(-1, (event.clientY / height - 0.5) * 2));
                            lastMove = event.timeStamp || performance.now();
                            kick();
                        }, { passive: true });

                        window.addEventListener('pointerout', () => {
                            if (fx.parallax <= 0)
                                return;
                            pointer.x = 0;
                            pointer.y = 0;
                            kick();
                        }, { passive: true });

                        // Called by the core when the wallpaper is restored/changed.
                        window.livelyPropertyListener = (name, value) => {
                            switch (name) {
                                case 'hue': fx.hue = (Math.min(100, Math.max(-100, Number(value) || 0)) * 3.6); break;
                                case 'saturation': fx.saturate = clampFraction(value, 100); break;
                                case 'brightness': fx.brightness = clampFraction(value, 100); break;
                                case 'contrast': fx.contrast = clampFraction(value, 100); break;
                                case 'gamma': fx.gamma = clampFraction(value, 200); break;
                                case 'temperature': fx.temp = Math.min(100, Math.max(-100, Number(value) || 0)) / 100; break;
                                case 'blur': fx.blur = clampUnit(value); break;
                                case 'vignette': fx.vignette = clampUnit(value); break;
                                case 'chromatic': fx.chroma = clampUnit(value); break;
                                case 'ripple': fx.ripple = clampUnit(value); break;
                                case 'parallax': fx.parallax = clampUnit(value); break;
                                case 'audio': fx.audio = clampUnit(value); break;
                                case 'speed': if (media.tagName === 'VIDEO') media.playbackRate = Math.max(0.0625, Number(value) || 1); break;
                                case 'mute': media.muted = Boolean(value); break;
                                case 'scaler': window.livelyMediaScaler(value); break;
                                default: return;
                            }
                            paintStage();
                            paintMedia();
                            kick();
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

                        // System audio spectrum (128 fft bands) pushed by NAudioVisualizerService.
                        // The magnitudes are device dependant, so everything is scaled by a
                        // slowly decaying peak instead of a fixed gain.
                        window.livelyAudioListener = (bands) => {
                            if (fx.audio <= 0 || !bands || bands.length < 32)
                                return;
                            let bass = 0, mid = 0, high = 0;
                            for (let i = 1; i < 8; i++) bass += bands[i];
                            for (let i = 8; i < 40; i++) mid += bands[i];
                            for (let i = 40; i < bands.length; i++) high += bands[i];
                            pulse.bass = bass / 7;
                            pulse.mid = mid / 32;
                            pulse.high = high / (bands.length - 40);
                            pulse.peak = Math.max(pulse.bass, pulse.mid, pulse.high, pulse.peak * 0.995, 0.00001);
                            pulse.ready = true;
                            kick();
                        };

                        // Api stubs: media wallpapers report no hardware/track telemetry.
                        window.livelySystemInformation = () => { };
                        window.livelyCurrentTrack = () => { };

                        paintStage();
                        paintMedia();
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
