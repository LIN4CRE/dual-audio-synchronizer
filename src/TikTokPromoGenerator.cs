using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

namespace DualAudioPromo {
    class TikTokPromoGenerator {
        const int Width = 1080;
        const int Height = 1920;
        const int Fps = 30;
        const double DurationSec = 26.0;
        const int TotalFrames = (int)(Fps * DurationSec); // 780 frames
        const int SampleRate = 44100;

        struct CaptionChunk {
            public double Start;
            public double End;
            public string[] Words;
            public Color Accent;
        }

        static readonly CaptionChunk[] Captions = new CaptionChunk[] {
            // Scene 1 (0.10s - 4.05s)
            new CaptionChunk { Start = 0.00, End = 1.95, Words = new[] { "Why", "does", "Windows", "still", "only" }, Accent = Color.FromArgb(56, 189, 248) },
            new CaptionChunk { Start = 1.95, End = 4.05, Words = new[] { "play", "audio", "on", "ONE", "speaker?!" }, Accent = Color.FromArgb(74, 222, 128) },

            // Scene 2 (4.10s - 10.65s)
            new CaptionChunk { Start = 4.05, End = 6.95, Words = new[] { "Meet", "Dual Audio", "Synchronizer", "v2.0 ⚡" }, Accent = Color.FromArgb(56, 189, 248) },
            new CaptionChunk { Start = 6.95, End = 10.65, Words = new[] { "Download,", "run,", "& auto-lock", "every speaker!" }, Accent = Color.FromArgb(74, 222, 128) },

            // Scene 3 (10.70s - 16.00s)
            new CaptionChunk { Start = 10.65, End = 13.35, Words = new[] { "Built-in", "2.8 kHz", "Vocal Clarity", "Boost ★" }, Accent = Color.FromArgb(250, 204, 21) },
            new CaptionChunk { Start = 13.35, End = 16.00, Words = new[] { "Plus", "1-Click", "Left / Right", "Stereo Split ⚡" }, Accent = Color.FromArgb(56, 189, 248) },

            // Scene 4 (16.05s - 21.45s)
            new CaptionChunk { Start = 16.00, End = 18.75, Words = new[] { "Hit Fullscreen", "on the", "Trance", "Visualizer ✨" }, Accent = Color.FromArgb(192, 132, 252) },
            new CaptionChunk { Start = 18.75, End = 21.45, Words = new[] { "And watch", "it react", "to every", "single beat! ✨" }, Accent = Color.FromArgb(74, 222, 128) },

            // Scene 5 (21.50s - 26.00s)
            new CaptionChunk { Start = 21.45, End = 23.60, Words = new[] { "100% Free", "& Open Source", "on GitHub ★" }, Accent = Color.FromArgb(56, 189, 248) },
            new CaptionChunk { Start = 23.60, End = 26.00, Words = new[] { "Link in Bio!", "paypal.me/dlinacre16", "☕" }, Accent = Color.FromArgb(250, 204, 21) }
        };

        static void Main(string[] args) {
            string projectDir = Directory.GetCurrentDirectory();
            string framesDir = Path.Combine(projectDir, "build", "tiktok_frames");
            if (Directory.Exists(framesDir)) Directory.Delete(framesDir, true);
            Directory.CreateDirectory(framesDir);

            string wavPath = Path.Combine(projectDir, "build", "tiktok_audio.wav");
            Console.WriteLine("1. Mixing Neural Voiceover Stems + 126 BPM Stereo Trance Soundtrack...");
            float[] beatEnvelope;
            GenerateSoundtrackAndVoiceover(projectDir, wavPath, out beatEnvelope);

            Console.WriteLine("2. Rendering " + TotalFrames + " Vertical Full HD (1080x1920) Frames...");
            RenderAllFrames(projectDir, framesDir, beatEnvelope);

            Console.WriteLine("3. Completed all frames and master audio.");
        }

        static void GenerateSoundtrackAndVoiceover(string projectDir, string wavPath, out float[] frameBeatEnv) {
            int totalSamples = (int)(SampleRate * DurationSec);
            float[] left = new float[totalSamples];
            float[] right = new float[totalSamples];
            frameBeatEnv = new float[TotalFrames];

            double[] startTimes = { 0.10, 4.10, 10.70, 16.05, 21.50 };
            float[] voTrack = new float[totalSamples];
            float[] voDuck = new float[totalSamples];
            for (int i = 0; i < totalSamples; i++) voDuck[i] = 1.0f;

            for (int s = 0; s < 5; s++) {
                string stemPath = Path.Combine(projectDir, "build", "piper", "vo_" + (s + 1) + ".wav");
                float[] samples = LoadWavMono(stemPath);
                int startIdx = (int)(startTimes[s] * SampleRate);
                for (int i = 0; i < samples.Length && (startIdx + i) < totalSamples; i++) {
                    voTrack[startIdx + i] += samples[i];
                }
            }

            // Smooth sidechain ducking envelope around narrator speech
            float env = 0f;
            for (int i = 0; i < totalSamples; i++) {
                float absVo = Math.Abs(voTrack[i]);
                if (absVo > env) env = env * 0.990f + absVo * 0.010f;
                else env = env * 0.9994f + absVo * 0.0006f;
                voDuck[i] = 1.0f - Math.Min(0.55f, env * 2.1f);
            }

            double bpm = 126.0;
            double beatDur = 60.0 / bpm;

            double[] bassFreqs = { 55.0, 43.65, 65.41, 49.00 }; // Am - F - C - G
            double[,] arpFreqs = {
                { 220.0, 261.63, 329.63, 440.0, 523.25, 659.25, 523.25, 440.0 },
                { 174.61, 220.0, 261.63, 349.23, 440.0, 523.25, 440.0, 349.23 },
                { 261.63, 329.63, 392.00, 523.25, 659.25, 783.99, 659.25, 523.25 },
                { 196.00, 246.94, 293.66, 392.00, 493.88, 587.33, 493.88, 392.00 }
            };

            double kickPhase = 0, bassPhase = 0, subPhase = 0, arpPhaseL = 0, arpPhaseR = 0;
            Random rng = new Random(42);

            for (int i = 0; i < totalSamples; i++) {
                double t = (double)i / SampleRate;
                double beatPos = t / beatDur;
                int beatIndex = (int)beatPos;
                double beatFrac = beatPos - beatIndex;
                int chordIdx = (beatIndex / 4) % 4;

                bool inDrop = (t >= 16.0 && t <= 21.45);

                // 1. Punchy 4-on-the-floor Trance Kick
                double kickEnv = Math.Exp(-beatFrac * 9.0);
                double kickFreq = 45.0 + 135.0 * Math.Exp(-beatFrac * 28.0);
                kickPhase += 2.0 * Math.PI * kickFreq / SampleRate;
                double kick = Math.Sin(kickPhase) * kickEnv * (inDrop ? 0.80 : 0.65);

                int fIdx = (int)(t * Fps);
                if (fIdx >= 0 && fIdx < TotalFrames) {
                    float bVal = (float)Math.Exp(-beatFrac * 4.2) * (inDrop ? 1.35f : 1.0f);
                    if (bVal > frameBeatEnv[fIdx]) frameBeatEnv[fIdx] = bVal;
                }

                // 2. Sidechained Rolling Trance Sub-Bass
                double sidechain = 1.0 - Math.Exp(-beatFrac * 6.5);
                double bFreq = bassFreqs[chordIdx];
                bassPhase += 2.0 * Math.PI * bFreq / SampleRate;
                subPhase += 2.0 * Math.PI * (bFreq * 2.0) / SampleRate;
                double saw = 2.0 * ((bassPhase / (2.0 * Math.PI)) - Math.Floor(0.5 + bassPhase / (2.0 * Math.PI)));
                double bass = (Math.Sin(bassPhase) * 0.65 + Math.Tanh(saw * 1.9) * 0.35 + Math.Sin(subPhase) * 0.22) * sidechain * (inDrop ? 0.46 : 0.36);

                // 3. Stereo Detuned Supersaw Arpeggio
                double sixteenthPos = beatPos * 4.0;
                int sixteenthIdx = (int)sixteenthPos;
                double sixteenthFrac = sixteenthPos - sixteenthIdx;
                double pluckEnv = Math.Exp(-sixteenthFrac * (inDrop ? 2.8 : 5.0));
                double aFreq = arpFreqs[chordIdx, sixteenthIdx % 8];
                arpPhaseL += 2.0 * Math.PI * (aFreq * 0.996) / SampleRate;
                arpPhaseR += 2.0 * Math.PI * (aFreq * 1.004) / SampleRate;
                double arpL = (Math.Sin(arpPhaseL) + 0.55 * Math.Sin(arpPhaseL * 2.0)) * pluckEnv * sidechain * (inDrop ? 0.28 : 0.20);
                double arpR = (Math.Sin(arpPhaseR) + 0.55 * Math.Sin(arpPhaseR * 2.0)) * pluckEnv * sidechain * (inDrop ? 0.28 : 0.20);

                // 4. Crisp Off-beat Hi-Hat + Riser Sweep before 13.0s drop
                double hatFrac = (beatFrac + 0.5) - Math.Floor(beatFrac + 0.5);
                double hatEnv = Math.Exp(-hatFrac * 26.0);
                double noise = (rng.NextDouble() * 2.0 - 1.0);
                double hat = noise * hatEnv * 0.09;

                double riser = 0;
                if (t >= 14.4 && t < 16.0) {
                    double rProg = (t - 14.4) / 1.6;
                    riser = noise * rProg * rProg * 0.22;
                }

                // Fade out smoothly on final 0.4s
                double masterFade = t > (DurationSec - 0.45) ? Math.Max(0.0, (DurationSec - t) / 0.45) : 1.0;

                float duck = voDuck[i];
                float musicL = (float)((kick + bass + arpL + hat * 0.8 + riser) * duck * masterFade);
                float musicR = (float)((kick + bass + arpR + hat * 1.2 + riser) * duck * masterFade);

                float vo = voTrack[i] * 1.22f;
                left[i] = SoftClip(musicL + vo);
                right[i] = SoftClip(musicR + vo);
            }

            WriteStereoWav(wavPath, left, right, SampleRate);
        }

        static float[] LoadWavMono(string path) {
            if (!File.Exists(path)) return new float[0];
            byte[] bytes = File.ReadAllBytes(path);
            int offset = 12;
            while (offset + 8 < bytes.Length) {
                string chunkId = System.Text.Encoding.ASCII.GetString(bytes, offset, 4);
                int chunkSize = BitConverter.ToInt32(bytes, offset + 4);
                if (chunkId == "data") {
                    int dataStart = offset + 8;
                    int count = Math.Min(chunkSize, bytes.Length - dataStart) / 2;
                    float[] samples = new float[count];
                    float maxPeak = 0.01f;
                    for (int i = 0; i < count; i++) {
                        short s = BitConverter.ToInt16(bytes, dataStart + i * 2);
                        float v = s / 32768f;
                        samples[i] = v;
                        if (Math.Abs(v) > maxPeak) maxPeak = Math.Abs(v);
                    }
                    float norm = 0.88f / maxPeak;
                    for (int i = 0; i < count; i++) samples[i] *= norm;
                    return samples;
                }
                offset += 8 + chunkSize;
            }
            return new float[0];
        }

        static float SoftClip(float x) {
            if (x > 1.3f) return 0.96f;
            if (x < -1.3f) return -0.96f;
            return (float)Math.Tanh(x);
        }

        static void WriteStereoWav(string path, float[] left, float[] right, int sampleRate) {
            int numSamples = left.Length;
            int dataBytes = numSamples * 4;
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs)) {
                bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                bw.Write(36 + dataBytes);
                bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
                bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
                bw.Write(16);
                bw.Write((short)1);
                bw.Write((short)2);
                bw.Write(sampleRate);
                bw.Write(sampleRate * 4);
                bw.Write((short)4);
                bw.Write((short)16);
                bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                bw.Write(dataBytes);
                for (int i = 0; i < numSamples; i++) {
                    short sL = (short)Math.Max(-32767, Math.Min(32767, left[i] * 32767f));
                    short sR = (short)Math.Max(-32767, Math.Min(32767, right[i] * 32767f));
                    bw.Write(sL);
                    bw.Write(sR);
                }
            }
        }

        static void RenderAllFrames(string projectDir, string framesDir, float[] beatEnvelope) {
            Image imgScreenshot = Image.FromFile(Path.Combine(projectDir, "assets", "screenshot.png"));
            Image imgVisualizer = Image.FromFile(Path.Combine(projectDir, "assets", "visualizer.png"));
            Image imgLogo = Image.FromFile(Path.Combine(projectDir, "assets", "logo.png"));

            using (var canvas = new Bitmap(Width, Height, PixelFormat.Format32bppArgb))
            using (var trailBmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb)) {
                for (int f = 0; f < TotalFrames; f++) {
                    double t = (double)f / Fps;
                    float beat = beatEnvelope[f];

                    using (var g = Graphics.FromImage(canvas)) {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                        RenderHypnoticBackground(trailBmp, t, beat);

                        if (t < 4.05) {
                            RenderScene1_Hook(g, trailBmp, t, beat);
                        } else if (t < 10.65) {
                            RenderScene2_StudioUI(g, trailBmp, imgScreenshot, t - 4.05, beat);
                        } else if (t < 16.00) {
                            RenderScene3_ClarityAndStereo(g, trailBmp, imgScreenshot, t - 10.65, beat);
                        } else if (t < 21.45) {
                            RenderScene4_TranceDrop(g, trailBmp, imgVisualizer, t - 16.00, beat);
                        } else {
                            RenderScene5_OutroCTA(g, trailBmp, imgLogo, imgScreenshot, t - 21.45, beat);
                        }

                        RenderWordSyncedCaptions(g, t, beat);
                    }

                    string frameFile = Path.Combine(framesDir, string.Format("frame_{0:D4}.png", f));
                    canvas.Save(frameFile, ImageFormat.Png);
                }
            }

            imgScreenshot.Dispose();
            imgVisualizer.Dispose();
            imgLogo.Dispose();
        }

        static void RenderHypnoticBackground(Bitmap trailBmp, double t, float beat) {
            using (var g = Graphics.FromImage(trailBmp)) {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var fade = new SolidBrush(Color.FromArgb(40, 6, 8, 18))) {
                    g.FillRectangle(fade, 0, 0, Width, Height);
                }

                float cx = Width * 0.5f;
                float cy = Height * 0.48f;
                float maxR = 980f;

                int rings = 24;
                double tunnelShift = (t * 2.5) % 1.0;
                for (int r = rings; r >= 1; r--) {
                    float norm = (float)((r - tunnelShift) / rings);
                    if (norm <= 0.02f) continue;
                    float rad = norm * norm * maxR * (1.0f + beat * 0.16f);
                    float hue = (float)((t * 68.0 + r * 16.0) % 360.0);
                    Color c = HsvToColor(hue, 0.85f, Math.Min(1.0f, 0.35f + (1.0f - norm) * 0.65f + beat * 0.2f), (int)(180 * (1.0f - norm * 0.45f)));

                    int segs = 72;
                    PointF[] pts = new PointF[segs + 1];
                    double rot = t * (r % 2 == 0 ? 0.85 : -0.85) + r * 0.14;
                    for (int s = 0; s <= segs; s++) {
                        double ang = (s / (double)segs) * Math.PI * 2.0;
                        double wave = Math.Sin(ang * 8.0 + rot) * (18.0 + beat * 44.0) * norm
                                    + Math.Cos(ang * 12.0 - t * 3.2) * (12.0 + beat * 28.0) * norm;
                        float rr = Math.Max(8f, rad + (float)wave);
                        pts[s] = new PointF(cx + (float)Math.Cos(ang + rot * 0.3) * rr, cy + (float)Math.Sin(ang + rot * 0.3) * rr);
                    }
                    using (var pen = new Pen(c, 2.3f + beat * 2.1f)) {
                        g.DrawLines(pen, pts);
                    }
                }

                double golden = 2.39996322972865332;
                for (int i = 0; i < 68; i++) {
                    double a = i * golden + t * 1.45;
                    float r = (float)(Math.Sqrt(i / 68.0) * (330.0 + beat * 115.0));
                    float px = cx + (float)Math.Cos(a) * r;
                    float py = cy + (float)Math.Sin(a) * r;
                    float sz = 4.5f + beat * 7.5f;
                    Color sc = HsvToColor((float)((t * 95 + i * 7) % 360), 0.75f, 1.0f, 195);
                    using (var b = new SolidBrush(sc)) {
                        g.FillEllipse(b, px - sz * 0.5f, py - sz * 0.5f, sz, sz);
                    }
                }
            }
        }

        static void RenderScene1_Hook(Graphics g, Bitmap bg, double t, float beat) {
            g.DrawImageUnscaled(bg, 0, 0);
            using (var darkOverlay = new SolidBrush(Color.FromArgb(165, 8, 11, 24))) {
                g.FillRectangle(darkOverlay, 0, 0, Width, Height);
            }

            DrawPill(g, new RectangleF(220, 145, 640, 64), Color.FromArgb(225, 30, 41, 82), Color.FromArgb(56, 189, 248), "⚡ WINDOWS 10 & 11 AUDIO HACK", 22f, Color.FromArgb(186, 230, 253));

            using (var fTitle = new Font("Segoe UI", 47f, FontStyle.Bold))
            using (var fSub = new Font("Segoe UI Semibold", 27f, FontStyle.Bold)) {
                var sf = new StringFormat { Alignment = StringAlignment.Center };
                g.DrawString("PLAY AUDIO ON\n2+ SPEAKERS AT ONCE", fTitle, Brushes.White, new RectangleF(50, 245, 980, 245), sf);
                using (var cyanBrush = new SolidBrush(Color.FromArgb(56, 189, 248))) {
                    g.DrawString("Zero Bluetooth Lag • Zero Hollow Echo", fSub, cyanBrush, new RectangleF(50, 495, 980, 65), sf);
                }
            }

            bool locked = t >= 1.80;
            RectangleF cardRect = new RectangleF(75, 605, 930, 650);
            using (var cardPath = RoundedRect(cardRect, 36))
            using (var cardBg = new SolidBrush(Color.FromArgb(230, 17, 22, 40)))
            using (var cardBorder = new Pen(locked ? Color.FromArgb(74, 222, 128) : Color.FromArgb(248, 113, 113), 4.5f)) {
                g.FillPath(cardBg, cardPath);
                g.DrawPath(cardBorder, cardPath);
            }

            if (!locked) {
                DrawPill(g, new RectangleF(180, 648, 720, 68), Color.FromArgb(75, 239, 68, 68), Color.FromArgb(248, 113, 113), "❌ WITHOUT SYNC: 140 ms DRIFT & ECHO", 21f, Color.FromArgb(254, 202, 202));
            } else {
                DrawPill(g, new RectangleF(155, 648, 770, 68), Color.FromArgb(80, 34, 197, 94), Color.FromArgb(74, 222, 128), "✔ PHASE-LOCKED: ±1 ms EXACT SYNC", 22f, Color.FromArgb(187, 247, 208));
            }

            DrawWaveformRow(g, "SPEAKER 1: Headphones (SK100)", 775, t, 0.0, Color.FromArgb(56, 189, 248), beat);
            double phaseOffset = locked ? 0.0 : 1.95;
            Color spk2Col = locked ? Color.FromArgb(74, 222, 128) : Color.FromArgb(248, 113, 113);
            DrawWaveformRow(g, "SPEAKER 2: EDIFIER R1280DBs", 1005, t, phaseOffset, spk2Col, beat);
        }

        static void DrawWaveformRow(Graphics g, string label, float y, double t, double phaseShift, Color col, float beat) {
            using (var fLbl = new Font("Segoe UI Semibold", 21f, FontStyle.Bold)) {
                g.DrawString(label, fLbl, Brushes.White, 130, y);
            }

            PointF[] pts = new PointF[90];
            for (int i = 0; i < pts.Length; i++) {
                float x = 130 + (i / (float)(pts.Length - 1)) * 820f;
                double ang = (i * 0.22) - t * 14.0 + phaseShift;
                float env = (float)Math.Sin((i / (double)pts.Length) * Math.PI);
                float amp = (38f + beat * 34f) * env;
                float py = y + 96f + (float)Math.Sin(ang) * amp;
                pts[i] = new PointF(x, py);
            }
            using (var glowPen = new Pen(Color.FromArgb(85, col), 11f))
            using (var mainPen = new Pen(col, 4.5f)) {
                g.DrawLines(glowPen, pts);
                g.DrawLines(mainPen, pts);
            }
        }

        static void RenderScene2_StudioUI(Graphics g, Bitmap bg, Image screenshot, double localT, float beat) {
            g.DrawImageUnscaled(bg, 0, 0);
            using (var darkOverlay = new SolidBrush(Color.FromArgb(170, 8, 11, 24))) {
                g.FillRectangle(darkOverlay, 0, 0, Width, Height);
            }

            DrawPill(g, new RectangleF(175, 140, 730, 64), Color.FromArgb(225, 30, 58, 138), Color.FromArgb(56, 189, 248), "⚡ ZERO CONFIG — DOWNLOAD & RUN", 21f, Color.White);

            using (var fTitle = new Font("Segoe UI", 42f, FontStyle.Bold)) {
                var sf = new StringFormat { Alignment = StringAlignment.Center };
                g.DrawString("Dual Audio Synchronizer v2.0", fTitle, Brushes.White, new RectangleF(40, 225, 1000, 75), sf);
            }

            float scale = 1.0f + 0.025f * (float)Math.Sin(localT * 1.4) + beat * 0.012f;
            float imgW = 910f * scale;
            float imgH = imgW * ((float)screenshot.Height / screenshot.Width);
            float imgX = (Width - imgW) * 0.5f;
            float imgY = 320f;

            RectangleF frameRect = new RectangleF(imgX - 10, imgY - 10, imgW + 20, imgH + 20);
            using (var path = RoundedRect(frameRect, 24))
            using (var borderPen = new Pen(Color.FromArgb(210, 56, 189, 248), 4f))
            using (var shadowBrush = new SolidBrush(Color.FromArgb(195, 10, 14, 28))) {
                g.FillPath(shadowBrush, path);
                g.DrawImage(screenshot, new RectangleF(imgX, imgY, imgW, imgH));
                g.DrawPath(borderPen, path);
            }

            string[] badges = {
                "✔ Auto-Detects All Bluetooth & Wired Speakers",
                "★ ±1 ms 4-Pt Cubic Hermite Clock-Drift Lock",
                "⚡ Auto-Reconnects Bluetooth Endpoints Live"
            };
            Color[] badgeCols = {
                Color.FromArgb(74, 222, 128),
                Color.FromArgb(56, 189, 248),
                Color.FromArgb(192, 132, 252)
            };

            for (int i = 0; i < badges.Length; i++) {
                double appearAt = i * 0.75;
                if (localT < appearAt) continue;
                float prog = (float)Math.Min(1.0, (localT - appearAt) * 4.5);
                float by = 1125f + i * 102f + (1.0f - prog) * 30f;
                DrawPill(g, new RectangleF(75, by, 930, 82), Color.FromArgb((int)(230 * prog), 20, 26, 48), badgeCols[i], badges[i], 23f, Color.White);
            }
        }

        static void RenderScene3_ClarityAndStereo(Graphics g, Bitmap bg, Image screenshot, double localT, float beat) {
            g.DrawImageUnscaled(bg, 0, 0);
            using (var darkOverlay = new SolidBrush(Color.FromArgb(175, 8, 11, 24))) {
                g.FillRectangle(darkOverlay, 0, 0, Width, Height);
            }

            DrawPill(g, new RectangleF(140, 140, 800, 64), Color.FromArgb(225, 113, 63, 18), Color.FromArgb(250, 204, 21), "★ HEARING CLARITY DSP & STEREO SPLIT", 21f, Color.FromArgb(254, 240, 138));

            using (var fTitle = new Font("Segoe UI", 40f, FontStyle.Bold)) {
                var sf = new StringFormat { Alignment = StringAlignment.Center };
                g.DrawString("Studio-Grade Audio Processing", fTitle, Brushes.White, new RectangleF(40, 225, 1000, 75), sf);
            }

            // Card 1: 2.8 kHz Vocal Clarity Biquad EQ Curve
            RectangleF eqCard = new RectangleF(75, 330, 930, 440);
            using (var p = RoundedRect(eqCard, 30))
            using (var b = new SolidBrush(Color.FromArgb(230, 18, 23, 42)))
            using (var pen = new Pen(Color.FromArgb(250, 204, 21), 3.5f)) {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }

            using (var fH = new Font("Segoe UI", 27f, FontStyle.Bold))
            using (var fS = new Font("Segoe UI Semibold", 20f)) {
                g.DrawString("★ 2.8 kHz Vocal Clarity EQ + Center Mono Blend", fH, Brushes.White, 115, 365);
                using (var goldB = new SolidBrush(Color.FromArgb(254, 240, 138))) {
                    g.DrawString("Makes movie dialogue & speech consonants crystal-clear", fS, goldB, 115, 415);
                }
            }

            // Draw animated bell EQ curve peaking at 2.8 kHz
            PointF[] eqPts = new PointF[80];
            for (int i = 0; i < 80; i++) {
                float u = i / 79f;
                float x = 125f + u * 830f;
                double bell = Math.Exp(-Math.Pow((u - 0.62) / 0.14, 2.0));
                float y = 685f - (float)(bell * (155.0 + beat * 35.0));
                eqPts[i] = new PointF(x, y);
            }
            using (var glow = new Pen(Color.FromArgb(90, 250, 204, 21), 14f))
            using (var line = new Pen(Color.FromArgb(250, 204, 21), 5f)) {
                g.DrawLines(glow, eqPts);
                g.DrawLines(line, eqPts);
            }
            DrawPill(g, new RectangleF(485, 470, 370, 54), Color.FromArgb(225, 120, 53, 15), Color.FromArgb(250, 204, 21), "+3.5 dB / +5.5 dB @ 2.8 kHz", 19f, Color.White);

            // Card 2: 1-Click Left / Right Stereo Split Matrix
            RectangleF stCard = new RectangleF(75, 810, 930, 560);
            using (var p = RoundedRect(stCard, 30))
            using (var b = new SolidBrush(Color.FromArgb(230, 18, 23, 42)))
            using (var pen = new Pen(Color.FromArgb(56, 189, 248), 3.5f)) {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }

            using (var fH = new Font("Segoe UI", 27f, FontStyle.Bold))
            using (var fS = new Font("Segoe UI Semibold", 20f)) {
                g.DrawString("⚡ Per-Speaker Channel Matrix (1-Click Routing)", fH, Brushes.White, 115, 845);
                using (var cyanB = new SolidBrush(Color.FromArgb(186, 230, 253))) {
                    g.DrawString("Turn any 2 Bluetooth speakers into a dedicated Left / Right pair", fS, cyanB, 115, 895);
                }
            }

            DrawPill(g, new RectangleF(120, 975, 405, 95), Color.FromArgb(220, 22, 48, 82), Color.FromArgb(56, 189, 248), "◀ Speaker A: Ch: Left Only", 21f, Color.White);
            DrawPill(g, new RectangleF(555, 975, 405, 95), Color.FromArgb(220, 22, 68, 52), Color.FromArgb(74, 222, 128), "▶ Speaker B: Ch: Right Only", 21f, Color.White);
            DrawPill(g, new RectangleF(120, 1100, 840, 80), Color.FromArgb(210, 42, 28, 78), Color.FromArgb(192, 132, 252), "Also supports: Ch: Stereo (L+R)  •  Ch: Mono Sum", 21f, Color.FromArgb(243, 232, 255));
        }

        static void RenderScene4_TranceDrop(Graphics g, Bitmap bg, Image vizShot, double localT, float beat) {
            g.DrawImageUnscaled(bg, 0, 0);

            float cx = Width * 0.5f;
            float cy = Height * 0.45f;
            int folds = 12;
            for (int f = 0; f < folds; f++) {
                double baseAng = (f / (double)folds) * Math.PI * 2.0 + localT * 0.7;
                PointF[] ribbon = new PointF[36];
                for (int k = 0; k < 36; k++) {
                    float dist = 55f + k * 21f * (1.0f + beat * 0.24f);
                    double wobble = Math.Sin(k * 0.35 - localT * 6.2) * (0.19 + beat * 0.16);
                    double a = baseAng + (f % 2 == 0 ? wobble : -wobble);
                    ribbon[k] = new PointF(cx + (float)Math.Cos(a) * dist, cy + (float)Math.Sin(a) * dist);
                }
                Color rc = HsvToColor((float)((localT * 125 + f * 30) % 360), 0.85f, 1.0f, 220);
                using (var pen = new Pen(rc, 4.0f + beat * 3.2f)) {
                    g.DrawCurve(pen, ribbon, 0.5f);
                }
            }

            DrawPill(g, new RectangleF(120, 140, 840, 68), Color.FromArgb(225, 59, 7, 100), Color.FromArgb(192, 132, 252), "✨ BUILT-IN 60 FPS TRANCE VISUALIZER", 23f, Color.FromArgb(243, 232, 255));

            using (var fBig = new Font("Segoe UI", 42f, FontStyle.Bold)) {
                var sf = new StringFormat { Alignment = StringAlignment.Center };
                g.DrawString("5 Beat-Reactive Psychedelic Modes", fBig, Brushes.White, new RectangleF(40, 230, 1000, 75), sf);
            }

            float pipW = 890f;
            float pipH = pipW * ((float)vizShot.Height / vizShot.Width);
            RectangleF pipRect = new RectangleF((Width - pipW) * 0.5f, 980f, pipW, pipH);
            using (var path = RoundedRect(new RectangleF(pipRect.X - 8, pipRect.Y - 8, pipRect.Width + 16, pipRect.Height + 16), 22))
            using (var pen = new Pen(Color.FromArgb(230, 192, 132, 252), 4f)) {
                g.DrawImage(vizShot, pipRect);
                g.DrawPath(pen, path);
            }
        }

        static void RenderScene5_OutroCTA(Graphics g, Bitmap bg, Image logo, Image screenshot, double localT, float beat) {
            g.DrawImageUnscaled(bg, 0, 0);
            using (var darkOverlay = new SolidBrush(Color.FromArgb(185, 8, 11, 24))) {
                g.FillRectangle(darkOverlay, 0, 0, Width, Height);
            }

            float logoSz = 200f + beat * 18f;
            g.DrawImage(logo, new RectangleF((Width - logoSz) * 0.5f, 150f, logoSz, logoSz));

            using (var fTitle = new Font("Segoe UI", 46f, FontStyle.Bold))
            using (var fSub = new Font("Segoe UI Semibold", 25f, FontStyle.Bold)) {
                var sf = new StringFormat { Alignment = StringAlignment.Center };
                g.DrawString("Dual Audio Synchronizer", fTitle, Brushes.White, new RectangleF(40, 375, 1000, 75), sf);
                using (var bCyan = new SolidBrush(Color.FromArgb(56, 189, 248))) {
                    g.DrawString("100% Free & Open-Source for Windows 10 & 11", fSub, bCyan, new RectangleF(40, 455, 1000, 55), sf);
                }
            }

            float sw = 735f;
            float sh = sw * ((float)screenshot.Height / screenshot.Width);
            g.DrawImage(screenshot, new RectangleF((Width - sw) * 0.5f, 525f, sw, sh));

            DrawPill(g, new RectangleF(70, 1145, 940, 92), Color.FromArgb(235, 30, 58, 138), Color.FromArgb(56, 189, 248), "★ lin4cre.github.io/dual-audio-synchronizer", 23f, Color.White);
            DrawPill(g, new RectangleF(70, 1260, 940, 92), Color.FromArgb(235, 146, 64, 14), Color.FromArgb(250, 204, 21), "☕ Support on PayPal: paypal.me/dlinacre16", 22f, Color.FromArgb(254, 249, 195));
        }

        static void RenderWordSyncedCaptions(Graphics g, double t, float beat) {
            // Top progress bar
            float prog = (float)(t / DurationSec);
            using (var bgBar = new SolidBrush(Color.FromArgb(90, 255, 255, 255)))
            using (var fgBar = new SolidBrush(Color.FromArgb(56, 189, 248))) {
                g.FillRectangle(bgBar, 40, 50, Width - 80, 10);
                g.FillRectangle(fgBar, 40, 50, (Width - 80) * prog, 10);
            }

            // Find active caption chunk
            CaptionChunk active = Captions[Captions.Length - 1];
            foreach (var c in Captions) {
                if (t >= c.Start && t <= c.End) {
                    active = c;
                    break;
                }
            }

            double chunkFrac = Math.Max(0.0, Math.Min(0.999, (t - active.Start) / Math.Max(0.1, active.End - active.Start)));
            int activeWordIdx = (int)(chunkFrac * active.Words.Length);

            RectangleF capBox = new RectangleF(55, 1615, 970, 155);
            using (var path = RoundedRect(capBox, 32))
            using (var bg = new SolidBrush(Color.FromArgb(238, 10, 13, 26)))
            using (var border = new Pen(active.Accent, 3.5f)) {
                g.FillPath(bg, path);
                g.DrawPath(border, path);
            }

            using (var font = new Font("Segoe UI", 27f, FontStyle.Bold)) {
                // Measure total width of words to center them on one row
                float spacing = 18f;
                float totalW = 0f;
                float[] wordWidths = new float[active.Words.Length];
                for (int i = 0; i < active.Words.Length; i++) {
                    SizeF sz = g.MeasureString(active.Words[i], font);
                    wordWidths[i] = sz.Width;
                    totalW += sz.Width + (i > 0 ? spacing : 0f);
                }

                float curX = (Width - totalW) * 0.5f;
                float centerY = capBox.Y + capBox.Height * 0.5f;

                for (int i = 0; i < active.Words.Length; i++) {
                    bool isSpokenNow = (i == activeWordIdx);
                    RectangleF wRect = new RectangleF(curX - 6, centerY - 34, wordWidths[i] + 12, 68);
                    if (isSpokenNow) {
                        using (var hlPath = RoundedRect(wRect, 16))
                        using (var hlBrush = new SolidBrush(active.Accent)) {
                            g.FillPath(hlBrush, hlPath);
                        }
                    }

                    Color txtCol = isSpokenNow ? Color.FromArgb(10, 14, 26) : (i < activeWordIdx ? Color.White : Color.FromArgb(185, 200, 225));
                    using (var txtBrush = new SolidBrush(txtCol)) {
                        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                        g.DrawString(active.Words[i], font, txtBrush, wRect, sf);
                    }
                    curX += wordWidths[i] + spacing;
                }
            }
        }

        static void DrawPill(Graphics g, RectangleF rect, Color fill, Color border, string text, float fontSize, Color textCol) {
            using (var path = RoundedRect(rect, (int)Math.Min(28, rect.Height * 0.45f)))
            using (var bFill = new SolidBrush(fill))
            using (var pBorder = new Pen(border, 3f))
            using (var font = new Font("Segoe UI Semibold", fontSize, FontStyle.Bold))
            using (var bText = new SolidBrush(textCol)) {
                g.FillPath(bFill, path);
                g.DrawPath(pBorder, path);
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(text, font, bText, rect, sf);
            }
        }

        static GraphicsPath RoundedRect(RectangleF r, int radius) {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        static Color HsvToColor(float h, float s, float v, int alpha) {
            h = ((h % 360f) + 360f) % 360f;
            float c = v * s;
            float x = c * (1f - Math.Abs(((h / 60f) % 2f) - 1f));
            float m = v - c;
            float r1 = 0, g1 = 0, b1 = 0;
            if (h < 60) { r1 = c; g1 = x; }
            else if (h < 120) { r1 = x; g1 = c; }
            else if (h < 180) { g1 = c; b1 = x; }
            else if (h < 240) { g1 = x; b1 = c; }
            else if (h < 300) { r1 = x; b1 = c; }
            else { r1 = c; b1 = x; }
            return Color.FromArgb(
                Math.Max(0, Math.Min(255, alpha)),
                Math.Max(0, Math.Min(255, (int)((r1 + m) * 255))),
                Math.Max(0, Math.Min(255, (int)((g1 + m) * 255))),
                Math.Max(0, Math.Min(255, (int)((b1 + m) * 255)))
            );
        }
    }
}
