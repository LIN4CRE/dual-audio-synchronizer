using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

public static class GenerateAssets {
    static GraphicsPath RoundRect(float x, float y, float w, float h, float r) {
        var p = new GraphicsPath();
        if (r <= 0.5f) {
            p.AddRectangle(new RectangleF(x, y, w, h));
            return p;
        }
        float d = Math.Min(Math.Min(w, h), r * 2f);
        p.AddArc(x, y, d, d, 180, 90);
        p.AddArc(x + w - d, y, d, d, 270, 90);
        p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        p.AddArc(x, y + h - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static Bitmap RenderIconBitmap(int size) {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            float S = size;
            bool tiny = size <= 20;
            float pad = tiny ? 0.5f : S * 0.045f;
            float tileW = S - pad * 2f;
            float corner = S * 0.23f;

            using (var tilePath = RoundRect(pad, pad, tileW, tileW, corner)) {
                // Deep studio indigo -> electric royal blue -> violet gradient
                using (var bgBrush = new LinearGradientBrush(
                    new RectangleF(0, 0, S, S),
                    Color.FromArgb(255, 19, 25, 54),
                    Color.FromArgb(255, 88, 51, 196),
                    45f)) {
                    var cb = new ColorBlend(3);
                    cb.Colors = new Color[] {
                        Color.FromArgb(255, 18, 28, 68),
                        Color.FromArgb(255, 37, 99, 235),
                        Color.FromArgb(255, 124, 58, 237)
                    };
                    cb.Positions = new float[] { 0f, 0.52f, 1f };
                    bgBrush.InterpolationColors = cb;
                    g.FillPath(bgBrush, tilePath);
                }

                // Soft cyan-mint ambient glow in upper center
                if (!tiny) {
                    using (var glowPath = new GraphicsPath()) {
                        float gx = S * 0.15f, gy = -S * 0.10f, gw = S * 0.70f, gh = S * 0.65f;
                        glowPath.AddEllipse(gx, gy, gw, gh);
                        using (var pgb = new PathGradientBrush(glowPath)) {
                            pgb.CenterColor = Color.FromArgb(75, 56, 189, 248);
                            pgb.SurroundColors = new Color[] { Color.FromArgb(0, 56, 189, 248) };
                            g.SetClip(tilePath);
                            g.FillPath(pgb, glowPath);
                            g.ResetClip();
                        }
                    }

                    // Crisp inner glass border
                    using (var borderPen = new Pen(Color.FromArgb(75, 255, 255, 255), Math.Max(1f, S * 0.018f))) {
                        g.DrawPath(borderPen, tilePath);
                    }
                }
            }

            // Acoustic Sync Waves arching across the top between both speakers
            var cyanGlow = Color.FromArgb(255, 56, 189, 248);
            var mintBright = Color.FromArgb(255, 74, 222, 128);
            float acx = S * 0.50f;
            float acy = S * (tiny ? 0.42f : 0.43f);

            if (tiny) {
                using (var p = new Pen(mintBright, Math.Max(1.6f, S * 0.095f))) {
                    p.StartCap = LineCap.Round;
                    p.EndCap = LineCap.Round;
                    float r = S * 0.23f;
                    g.DrawArc(p, acx - r, acy - r, r * 2, r * 2, 205, 130);
                }
            } else {
                float[] radii = new float[] { S * 0.135f, S * 0.235f };
                for (int i = 0; i < radii.Length; i++) {
                    float r = radii[i];
                    Color c = (i == 0) ? mintBright : cyanGlow;
                    // Outer soft halo
                    using (var glowPen = new Pen(Color.FromArgb(65, c), S * 0.085f)) {
                        glowPen.StartCap = LineCap.Round;
                        glowPen.EndCap = LineCap.Round;
                        g.DrawArc(glowPen, acx - r, acy - r, r * 2, r * 2, 205, 130);
                    }
                    // Crisp core arc
                    using (var arcPen = new Pen(c, S * 0.048f)) {
                        arcPen.StartCap = LineCap.Round;
                        arcPen.EndCap = LineCap.Round;
                        g.DrawArc(arcPen, acx - r, acy - r, r * 2, r * 2, 205, 130);
                    }
                }
                // Center sync node dot
                float dotR = S * 0.036f;
                using (var b = new SolidBrush(mintBright)) {
                    g.FillEllipse(b, acx - dotR, acy - dotR, dotR * 2, dotR * 2);
                }
            }

            // Two Studio Monitor Speaker Cabinets (Left & Right)
            float cw = S * (tiny ? 0.31f : 0.285f);
            float ch = S * (tiny ? 0.46f : 0.435f);
            float cy = S * (tiny ? 0.44f : 0.435f);
            float[] xs = tiny
                ? new float[] { S * 0.12f, S * 0.57f }
                : new float[] { S * 0.145f, S * 0.570f };

            foreach (float x in xs) {
                // Drop shadow under cabinet
                if (!tiny) {
                    using (var shPath = RoundRect(x + S * 0.01f, cy + S * 0.02f, cw, ch, S * 0.055f))
                    using (var shBr = new SolidBrush(Color.FromArgb(80, 8, 12, 28))) {
                        g.FillPath(shBr, shPath);
                    }
                }

                // Cabinet body
                using (var cabPath = RoundRect(x, cy, cw, ch, S * 0.055f))
                using (var cabBr = new LinearGradientBrush(
                    new RectangleF(x, cy, cw, ch),
                    Color.FromArgb(255, 250, 252, 255),
                    Color.FromArgb(255, 218, 228, 248),
                    90f)) {
                    g.FillPath(cabBr, cabPath);
                }

                float cx = x + cw * 0.5f;
                float wy = cy + ch * 0.63f;
                float wr = cw * 0.34f;

                // Woofer surround ring
                using (var darkBr = new SolidBrush(Color.FromArgb(255, 26, 32, 62))) {
                    g.FillEllipse(darkBr, cx - wr, wy - wr, wr * 2, wr * 2);
                }

                if (!tiny) {
                    // Metallic cone ring
                    float mr = wr * 0.72f;
                    using (var ringPen = new Pen(Color.FromArgb(255, 96, 165, 250), Math.Max(1f, S * 0.018f))) {
                        g.DrawEllipse(ringPen, cx - mr, wy - mr, mr * 2, mr * 2);
                    }
                    // Center dust cap
                    float ir = wr * 0.36f;
                    using (var capBr = new SolidBrush(Color.FromArgb(255, 56, 189, 248))) {
                        g.FillEllipse(capBr, cx - ir, wy - ir, ir * 2, ir * 2);
                    }
                    // Tweeter
                    float tr = cw * 0.135f;
                    float ty = cy + ch * 0.23f;
                    using (var twBr = new SolidBrush(Color.FromArgb(255, 26, 32, 62))) {
                        g.FillEllipse(twBr, cx - tr, ty - tr, tr * 2, tr * 2);
                    }
                    float tir = tr * 0.45f;
                    using (var ticBr = new SolidBrush(Color.FromArgb(255, 74, 222, 128))) {
                        g.FillEllipse(ticBr, cx - tir, ty - tir, tir * 2, tir * 2);
                    }
                }
            }
        }
        return bmp;
    }

    static byte[] ToDib(Bitmap b) {
        int w = b.Width, h = b.Height;
        int maskStride = ((w + 31) / 32) * 4;
        using (var ms = new MemoryStream())
        using (var bw = new BinaryWriter(ms)) {
            bw.Write(40); bw.Write(w); bw.Write(h * 2); bw.Write((short)1); bw.Write((short)32);
            bw.Write(0); bw.Write(w * h * 4 + maskStride * h); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
            for (int y = h - 1; y >= 0; y--)
                for (int x = 0; x < w; x++) {
                    Color c = b.GetPixel(x, y);
                    bw.Write(c.B); bw.Write(c.G); bw.Write(c.R); bw.Write(c.A);
                }
            for (int y = h - 1; y >= 0; y--) {
                byte[] row = new byte[maskStride];
                for (int x = 0; x < w; x++)
                    if (b.GetPixel(x, y).A < 128) row[x / 8] |= (byte)(0x80 >> (x % 8));
                bw.Write(row);
            }
            return ms.ToArray();
        }
    }

    static byte[] ToPng(Bitmap b) {
        using (var ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); return ms.ToArray(); }
    }

    public static void WriteIco(string outIco, string outPreviewPng) {
        int[] sizes = new int[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        var images = new List<byte[]>();
        var bitmaps = new List<Bitmap>();
        foreach (int s in sizes) {
            var bmp = RenderIconBitmap(s);
            bitmaps.Add(bmp);
            images.Add(s >= 256 ? ToPng(bmp) : ToDib(bmp));
        }

        using (var fs = File.Create(outIco))
        using (var bw = new BinaryWriter(fs)) {
            bw.Write((short)0); bw.Write((short)1); bw.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++) {
                int s = sizes[i];
                bw.Write((byte)(s >= 256 ? 0 : s)); bw.Write((byte)(s >= 256 ? 0 : s));
                bw.Write((byte)0); bw.Write((byte)0); bw.Write((short)1); bw.Write((short)32);
                bw.Write(images[i].Length); bw.Write(offset);
                offset += images[i].Length;
            }
            foreach (var img in images) bw.Write(img);
        }

        if (!string.IsNullOrEmpty(outPreviewPng)) {
            int W = 24; foreach (int s in sizes) W += s + 20;
            int H = 256 * 2 + 64;
            using (var sheet = new Bitmap(W, H))
            using (var g = Graphics.FromImage(sheet)) {
                g.Clear(Color.FromArgb(20, 22, 34));
                using (var light = new SolidBrush(Color.FromArgb(242, 244, 248))) g.FillRectangle(light, 0, H / 2, W, H / 2);
                int x = 20;
                for (int i = 0; i < sizes.Length; i++) {
                    int s = sizes[i];
                    g.DrawImage(bitmaps[i], x, (H / 2 - s) / 2, s, s);
                    g.DrawImage(bitmaps[i], x, H / 2 + (H / 2 - s) / 2, s, s);
                    x += s + 20;
                }
                sheet.Save(outPreviewPng, ImageFormat.Png);
            }
        }
        foreach (var b in bitmaps) b.Dispose();
    }

    public static void WriteBanner(string outBannerPng) {
        int W = 1280, H = 640;
        using (var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(bmp)) {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // Deep dark studio gradient background
            using (var bg = new LinearGradientBrush(new Rectangle(0, 0, W, H),
                Color.FromArgb(255, 13, 17, 30), Color.FromArgb(255, 26, 22, 54), 35f)) {
                g.FillRectangle(bg, 0, 0, W, H);
            }

            // Ambient radial glows
            using (var gp = new GraphicsPath()) {
                gp.AddEllipse(-120, -140, 720, 720);
                using (var pgb = new PathGradientBrush(gp)) {
                    pgb.CenterColor = Color.FromArgb(55, 37, 99, 235);
                    pgb.SurroundColors = new Color[] { Color.FromArgb(0, 37, 99, 235) };
                    g.FillPath(pgb, gp);
                }
            }
            using (var gp2 = new GraphicsPath()) {
                gp2.AddEllipse(680, 120, 720, 620);
                using (var pgb2 = new PathGradientBrush(gp2)) {
                    pgb2.CenterColor = Color.FromArgb(50, 124, 58, 237);
                    pgb2.SurroundColors = new Color[] { Color.FromArgb(0, 124, 58, 237) };
                    g.FillPath(pgb2, gp2);
                }
            }

            // Subtle decorative waveform lines across lower background
            using (var wavePen1 = new Pen(Color.FromArgb(28, 56, 189, 248), 2f))
            using (var wavePen2 = new Pen(Color.FromArgb(22, 74, 222, 128), 2f)) {
                var pts1 = new PointF[128];
                var pts2 = new PointF[128];
                for (int i = 0; i < 128; i++) {
                    float x = i * (W / 127f);
                    float y1 = H * 0.84f + (float)Math.Sin(i * 0.16) * 24f * (float)Math.Sin(i * 0.035);
                    float y2 = H * 0.84f + (float)Math.Sin(i * 0.16 + 0.5) * 18f * (float)Math.Cos(i * 0.04);
                    pts1[i] = new PointF(x, y1);
                    pts2[i] = new PointF(x, y2);
                }
                g.DrawCurve(wavePen1, pts1);
                g.DrawCurve(wavePen2, pts2);
            }

            // Glass card container
            using (var cardPath = RoundRect(56, 56, W - 112, H - 112, 28))
            using (var cardBr = new SolidBrush(Color.FromArgb(145, 22, 25, 42)))
            using (var cardBorder = new Pen(Color.FromArgb(55, 148, 163, 184), 1.5f)) {
                g.FillPath(cardBr, cardPath);
                g.DrawPath(cardBorder, cardPath);
            }

            // Render high-res app icon on left
            using (var iconBmp = RenderIconBitmap(280)) {
                g.DrawImage(iconBmp, 110, 175, 280, 280);
            }

            // Right side typography
            float tx = 435f;
            // Top pill badge
            using (var pillPath = RoundRect(tx, 148, 360, 34, 17))
            using (var pillBr = new SolidBrush(Color.FromArgb(45, 56, 189, 248)))
            using (var pillPen = new Pen(Color.FromArgb(120, 56, 189, 248), 1f)) {
                g.FillPath(pillBr, pillPath);
                g.DrawPath(pillPen, pillPath);
            }
            using (var fPill = new Font("Segoe UI Semibold", 11f, FontStyle.Bold))
            using (var bPill = new SolidBrush(Color.FromArgb(255, 125, 211, 252))) {
                g.DrawString("WINDOWS REAL-TIME AUDIO ENGINE  \u2022  v2.0", fPill, bPill, tx + 16, 155);
            }

            // Main title
            using (var fTitle = new Font("Segoe UI", 40f, FontStyle.Bold))
            using (var bTitle = new SolidBrush(Color.White)) {
                g.DrawString("Dual Audio Synchronizer", fTitle, bTitle, tx - 4, 196);
            }

            // Subtitle
            using (var fSub = new Font("Segoe UI", 16.5f, FontStyle.Regular))
            using (var bSub = new SolidBrush(Color.FromArgb(255, 186, 198, 228))) {
                g.DrawString("Play crystal-clear, phase-locked audio through multiple speakers\n& headphones simultaneously with built-in Hearing Clarity EQ.", fSub, bSub, tx, 278);
            }

            // Feature badges row
            string[] badges = new string[] {
                "Event-Driven WASAPI",
                "\u00B11 ms Drift Lock",
                "2.8 kHz Voice Clarity EQ",
                "Multi-Speaker Balance"
            };
            Color[] badgeColors = new Color[] {
                Color.FromArgb(255, 74, 222, 128),
                Color.FromArgb(255, 56, 189, 248),
                Color.FromArgb(255, 250, 204, 21),
                Color.FromArgb(255, 192, 132, 252)
            };

            float bx = tx;
            float by = 402f;
            using (var fBadge = new Font("Segoe UI Semibold", 11.5f, FontStyle.Bold)) {
                for (int i = 0; i < badges.Length; i++) {
                    SizeF sz = g.MeasureString(badges[i], fBadge);
                    float bw = sz.Width + 42f;
                    float bh = 40f;
                    if (bx + bw > W - 80) {
                        bx = tx;
                        by += 52f;
                    }
                    using (var bp = RoundRect(bx, by, bw, bh, 12))
                    using (var bFill = new SolidBrush(Color.FromArgb(38, badgeColors[i])))
                    using (var bPen = new Pen(Color.FromArgb(125, badgeColors[i]), 1.3f))
                    using (var bTxt = new SolidBrush(badgeColors[i])) {
                        g.FillPath(bFill, bp);
                        g.DrawPath(bPen, bp);
                        g.FillEllipse(bTxt, bx + 14, by + 15, 10, 10);
                        g.DrawString(badges[i], fBadge, bTxt, bx + 28, by + 9);
                    }
                    bx += bw + 14f;
                }
            }

            bmp.Save(outBannerPng, ImageFormat.Png);
        }
    }

    public static void GenerateAll(string rootDir) {
        string assetsDir = Path.Combine(rootDir, "assets");
        string buildDir = Path.Combine(rootDir, "build");
        if (!Directory.Exists(assetsDir)) Directory.CreateDirectory(assetsDir);
        if (!Directory.Exists(buildDir)) Directory.CreateDirectory(buildDir);

        WriteIco(Path.Combine(assetsDir, "app.ico"), Path.Combine(buildDir, "icon_preview.png"));
        using (var logo = RenderIconBitmap(512)) {
            logo.Save(Path.Combine(assetsDir, "logo.png"), ImageFormat.Png);
        }
        WriteBanner(Path.Combine(assetsDir, "banner.png"));
        Console.WriteLine("Generated assets/app.ico, assets/logo.png, and assets/banner.png");
    }
}
