using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Dual Audio Synchronizer")]
[assembly: AssemblyDescription("Phase-Locked Multi-Speaker Audio Mirror & Hearing Clarity Engine for Windows")]
[assembly: AssemblyCompany("David Linacre (LIN4CRE)")]
[assembly: AssemblyProduct("Dual Audio Synchronizer")]
[assembly: AssemblyCopyright("Copyright (c) 2026 David Linacre. MIT License.")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

namespace DualAudioStudio {
    #region Win32 & WASAPI COM Interop
    static class NativeMethods {
        [DllImport("winmm.dll")]
        public static extern uint timeBeginPeriod(uint uPeriod);

        [DllImport("winmm.dll")]
        public static extern uint timeEndPeriod(uint uPeriod);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
        public static extern void CopyMemory(IntPtr dest, IntPtr src, uint count);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorComObject { }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr ppDevices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IntPtr ppEndpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IntPtr ppDevice);
    }

    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection {
        [PreserveSig] int GetCount(out int pcDevices);
        [PreserveSig] int Item(int nDevice, out IntPtr ppDevice);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice {
        [PreserveSig] int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, out IntPtr ppInterface);
        [PreserveSig] int OpenPropertyStore(int stgmAccess, out IntPtr ppProperties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        [PreserveSig] int GetState(out int pdwState);
    }

    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore {
        [PreserveSig] int GetCount(out int cProps);
        [PreserveSig] int GetAt(int iProp, out PropertyKey pkey);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant pv);
    }

    struct PropertyKey {
        public Guid fmtid;
        public int pid;
    }

    [StructLayout(LayoutKind.Explicit)]
    struct PropVariant {
        [FieldOffset(0)] public short vt;
        [FieldOffset(8)] public IntPtr pwszVal;
    }

    [Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioClient {
        [PreserveSig] int Initialize(int ShareMode, uint StreamFlags, long hnsBufferDuration, long hnsPeriodicity, IntPtr pFormat, IntPtr AudioSessionGuid);
        [PreserveSig] int GetBufferSize(out uint pNumBufferFrames);
        [PreserveSig] int GetStreamLatency(out long phnsLatency);
        [PreserveSig] int GetCurrentPadding(out uint pNumPaddingFrames);
        [PreserveSig] int IsFormatSupported(int ShareMode, IntPtr pFormat, out IntPtr ppClosestMatch);
        [PreserveSig] int GetMixFormat(out IntPtr ppDeviceFormat);
        [PreserveSig] int GetDevicePeriod(out long phnsDefaultDevicePeriod, out long phnsMinimumDevicePeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr eventHandle);
        [PreserveSig] int GetService(ref Guid riid, out IntPtr ppv);
    }

    [Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioCaptureClient {
        [PreserveSig] int GetBuffer(out IntPtr ppData, out uint pNumFramesToRead, out uint pdwFlags, out ulong pu64DevicePosition, out ulong pu64QPCPosition);
        [PreserveSig] int ReleaseBuffer(uint NumFramesRead);
        [PreserveSig] int GetNextPacketSize(out uint pNumFramesInNextPacket);
    }

    [Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioRenderClient {
        [PreserveSig] int GetBuffer(uint NumFramesRequested, out IntPtr ppData);
        [PreserveSig] int ReleaseBuffer(uint NumFramesWritten, uint dwFlags);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    struct WAVEFORMATEX {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }
    #endregion

    #region Brand Logo & Icon Renderer
    static class BrandArt {
        public static GraphicsPath RoundRect(float x, float y, float w, float h, float r) {
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

        public static Bitmap RenderLogo(int size) {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp)) {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.Clear(Color.Transparent);

                float S = size;
                bool tiny = size <= 20;
                float pad = tiny ? 0.5f : S * 0.045f;
                float tileW = S - pad * 2f;
                float corner = S * 0.23f;

                using (var tilePath = RoundRect(pad, pad, tileW, tileW, corner)) {
                    using (var bgBrush = new LinearGradientBrush(
                        new RectangleF(0, 0, S, S),
                        Color.FromArgb(255, 18, 28, 68),
                        Color.FromArgb(255, 124, 58, 237),
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

                    if (!tiny) {
                        using (var borderPen = new Pen(Color.FromArgb(80, 255, 255, 255), Math.Max(1f, S * 0.02f))) {
                            g.DrawPath(borderPen, tilePath);
                        }
                    }
                }

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
                        using (var glowPen = new Pen(Color.FromArgb(65, c), S * 0.085f)) {
                            glowPen.StartCap = LineCap.Round;
                            glowPen.EndCap = LineCap.Round;
                            g.DrawArc(glowPen, acx - r, acy - r, r * 2, r * 2, 205, 130);
                        }
                        using (var arcPen = new Pen(c, S * 0.048f)) {
                            arcPen.StartCap = LineCap.Round;
                            arcPen.EndCap = LineCap.Round;
                            g.DrawArc(arcPen, acx - r, acy - r, r * 2, r * 2, 205, 130);
                        }
                    }
                    float dotR = S * 0.036f;
                    using (var b = new SolidBrush(mintBright)) {
                        g.FillEllipse(b, acx - dotR, acy - dotR, dotR * 2, dotR * 2);
                    }
                }

                float cw = S * (tiny ? 0.31f : 0.285f);
                float ch = S * (tiny ? 0.46f : 0.435f);
                float cy = S * (tiny ? 0.44f : 0.435f);
                float[] xs = tiny
                    ? new float[] { S * 0.12f, S * 0.57f }
                    : new float[] { S * 0.145f, S * 0.570f };

                foreach (float x in xs) {
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

                    using (var darkBr = new SolidBrush(Color.FromArgb(255, 26, 32, 62))) {
                        g.FillEllipse(darkBr, cx - wr, wy - wr, wr * 2, wr * 2);
                    }

                    if (!tiny) {
                        float mr = wr * 0.72f;
                        using (var ringPen = new Pen(Color.FromArgb(255, 96, 165, 250), Math.Max(1f, S * 0.018f))) {
                            g.DrawEllipse(ringPen, cx - mr, wy - mr, mr * 2, mr * 2);
                        }
                        float ir = wr * 0.36f;
                        using (var capBr = new SolidBrush(Color.FromArgb(255, 56, 189, 248))) {
                            g.FillEllipse(capBr, cx - ir, wy - ir, ir * 2, ir * 2);
                        }
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

        public static Icon LoadAppIcon() {
            try {
                var asm = Assembly.GetExecutingAssembly();
                foreach (string resName in asm.GetManifestResourceNames()) {
                    if (resName.EndsWith("app.ico", StringComparison.OrdinalIgnoreCase)) {
                        using (Stream s = asm.GetManifestResourceStream(resName)) {
                            if (s != null) return new Icon(s);
                        }
                    }
                }
                Icon assoc = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (assoc != null) return assoc;
            } catch { }
            return SystemIcons.Application;
        }
    }
    #endregion

    #region DSP: Biquad Voice Clarity EQ & Elastic Drift-Locked Delay Buffer
    class BiquadPeakingFilter {
        private float b0 = 1f, b1, b2, a1, a2;
        private float x1, x2, y1, y2;

        public void Configure(float sampleRate, float centerFreq, float gainDb, float Q) {
            if (sampleRate <= 0) sampleRate = 44100f;
            float A = (float)Math.Pow(10.0, gainDb / 40.0);
            float w0 = (float)(2.0 * Math.PI * centerFreq / sampleRate);
            float alpha = (float)(Math.Sin(w0) / (2.0 * Q));

            float b0_raw = 1.0f + alpha * A;
            float b1_raw = -2.0f * (float)Math.Cos(w0);
            float b2_raw = 1.0f - alpha * A;
            float a0_raw = 1.0f + alpha / A;
            float a1_raw = -2.0f * (float)Math.Cos(w0);
            float a2_raw = 1.0f - alpha / A;

            b0 = b0_raw / a0_raw;
            b1 = b1_raw / a0_raw;
            b2 = b2_raw / a0_raw;
            a1 = a1_raw / a0_raw;
            a2 = a2_raw / a0_raw;
        }

        public void ResetState() {
            x1 = x2 = y1 = y2 = 0f;
        }

        public float Process(float inSample) {
            float outSample = b0 * inSample + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            // Denormal & NaN protection
            if (float.IsNaN(outSample) || float.IsInfinity(outSample)) {
                x1 = x2 = y1 = y2 = 0f;
                return inSample;
            }
            if (outSample > -1e-20f && outSample < 1e-20f) outSample = 0f;
            x2 = x1;
            x1 = inSample;
            y2 = y1;
            y1 = outSample;
            return outSample;
        }
    }

    // 0 = Off, 1 = Balanced Vocal (+3.5 dB + 50% Mono Blend), 2 = Extra Crisp (+5.5 dB + 65% Mono Blend)
    public enum ClarityPreset {
        Off = 0,
        BalancedVocal = 1,
        ExtraCrisp = 2
    }

    // 0 = Stereo (L+R), 1 = Mono Sum, 2 = Left Channel Only, 3 = Right Channel Only
    public enum SpeakerChannelMode {
        Stereo = 0,
        MonoSum = 1,
        LeftOnly = 2,
        RightOnly = 3
    }

    class ElasticSyncBuffer {
        private float[] ringL;
        private float[] ringR;
        private int capacityFrames;
        private int writeFrame;
        private int bufferedFrames;
        private int sampleRate;
        private int channels;
        private volatile int currentDelayMs;
        private ClarityPreset lastPreset = ClarityPreset.BalancedVocal;

        private BiquadPeakingFilter eqLeft = new BiquadPeakingFilter();
        private BiquadPeakingFilter eqRight = new BiquadPeakingFilter();

        public ElasticSyncBuffer(int sampleRate, int channels, int maxDelayMs = 1200) {
            this.sampleRate = Math.Max(8000, sampleRate);
            this.channels = Math.Max(1, channels);
            this.capacityFrames = (int)((long)this.sampleRate * maxDelayMs / 1000) + 8192;
            this.ringL = new float[capacityFrames];
            this.ringR = new float[capacityFrames];
            this.writeFrame = 0;
            this.bufferedFrames = 0;
            UpdateEq(ClarityPreset.BalancedVocal);
        }

        private void UpdateEq(ClarityPreset preset) {
            lastPreset = preset;
            float gain = (preset == ClarityPreset.ExtraCrisp) ? 5.5f : 3.5f;
            eqLeft.Configure(sampleRate, 2800f, gain, 0.95f);
            eqRight.Configure(sampleRate, 2800f, gain, 0.95f);
        }

        public int DelayMs {
            get { return currentDelayMs; }
            set { currentDelayMs = Math.Max(0, Math.Min(500, value)); }
        }

        public unsafe void PushInput(IntPtr pInput, int inFrames, bool isSilent) {
            if (inFrames <= 0) return;
            float* pIn = (float*)pInput;
            for (int i = 0; i < inFrames; i++) {
                float l = 0f, r = 0f;
                if (!isSilent && pInput != IntPtr.Zero) {
                    if (channels >= 2) {
                        l = pIn[i * channels];
                        r = pIn[i * channels + 1];
                    } else {
                        l = r = pIn[i];
                    }
                }
                ringL[writeFrame] = l;
                ringR[writeFrame] = r;
                writeFrame++;
                if (writeFrame >= capacityFrames) writeFrame = 0;
            }
            bufferedFrames = Math.Min(capacityFrames - 64, bufferedFrames + inFrames);
        }

        // Renders outFrames into pOutput while reading inFrames consumed at the target delay offset.
        // When outFrames != inFrames (e.g. +-1 frame drift correction), uses linear interpolation so there is zero click!
        public unsafe void RenderOutput(IntPtr pOutput, int inFrames, int outFrames, float volume, bool isMuted, ClarityPreset preset, SpeakerChannelMode channelMode = SpeakerChannelMode.Stereo) {
            if (outFrames <= 0 || pOutput == IntPtr.Zero) return;
            if (preset != lastPreset) UpdateEq(preset);

            float* pOut = (float*)pOutput;
            if (isMuted || volume <= 0.0005f) {
                int totalFloats = outFrames * channels;
                for (int i = 0; i < totalFloats; i++) pOut[i] = 0f;
                return;
            }

            int delayFrames = (int)((long)sampleRate * currentDelayMs / 1000);
            int totalBack = delayFrames + inFrames;
            if (totalBack > capacityFrames - 8) totalBack = capacityFrames - 8;

            int startFrame = writeFrame - totalBack;
            while (startFrame < 0) startFrame += capacityFrames;

            float monoMix = (preset == ClarityPreset.ExtraCrisp) ? 0.65f : (preset == ClarityPreset.BalancedVocal ? 0.50f : 0f);
            float stereoKeep = 1.0f - monoMix;
            bool applyEq = (preset != ClarityPreset.Off);

            float step = (outFrames > 1 && inFrames != outFrames)
                ? (float)(inFrames - 1) / (float)(outFrames - 1)
                : 1.0f;

            for (int i = 0; i < outFrames; i++) {
                float l, r;
                if (inFrames == outFrames) {
                    int idx = startFrame + i;
                    if (idx >= capacityFrames) idx -= capacityFrames;
                    l = ringL[idx];
                    r = ringR[idx];
                } else {
                    float pos = i * step;
                    int idx0 = (int)pos;
                    float frac = pos - idx0;
                    int a = (startFrame + idx0) % capacityFrames;
                    int b = a + 1;
                    if (b >= capacityFrames) b = 0;
                    l = ringL[a] + (ringL[b] - ringL[a]) * frac;
                    r = ringR[a] + (ringR[b] - ringR[a]) * frac;
                }

                if (channelMode == SpeakerChannelMode.MonoSum) {
                    l = r = (l + r) * 0.5f;
                } else if (channelMode == SpeakerChannelMode.LeftOnly) {
                    r = l;
                } else if (channelMode == SpeakerChannelMode.RightOnly) {
                    l = r;
                }

                if (applyEq) {
                    float center = (l + r) * 0.5f;
                    l = l * stereoKeep + center * monoMix;
                    r = r * stereoKeep + center * monoMix;
                    l = eqLeft.Process(l);
                    r = eqRight.Process(r);
                }

                l *= volume;
                r *= volume;

                // Soft knee limiter to prevent harsh digital clipping at >100% volume or EQ boost
                if (l > 0.98f) l = 0.98f + 0.02f * (float)Math.Tanh((l - 0.98f) * 10f);
                else if (l < -0.98f) l = -0.98f + 0.02f * (float)Math.Tanh((l + 0.98f) * 10f);
                if (r > 0.98f) r = 0.98f + 0.02f * (float)Math.Tanh((r - 0.98f) * 10f);
                else if (r < -0.98f) r = -0.98f + 0.02f * (float)Math.Tanh((r + 0.98f) * 10f);

                if (channels >= 2) {
                    pOut[i * channels] = l;
                    pOut[i * channels + 1] = r;
                    for (int c = 2; c < channels; c++) pOut[i * channels + c] = 0f;
                } else {
                    pOut[i] = (l + r) * 0.5f;
                }
            }
        }
    }

    class SecondarySpeaker {
        public string Id;
        public string Name;
        public volatile bool Enabled;
        public volatile int DelayMs;
        public volatile float Volume = 1.0f;
        public volatile bool IsMuted;
        public volatile SpeakerChannelMode ChannelMode = SpeakerChannelMode.Stereo;

        public IMMDevice Device;
        public IAudioClient Client;
        public IAudioRenderClient RenderClient;
        public uint RenderBufferSize;
        public ElasticSyncBuffer SyncBuffer;
        public volatile bool Initialized;
        public volatile float AvgBufferMs = 10.0f;
        public volatile int DriftCorrections;
    }

    class AudioEngine {
        const uint AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
        const uint AUDCLNT_STREAMFLAGS_EVENTCALLBACK = 0x00040000;
        const uint AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM = 0x80000000;
        const uint AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY = 0x08000000;
        const uint AUDCLNT_BUFFERFLAGS_SILENT = 0x02;

        public volatile bool IsRunning = true;
        public volatile bool IsPaused = false;
        public volatile ClarityPreset VoiceClarityMode = ClarityPreset.BalancedVocal;
        public string SelectedPrimaryId = ""; // "" = Follow Windows Default
        public string ActivePrimaryId = "";
        public string ActivePrimaryName = "";
        public volatile int SampleRate = 44100;
        public volatile float PeakLevelLeft = 0f;
        public volatile float PeakLevelRight = 0f;

        public List<SecondarySpeaker> Speakers = new List<SecondarySpeaker>();
        private readonly object syncLock = new object();
        private Thread audioThread;

        public void Start() {
            NativeMethods.timeBeginPeriod(1);
            audioThread = new Thread(AudioLoop);
            audioThread.Name = "DualAudio_WASAPI_RealtimeEngine";
            audioThread.Priority = ThreadPriority.Highest;
            audioThread.IsBackground = true;
            audioThread.Start();
        }

        public void Stop() {
            IsRunning = false;
            try { NativeMethods.timeEndPeriod(1); } catch { }
        }

        public void LockExecute(Action action) {
            lock (syncLock) { action(); }
        }

        private unsafe void MeasurePeakLevels(IntPtr pData, int frames, int channels, bool isSilent) {
            if (isSilent || pData == IntPtr.Zero || frames <= 0) {
                PeakLevelLeft *= 0.82f;
                PeakLevelRight *= 0.82f;
                return;
            }
            float* p = (float*)pData;
            float maxL = 0f, maxR = 0f;
            int step = Math.Max(1, frames / 64);
            for (int i = 0; i < frames; i += step) {
                float l = Math.Abs(p[i * channels]);
                float r = (channels >= 2) ? Math.Abs(p[i * channels + 1]) : l;
                if (l > maxL) maxL = l;
                if (r > maxR) maxR = r;
            }
            PeakLevelLeft = Math.Max(maxL, PeakLevelLeft * 0.80f);
            PeakLevelRight = Math.Max(maxR, PeakLevelRight * 0.80f);
        }

        private void AudioLoop() {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            Guid iidAudioClient = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
            Guid iidCapture = new Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
            Guid iidRender = new Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");

            while (IsRunning) {
                IMMDevice primaryDev = null;
                IAudioClient primaryClient = null;
                IAudioCaptureClient captureClient = null;
                IntPtr hCaptureEvent = IntPtr.Zero;
                WAVEFORMATEX mixFormat = new WAVEFORMATEX();

                try {
                    IntPtr pPrimary = IntPtr.Zero;
                    if (string.IsNullOrEmpty(SelectedPrimaryId)) {
                        enumerator.GetDefaultAudioEndpoint(0, 1, out pPrimary);
                    } else {
                        enumerator.GetDevice(SelectedPrimaryId, out pPrimary);
                        if (pPrimary == IntPtr.Zero) {
                            enumerator.GetDefaultAudioEndpoint(0, 1, out pPrimary);
                        }
                    }

                    if (pPrimary == IntPtr.Zero) {
                        Thread.Sleep(500);
                        continue;
                    }

                    primaryDev = (IMMDevice)Marshal.GetObjectForIUnknown(pPrimary);
                    string resolvedId;
                    primaryDev.GetId(out resolvedId);
                    ActivePrimaryId = resolvedId ?? "";
                    ActivePrimaryName = DeviceHelper.GetDeviceName(primaryDev);

                    IntPtr pClient;
                    primaryDev.Activate(ref iidAudioClient, 1, IntPtr.Zero, out pClient);
                    primaryClient = (IAudioClient)Marshal.GetObjectForIUnknown(pClient);

                    IntPtr pMixFormat;
                    primaryClient.GetMixFormat(out pMixFormat);
                    mixFormat = (WAVEFORMATEX)Marshal.PtrToStructure(pMixFormat, typeof(WAVEFORMATEX));
                    SampleRate = (int)mixFormat.nSamplesPerSec;

                    // Try event-driven loopback first (lowest jitter), fallback to timer-driven 20ms loopback
                    bool useEvent = false;
                    hCaptureEvent = NativeMethods.CreateEvent(IntPtr.Zero, false, false, null);
                    int hr = -1;
                    if (hCaptureEvent != IntPtr.Zero) {
                        hr = primaryClient.Initialize(0, AUDCLNT_STREAMFLAGS_LOOPBACK | AUDCLNT_STREAMFLAGS_EVENTCALLBACK, 200000, 0, pMixFormat, IntPtr.Zero);
                        if (hr == 0 && primaryClient.SetEventHandle(hCaptureEvent) == 0) {
                            useEvent = true;
                        }
                    }
                    if (!useEvent) {
                        pClient = IntPtr.Zero;
                        primaryDev.Activate(ref iidAudioClient, 1, IntPtr.Zero, out pClient);
                        primaryClient = (IAudioClient)Marshal.GetObjectForIUnknown(pClient);
                        hr = primaryClient.Initialize(0, AUDCLNT_STREAMFLAGS_LOOPBACK, 200000, 0, pMixFormat, IntPtr.Zero);
                    }
                    if (hr != 0) {
                        Thread.Sleep(500);
                        continue;
                    }

                    IntPtr pCapture;
                    primaryClient.GetService(ref iidCapture, out pCapture);
                    captureClient = (IAudioCaptureClient)Marshal.GetObjectForIUnknown(pCapture);
                    primaryClient.Start();

                    // Immediately initialize all enabled secondary speakers before entering packet loop
                    lock (syncLock) {
                        foreach (var spk in Speakers) {
                            if (spk.Enabled && !spk.Initialized && spk.Id != ActivePrimaryId) {
                                InitSecondarySpeaker(spk, enumerator, iidAudioClient, iidRender, pMixFormat, mixFormat);
                            }
                        }
                    }

                    int defaultCheckTick = 0;
                    string expectedSelection = SelectedPrimaryId;

                    // Target queue depth on secondary hardware buffer: 10 ms (~441 frames @ 44.1 kHz)
                    int targetPaddingFrames = Math.Max(220, (int)(mixFormat.nSamplesPerSec * 10 / 1000));
                    int highDriftFrames = targetPaddingFrames + Math.Max(140, (int)(mixFormat.nSamplesPerSec * 3 / 1000));
                    int maxSpikeFrames = targetPaddingFrames + Math.Max(900, (int)(mixFormat.nSamplesPerSec * 22 / 1000));
                    int lowUnderrunFrames = Math.Max(90, (int)(mixFormat.nSamplesPerSec * 2 / 1000));

                    while (IsRunning && expectedSelection == SelectedPrimaryId) {
                        if (IsPaused) {
                            PeakLevelLeft = 0f;
                            PeakLevelRight = 0f;
                            Thread.Sleep(30);
                            continue;
                        }

                        if (useEvent) {
                            NativeMethods.WaitForSingleObject(hCaptureEvent, 3);
                        } else {
                            Thread.Sleep(1);
                        }

                        // Periodically check if Windows Default Playback Device changed when in Auto-Default mode
                        defaultCheckTick++;
                        if (defaultCheckTick >= 80) {
                            defaultCheckTick = 0;
                            if (string.IsNullOrEmpty(SelectedPrimaryId)) {
                                string curDef = DeviceHelper.GetDefaultRenderDeviceId();
                                if (!string.IsNullOrEmpty(curDef) && curDef != ActivePrimaryId) {
                                    break; // Re-bind to the new default device
                                }
                            }
                            lock (syncLock) {
                                foreach (var spk in Speakers) {
                                    if (spk.Enabled && !spk.Initialized && spk.Id != ActivePrimaryId) {
                                        InitSecondarySpeaker(spk, enumerator, iidAudioClient, iidRender, pMixFormat, mixFormat);
                                    }
                                }
                            }
                        }

                        uint packetSize;
                        bool gotPacket = false;
                        while (captureClient.GetNextPacketSize(out packetSize) == 0 && packetSize > 0) {
                            gotPacket = true;
                            IntPtr pData;
                            uint numFrames;
                            uint flags;
                            ulong devPos, qpcPos;

                            hr = captureClient.GetBuffer(out pData, out numFrames, out flags, out devPos, out qpcPos);
                            if (hr != 0) break;

                            bool isSilent = (flags & AUDCLNT_BUFFERFLAGS_SILENT) != 0;
                            MeasurePeakLevels(pData, (int)numFrames, mixFormat.nChannels, isSilent);

                            ClarityPreset clarity = VoiceClarityMode;

                            lock (syncLock) {
                                foreach (var spk in Speakers) {
                                    if (!spk.Enabled || spk.Id == ActivePrimaryId) continue;
                                    if (!spk.Initialized || spk.RenderClient == null) {
                                        InitSecondarySpeaker(spk, enumerator, iidAudioClient, iidRender, pMixFormat, mixFormat);
                                        if (!spk.Initialized) continue;
                                    }

                                    try {
                                        spk.SyncBuffer.DelayMs = spk.DelayMs;
                                        spk.SyncBuffer.PushInput(pData, (int)numFrames, isSilent);

                                        uint padding;
                                        if (spk.Client.GetCurrentPadding(out padding) != 0) {
                                            spk.Initialized = false;
                                            continue;
                                        }

                                        // Update exponential moving average of hardware buffer depth
                                        float padMs = (padding * 1000.0f) / Math.Max(1u, mixFormat.nSamplesPerSec);
                                        spk.AvgBufferMs = spk.AvgBufferMs * 0.90f + padMs * 0.10f;

                                        uint avail = spk.RenderBufferSize > padding ? spk.RenderBufferSize - padding : 0;
                                        int inFrames = (int)numFrames;
                                        int outFrames = inFrames;

                                        // Active Clock-Drift Lock (keeps secondary speaker hardware queue pinned at ~10 ms)
                                        if (padding > maxSpikeFrames) {
                                            // Sudden Bluetooth stall/burst: drop excess frames immediately so latency never lingers
                                            outFrames = 0;
                                            spk.DriftCorrections++;
                                        } else if (spk.AvgBufferMs > 13.5f && outFrames > 32) {
                                            // Secondary clock slightly slower than primary: smoothly compress by 1 frame via linear interpolation
                                            outFrames = inFrames - 1;
                                            spk.DriftCorrections++;
                                        } else if (!isSilent && padding < lowUnderrunFrames && outFrames > 32) {
                                            // Secondary clock slightly faster than primary: smoothly stretch by 1 frame to prevent underrun click
                                            outFrames = inFrames + 1;
                                            spk.DriftCorrections++;
                                        }

                                        if (outFrames > (int)avail) outFrames = (int)avail;

                                        if (outFrames > 0) {
                                            IntPtr pRender;
                                            if (spk.RenderClient.GetBuffer((uint)outFrames, out pRender) == 0) {
                                                if (isSilent || spk.IsMuted) {
                                                    spk.RenderClient.ReleaseBuffer((uint)outFrames, AUDCLNT_BUFFERFLAGS_SILENT);
                                                } else {
                                                    spk.SyncBuffer.RenderOutput(pRender, inFrames, outFrames, spk.Volume, spk.IsMuted, clarity, spk.ChannelMode);
                                                    spk.RenderClient.ReleaseBuffer((uint)outFrames, 0);
                                                }
                                            }
                                        }
                                    } catch {
                                        spk.Initialized = false;
                                    }
                                }
                            }

                            captureClient.ReleaseBuffer(numFrames);
                        }

                        if (!gotPacket) {
                            PeakLevelLeft *= 0.92f;
                            PeakLevelRight *= 0.92f;
                        }
                    }
                } catch {
                    Thread.Sleep(500);
                } finally {
                    try { if (primaryClient != null) primaryClient.Stop(); } catch { }
                    if (hCaptureEvent != IntPtr.Zero) {
                        try { NativeMethods.CloseHandle(hCaptureEvent); } catch { }
                    }
                    lock (syncLock) {
                        foreach (var spk in Speakers) {
                            try { if (spk.Client != null) spk.Client.Stop(); } catch { }
                            spk.Initialized = false;
                        }
                    }
                }
            }
        }

        private void InitSecondarySpeaker(SecondarySpeaker spk, IMMDeviceEnumerator enumerator, Guid iidClient, Guid iidRender, IntPtr pMixFormat, WAVEFORMATEX mixFormat) {
            try {
                IntPtr pDev;
                int hr = enumerator.GetDevice(spk.Id, out pDev);
                if (hr != 0 || pDev == IntPtr.Zero) return;

                spk.Device = (IMMDevice)Marshal.GetObjectForIUnknown(pDev);
                IntPtr pClient;
                spk.Device.Activate(ref iidClient, 1, IntPtr.Zero, out pClient);
                spk.Client = (IAudioClient)Marshal.GetObjectForIUnknown(pClient);

                // 80ms hardware capacity prevents overflow on Bluetooth jitter, while Active Drift Lock keeps actual queue at ~10ms
                uint flags = AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY;
                hr = spk.Client.Initialize(0, flags, 800000, 0, pMixFormat, IntPtr.Zero);
                if (hr != 0) hr = spk.Client.Initialize(0, 0, 800000, 0, pMixFormat, IntPtr.Zero);
                if (hr != 0) return;

                IntPtr pRender;
                spk.Client.GetService(ref iidRender, out pRender);
                spk.RenderClient = (IAudioRenderClient)Marshal.GetObjectForIUnknown(pRender);

                spk.Client.GetBufferSize(out spk.RenderBufferSize);
                spk.SyncBuffer = new ElasticSyncBuffer((int)mixFormat.nSamplesPerSec, mixFormat.nChannels, 1200);
                spk.SyncBuffer.DelayMs = spk.DelayMs;
                spk.AvgBufferMs = 10.0f;

                spk.Client.Start();
                spk.Initialized = true;
            } catch {
                spk.Initialized = false;
            }
        }
    }

    static class DeviceHelper {
        public static string GetDeviceName(IMMDevice dev) {
            try {
                IntPtr pStore;
                dev.OpenPropertyStore(0, out pStore);
                if (pStore == IntPtr.Zero) return "Unknown Device";
                var store = (IPropertyStore)Marshal.GetObjectForIUnknown(pStore);
                var key = new PropertyKey { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
                PropVariant val;
                store.GetValue(ref key, out val);
                return Marshal.PtrToStringUni(val.pwszVal) ?? "Unknown Device";
            } catch {
                return "Unknown Device";
            }
        }

        public static List<Tuple<string, string>> GetAllRenderDevices() {
            var list = new List<Tuple<string, string>>();
            try {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                IntPtr pColl;
                enumerator.EnumAudioEndpoints(0, 1, out pColl);
                var coll = (IMMDeviceCollection)Marshal.GetObjectForIUnknown(pColl);
                int count;
                coll.GetCount(out count);
                for (int i = 0; i < count; i++) {
                    IntPtr pDev;
                    coll.Item(i, out pDev);
                    var dev = (IMMDevice)Marshal.GetObjectForIUnknown(pDev);
                    string id;
                    dev.GetId(out id);
                    string name = GetDeviceName(dev);
                    list.Add(new Tuple<string, string>(id, name));
                }
            } catch { }
            return list;
        }

        public static string GetDefaultRenderDeviceId() {
            try {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                IntPtr pDev;
                if (enumerator.GetDefaultAudioEndpoint(0, 1, out pDev) == 0 && pDev != IntPtr.Zero) {
                    var dev = (IMMDevice)Marshal.GetObjectForIUnknown(pDev);
                    string id;
                    dev.GetId(out id);
                    return id;
                }
            } catch { }
            return "";
        }
    }
    #endregion

    #region Custom Modern UI Controls
    class ModernButton : Button {
        public Color BaseColor = Color.FromArgb(40, 44, 68);
        public Color HoverColor = Color.FromArgb(56, 62, 94);
        public Color BorderColor = Color.FromArgb(80, 137, 180, 250);
        public int CornerRadius = 8;
        private bool isHovered = false;

        public ModernButton() {
            this.FlatStyle = FlatStyle.Flat;
            this.FlatAppearance.BorderSize = 0;
            this.BackColor = Color.Transparent;
            this.ForeColor = Color.White;
            this.Cursor = Cursors.Hand;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnMouseEnter(EventArgs e) { isHovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { isHovered = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(this.Parent != null ? this.Parent.BackColor : Color.FromArgb(24, 24, 37));

            var rect = new RectangleF(1f, 1f, this.Width - 2.5f, this.Height - 2.5f);
            using (var path = BrandArt.RoundRect(rect.X, rect.Y, rect.Width, rect.Height, CornerRadius))
            using (var br = new SolidBrush(isHovered ? HoverColor : BaseColor))
            using (var pen = new Pen(BorderColor, 1.2f)) {
                g.FillPath(br, path);
                if (BorderColor.A > 0) g.DrawPath(pen, path);
            }

            TextRenderer.DrawText(g, this.Text, this.Font, new Rectangle(0, 0, this.Width, this.Height),
                this.ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    class ToggleSwitch : Control {
        private bool isChecked = false;
        public event EventHandler CheckedChanged;

        public bool Checked {
            get { return isChecked; }
            set {
                if (isChecked != value) {
                    isChecked = value;
                    Invalidate();
                    if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
                }
            }
        }

        public ToggleSwitch() {
            this.Size = new Size(52, 26);
            this.Cursor = Cursors.Hand;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnClick(EventArgs e) {
            Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(this.Parent != null ? this.Parent.BackColor : Color.FromArgb(26, 28, 44));

            float w = this.Width - 2f, h = this.Height - 2f;
            Color trackColor = isChecked ? Color.FromArgb(37, 99, 235) : Color.FromArgb(52, 56, 80);
            Color borderColor = isChecked ? Color.FromArgb(96, 165, 250) : Color.FromArgb(82, 88, 116);

            using (var path = BrandArt.RoundRect(1f, 1f, w, h, h * 0.5f))
            using (var br = new SolidBrush(trackColor))
            using (var pen = new Pen(borderColor, 1.2f)) {
                g.FillPath(br, path);
                g.DrawPath(pen, path);
            }

            float knobD = h - 6f;
            float knobX = isChecked ? (w - knobD - 2f) : 4f;
            using (var knobBr = new SolidBrush(isChecked ? Color.FromArgb(166, 227, 161) : Color.FromArgb(203, 213, 225))) {
                g.FillEllipse(knobBr, knobX, 4f, knobD, knobD);
            }
        }
    }

    class ModernSlider : Control {
        private int min = 0, max = 100, val = 0;
        public Color AccentColor = Color.FromArgb(56, 189, 248);
        public event EventHandler ValueChanged;
        private bool dragging = false;

        public int Minimum { get { return min; } set { min = value; Invalidate(); } }
        public int Maximum { get { return max; } set { max = Math.Max(min + 1, value); Invalidate(); } }
        public int Value {
            get { return val; }
            set {
                int clamped = Math.Max(min, Math.Min(max, value));
                if (val != clamped) {
                    val = clamped;
                    Invalidate();
                }
            }
        }

        public ModernSlider() {
            this.Size = new Size(260, 26);
            this.Cursor = Cursors.Hand;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        private void SetFromMouse(int mouseX) {
            float pad = 10f;
            float usable = Math.Max(1f, this.Width - pad * 2f);
            float ratio = Math.Max(0f, Math.Min(1f, (mouseX - pad) / usable));
            int newVal = min + (int)Math.Round(ratio * (max - min));
            if (newVal != val) {
                val = newVal;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e) {
            if (e.Button == MouseButtons.Left) {
                dragging = true;
                SetFromMouse(e.X);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e) {
            if (dragging) SetFromMouse(e.X);
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e) {
            dragging = false;
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(this.Parent != null ? this.Parent.BackColor : Color.FromArgb(26, 28, 44));

            float pad = 10f;
            float trackY = this.Height * 0.5f - 3f;
            float trackW = this.Width - pad * 2f;
            float ratio = (float)(val - min) / Math.Max(1, max - min);
            float fillW = Math.Max(4f, trackW * ratio);

            using (var trackPath = BrandArt.RoundRect(pad, trackY, trackW, 6f, 3f))
            using (var trackBr = new SolidBrush(Color.FromArgb(45, 50, 74))) {
                g.FillPath(trackBr, trackPath);
            }

            if (val > min) {
                using (var fillPath = BrandArt.RoundRect(pad, trackY, fillW, 6f, 3f))
                using (var fillBr = new SolidBrush(AccentColor)) {
                    g.FillPath(fillBr, fillPath);
                }
            }

            float kx = pad + trackW * ratio;
            float ky = this.Height * 0.5f;
            using (var glowBr = new SolidBrush(Color.FromArgb(55, AccentColor))) {
                g.FillEllipse(glowBr, kx - 9f, ky - 9f, 18f, 18f);
            }
            using (var knobBr = new SolidBrush(Color.White))
            using (var knobPen = new Pen(AccentColor, 2f)) {
                g.FillEllipse(knobBr, kx - 6.5f, ky - 6.5f, 13f, 13f);
                g.DrawEllipse(knobPen, kx - 6.5f, ky - 6.5f, 13f, 13f);
            }
        }
    }

    class LevelMeterBar : Control {
        public float LevelL = 0f;
        public float LevelR = 0f;

        public LevelMeterBar() {
            this.Size = new Size(160, 22);
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(this.Parent != null ? this.Parent.BackColor : Color.FromArgb(22, 24, 38));

            float w = this.Width - 2f;
            float barH = 6f;
            float[] levels = new float[] { Math.Min(1f, LevelL * 1.4f), Math.Min(1f, LevelR * 1.4f) };
            float[] ys = new float[] { 3f, 12f };

            for (int i = 0; i < 2; i++) {
                using (var bgPath = BrandArt.RoundRect(1f, ys[i], w, barH, 3f))
                using (var bgBr = new SolidBrush(Color.FromArgb(38, 42, 64))) {
                    g.FillPath(bgBr, bgPath);
                }
                float fw = w * levels[i];
                if (fw > 2f) {
                    using (var fgPath = BrandArt.RoundRect(1f, ys[i], fw, barH, 3f))
                    using (var grad = new LinearGradientBrush(new RectangleF(0, 0, w, barH),
                        Color.FromArgb(74, 222, 128), Color.FromArgb(56, 189, 248), 0f)) {
                        g.FillPath(grad, fgPath);
                    }
                }
            }
        }
    }
    #endregion

    #region About Dialog
    class AboutDialog : Form {
        public AboutDialog(Icon appIcon) {
            this.Text = "About Dual Audio Synchronizer";
            this.Size = new Size(520, 420);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = Color.FromArgb(18, 20, 32);
            this.ForeColor = Color.White;
            this.Font = new Font("Segoe UI", 9.5f);
            this.Icon = appIcon;

            var picLogo = new PictureBox {
                Image = BrandArt.RenderLogo(96),
                Size = new Size(96, 96),
                SizeMode = PictureBoxSizeMode.Zoom,
                Location = new Point(28, 28),
                BackColor = Color.Transparent
            };

            var lblTitle = new Label {
                Text = "Dual Audio Synchronizer",
                Font = new Font("Segoe UI", 16f, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(140, 26)
            };

            var lblVer = new Label {
                Text = "Version 2.0.0  \u2022  Phase-Locked WASAPI Engine",
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248),
                AutoSize = true,
                Location = new Point(142, 58)
            };

            var lblDesc = new Label {
                Text = "Engineered for simultaneous zero-drift playback across multiple Bluetooth and wired speakers on Windows, with built-in Hearing Clarity DSP.",
                Font = new Font("Segoe UI", 9.2f),
                ForeColor = Color.FromArgb(186, 198, 228),
                Size = new Size(340, 52),
                Location = new Point(142, 84)
            };

            var infoCard = new Panel {
                Location = new Point(28, 148),
                Size = new Size(448, 165),
                BackColor = Color.FromArgb(26, 29, 46)
            };

            string[] bullets = new string[] {
                "\u2022  Event-Driven WASAPI Loopback + 1 ms High-Resolution Scheduler",
                "\u2022  Active Clock-Drift Controller (\u00B11 ms linear-interpolated phase lock)",
                "\u2022  2.8 kHz Biquad Peaking EQ + Center Mono Blend for Speech Clarity",
                "\u2022  Smart Bluetooth Reconnect & Single-Instance System Tray Guard"
            };
            int by = 16;
            foreach (string b in bullets) {
                var lbl = new Label {
                    Text = b,
                    Font = new Font("Segoe UI", 9f),
                    ForeColor = Color.FromArgb(215, 224, 245),
                    AutoSize = true,
                    Location = new Point(16, by)
                };
                infoCard.Controls.Add(lbl);
                by += 34;
            }

            var btnGitHub = new ModernButton {
                Text = "GitHub Repo",
                Size = new Size(134, 36),
                Location = new Point(28, 330),
                BaseColor = Color.FromArgb(37, 99, 235),
                HoverColor = Color.FromArgb(59, 130, 246),
                BorderColor = Color.FromArgb(120, 147, 197, 253),
                Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold)
            };
            btnGitHub.Click += (s, e) => {
                try { Process.Start("https://github.com/LIN4CRE/dual-audio-synchronizer"); } catch { }
            };

            var btnDonate = new ModernButton {
                Text = "\u2615 Buy a Coffee / Smoke (PayPal)",
                Size = new Size(204, 36),
                Location = new Point(168, 330),
                BaseColor = Color.FromArgb(180, 83, 9),
                HoverColor = Color.FromArgb(217, 119, 6),
                BorderColor = Color.FromArgb(160, 253, 224, 71),
                ForeColor = Color.FromArgb(254, 249, 195),
                Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold)
            };
            btnDonate.Click += (s, e) => {
                try { Process.Start("https://paypal.me/dlinacre16"); } catch { }
            };

            var btnClose = new ModernButton {
                Text = "Close",
                Size = new Size(98, 36),
                Location = new Point(378, 330),
                Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold)
            };
            btnClose.Click += (s, e) => this.Close();

            this.Controls.Add(picLogo);
            this.Controls.Add(lblTitle);
            this.Controls.Add(lblVer);
            this.Controls.Add(lblDesc);
            this.Controls.Add(infoCard);
            this.Controls.Add(btnGitHub);
            this.Controls.Add(btnDonate);
            this.Controls.Add(btnClose);
        }
    }
    #endregion

    #region Main Studio Window
    public class MainForm : Form {
        private AudioEngine engine;
        private NotifyIcon trayIcon;
        private Icon appIcon;
        private FlowLayoutPanel speakersPanel;
        private Label lblStatusPill;
        private Label lblPrimaryActive;
        private LevelMeterBar levelMeter;
        private ModernButton btnTogglePause;
        private ComboBox cbPrimary;
        private ComboBox cbClarity;
        private CheckBox chkStartup;
        private CheckBox chkCloseToTray;
        private System.Windows.Forms.Timer uiTimer;
        private List<Action> cardTelemetryUpdaters = new List<Action>();
        private bool isPopulating = false;
        private bool startMinimized = false;
        private string screenshotPath = null;

        private static string AppDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DualAudio");
        private static string SettingsFile = Path.Combine(AppDir, "settings.ini");

        public MainForm(bool startMinimized, string screenshotPath, EventWaitHandle showEvent) {
            this.startMinimized = startMinimized;
            this.screenshotPath = screenshotPath;
            this.appIcon = BrandArt.LoadAppIcon();

            InitializeComponent();
            LoadSettingsAndStart();

            if (showEvent != null) {
                Thread ipcThread = new Thread(() => {
                    while (engine != null && engine.IsRunning) {
                        try {
                            if (showEvent.WaitOne(1000)) {
                                this.BeginInvoke(new Action(RestoreFromTray));
                            }
                        } catch { break; }
                    }
                });
                ipcThread.IsBackground = true;
                ipcThread.Start();
            }
        }

        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);
            try {
                int dark = 1;
                NativeMethods.DwmSetWindowAttribute(this.Handle, 20, ref dark, sizeof(int));
            } catch { }
        }

        protected override void OnShown(EventArgs e) {
            base.OnShown(e);
            if (!string.IsNullOrEmpty(screenshotPath)) {
                var shotTimer = new System.Windows.Forms.Timer { Interval = 650 };
                shotTimer.Tick += (s, ev) => {
                    shotTimer.Stop();
                    try {
                        engine.PeakLevelLeft = 0.62f;
                        engine.PeakLevelRight = 0.58f;
                        levelMeter.LevelL = 0.62f;
                        levelMeter.LevelR = 0.58f;
                        levelMeter.Invalidate();
                        Application.DoEvents();
                        using (var bmp = new Bitmap(this.Width, this.Height)) {
                            this.DrawToBitmap(bmp, new Rectangle(0, 0, this.Width, this.Height));
                            bmp.Save(screenshotPath, ImageFormat.Png);
                        }
                    } catch { }
                    engine.Stop();
                    trayIcon.Visible = false;
                    Application.Exit();
                };
                shotTimer.Start();
            } else if (startMinimized) {
                this.WindowState = FormWindowState.Minimized;
                this.Hide();
            }
        }

        private void InitializeComponent() {
            this.Text = "Dual Audio Synchronizer";
            this.Size = new Size(880, 765);
            this.MinimumSize = new Size(840, 620);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(16, 18, 30);
            this.ForeColor = Color.FromArgb(226, 232, 240);
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            this.Icon = appIcon;
            this.DoubleBuffered = true;

            // 1. Top Studio Header Bar
            var headerPanel = new Panel {
                Dock = DockStyle.Top,
                Height = 92,
                BackColor = Color.FromArgb(22, 25, 42),
                Padding = new Padding(22, 14, 22, 14)
            };
            headerPanel.Paint += (s, e) => {
                using (var p = new Pen(Color.FromArgb(45, 148, 163, 184), 1f)) {
                    e.Graphics.DrawLine(p, 0, headerPanel.Height - 1, headerPanel.Width, headerPanel.Height - 1);
                }
            };

            var picLogo = new PictureBox {
                Image = BrandArt.RenderLogo(58),
                Size = new Size(58, 58),
                SizeMode = PictureBoxSizeMode.Zoom,
                Location = new Point(22, 17),
                BackColor = Color.Transparent
            };

            var lblTitle = new Label {
                Text = "Dual Audio Synchronizer",
                Font = new Font("Segoe UI", 16f, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(92, 14)
            };

            var lblSub = new Label {
                Text = "Phase-Locked Multi-Speaker Engine & Hearing Clarity Studio  \u2022  v2.0",
                UseMnemonic = false,
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(148, 163, 184),
                AutoSize = true,
                Location = new Point(95, 45)
            };

            lblStatusPill = new Label {
                Text = "\u25CF IN SYNC  \u2022  2 Speakers Active",
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(74, 222, 128),
                AutoSize = true,
                Location = new Point(95, 66)
            };

            var lblMeterCaption = new Label {
                Text = "LIVE SIGNAL",
                Font = new Font("Segoe UI Semibold", 7.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 116, 139),
                AutoSize = true,
                Location = new Point(470, 18)
            };

            levelMeter = new LevelMeterBar {
                Location = new Point(470, 36),
                Size = new Size(135, 22)
            };

            btnTogglePause = new ModernButton {
                Text = "Pause Sync",
                Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
                Size = new Size(108, 34),
                Location = new Point(622, 15),
                BaseColor = Color.FromArgb(37, 99, 235),
                HoverColor = Color.FromArgb(59, 130, 246),
                BorderColor = Color.FromArgb(110, 147, 197, 253)
            };
            btnTogglePause.Click += (s, e) => {
                engine.IsPaused = !engine.IsPaused;
                UpdateStatusUi();
            };

            var btnTestTone = new ModernButton {
                Text = "Test Sync Tone",
                Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold),
                Size = new Size(108, 30),
                Location = new Point(622, 53),
                BaseColor = Color.FromArgb(38, 43, 68),
                HoverColor = Color.FromArgb(52, 58, 90),
                BorderColor = Color.FromArgb(90, 250, 204, 21),
                ForeColor = Color.FromArgb(250, 204, 21)
            };
            btnTestTone.Click += (s, e) => PlaySyncChime();

            var btnRefresh = new ModernButton {
                Text = "Refresh",
                Font = new Font("Segoe UI Semibold", 9f),
                Size = new Size(96, 34),
                Location = new Point(740, 15)
            };
            btnRefresh.Click += (s, e) => RefreshDevices();

            var btnAbout = new ModernButton {
                Text = "About",
                Font = new Font("Segoe UI", 8.5f),
                Size = new Size(96, 30),
                Location = new Point(740, 53),
                ForeColor = Color.FromArgb(186, 198, 228)
            };
            btnAbout.Click += (s, e) => {
                using (var dlg = new AboutDialog(appIcon)) {
                    dlg.ShowDialog(this);
                }
            };

            headerPanel.Controls.Add(picLogo);
            headerPanel.Controls.Add(lblTitle);
            headerPanel.Controls.Add(lblSub);
            headerPanel.Controls.Add(lblStatusPill);
            headerPanel.Controls.Add(lblMeterCaption);
            headerPanel.Controls.Add(levelMeter);
            headerPanel.Controls.Add(btnTogglePause);
            headerPanel.Controls.Add(btnTestTone);
            headerPanel.Controls.Add(btnRefresh);
            headerPanel.Controls.Add(btnAbout);

            // 2. Primary Source & Hearing Clarity Control Strip
            var controlBar = new Panel {
                Dock = DockStyle.Top,
                Height = 82,
                BackColor = Color.FromArgb(20, 23, 38),
                Padding = new Padding(22, 10, 22, 10)
            };
            controlBar.Paint += (s, e) => {
                using (var p = new Pen(Color.FromArgb(40, 148, 163, 184), 1f)) {
                    e.Graphics.DrawLine(p, 0, controlBar.Height - 1, controlBar.Width, controlBar.Height - 1);
                }
            };

            var lblMaster = new Label {
                Text = "PRIMARY AUDIO SOURCE (LOOPBACK CAPTURE)",
                Font = new Font("Segoe UI Semibold", 8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248),
                Location = new Point(22, 10),
                AutoSize = true
            };

            cbPrimary = new ComboBox {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 420,
                Location = new Point(22, 32),
                BackColor = Color.FromArgb(34, 38, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f)
            };
            cbPrimary.SelectedIndexChanged += CbPrimary_SelectedIndexChanged;

            lblPrimaryActive = new Label {
                Text = "Capturing: Detecting...",
                Font = new Font("Segoe UI", 8.2f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(22, 60),
                AutoSize = true
            };

            var lblClarity = new Label {
                Text = "HEARING CLARITY & DIALOGUE ENHANCEMENT (DSP)",
                UseMnemonic = false,
                Font = new Font("Segoe UI Semibold", 8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(250, 204, 21),
                Location = new Point(468, 10),
                AutoSize = true
            };

            cbClarity = new ComboBox {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 368,
                Location = new Point(468, 32),
                BackColor = Color.FromArgb(34, 38, 60),
                ForeColor = Color.FromArgb(254, 240, 138),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold)
            };
            cbClarity.Items.Add("Balanced Vocal Clarity (+3.5 dB @ 2.8 kHz + Mono Blend) [Recommended]");
            cbClarity.Items.Add("Extra Crisp Speech (+5.5 dB @ 2.8 kHz + Strong Center Focus)");
            cbClarity.Items.Add("Flat Studio Pass-Through (No EQ / Original Stereo)");
            cbClarity.SelectedIndex = 0;
            cbClarity.SelectedIndexChanged += (s, e) => {
                if (engine == null) return;
                if (cbClarity.SelectedIndex == 0) engine.VoiceClarityMode = ClarityPreset.BalancedVocal;
                else if (cbClarity.SelectedIndex == 1) engine.VoiceClarityMode = ClarityPreset.ExtraCrisp;
                else engine.VoiceClarityMode = ClarityPreset.Off;
                if (!isPopulating) SaveSettings();
            };

            var lblClaritySub = new Label {
                Text = "Sharpens speech consonants (2.8 kHz EQ) & centers dialogue",
                UseMnemonic = false,
                Font = new Font("Segoe UI", 8.2f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(468, 60),
                AutoSize = true
            };

            controlBar.Controls.Add(lblMaster);
            controlBar.Controls.Add(cbPrimary);
            controlBar.Controls.Add(lblPrimaryActive);
            controlBar.Controls.Add(lblClarity);
            controlBar.Controls.Add(cbClarity);
            controlBar.Controls.Add(lblClaritySub);

            // 3. Section Subheader
            var listHeader = new Panel {
                Dock = DockStyle.Top,
                Height = 36,
                BackColor = Color.FromArgb(16, 18, 30),
                Padding = new Padding(22, 10, 22, 0)
            };
            var lblSecTitle = new Label {
                Text = "SECONDARY SPEAKERS  \u2022  Toggle ON to mirror audio simultaneously with automatic \u00B11 ms drift lock",
                Font = new Font("Segoe UI Semibold", 8.8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(22, 10),
                AutoSize = true
            };
            listHeader.Controls.Add(lblSecTitle);

            // 4. Bottom Footer Bar
            var bottomPanel = new Panel {
                Dock = DockStyle.Bottom,
                Height = 56,
                BackColor = Color.FromArgb(22, 25, 42),
                Padding = new Padding(22, 12, 22, 12)
            };
            bottomPanel.Paint += (s, e) => {
                using (var p = new Pen(Color.FromArgb(45, 148, 163, 184), 1f)) {
                    e.Graphics.DrawLine(p, 0, 0, bottomPanel.Width, 0);
                }
            };

            chkStartup = new CheckBox {
                Text = "Start with Windows (Tray)",
                ForeColor = Color.FromArgb(203, 213, 225),
                AutoSize = true,
                Location = new Point(22, 17),
                Checked = true,
                Cursor = Cursors.Hand
            };
            chkStartup.CheckedChanged += (s, e) => {
                if (!isPopulating) {
                    ToggleStartup(chkStartup.Checked);
                    SaveSettings();
                }
            };

            chkCloseToTray = new CheckBox {
                Text = "Keep in System Tray on close",
                ForeColor = Color.FromArgb(203, 213, 225),
                AutoSize = true,
                Location = new Point(236, 17),
                Checked = true,
                Cursor = Cursors.Hand
            };
            chkCloseToTray.CheckedChanged += (s, e) => {
                if (!isPopulating) SaveSettings();
            };

            var btnSupport = new ModernButton {
                Text = "\u2615 Buy a Coffee / Smoke",
                Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
                Size = new Size(192, 34),
                Location = new Point(488, 11),
                BaseColor = Color.FromArgb(180, 83, 9),
                HoverColor = Color.FromArgb(217, 119, 6),
                BorderColor = Color.FromArgb(165, 253, 224, 71),
                ForeColor = Color.FromArgb(254, 249, 195)
            };
            btnSupport.Click += (s, e) => {
                try { Process.Start("https://paypal.me/dlinacre16"); } catch { }
            };

            var btnSave = new ModernButton {
                Text = "Save Configuration",
                Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
                Size = new Size(146, 34),
                Location = new Point(690, 11),
                BaseColor = Color.FromArgb(22, 163, 74),
                HoverColor = Color.FromArgb(34, 197, 94),
                BorderColor = Color.FromArgb(120, 134, 239, 172)
            };
            btnSave.Click += (s, e) => {
                SaveSettings();
                lblStatusPill.Text = "\u2714 Settings Saved  \u2022  Phase Lock Active";
            };

            bottomPanel.Controls.Add(chkStartup);
            bottomPanel.Controls.Add(chkCloseToTray);
            bottomPanel.Controls.Add(btnSupport);
            bottomPanel.Controls.Add(btnSave);

            // 5. Center Scrollable Speaker Cards Area
            speakersPanel = new FlowLayoutPanel {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(22, 6, 22, 14),
                BackColor = Color.FromArgb(16, 18, 30)
            };

            // Add controls in correct WinForms dock z-order
            this.Controls.Add(speakersPanel);
            this.Controls.Add(listHeader);
            this.Controls.Add(controlBar);
            this.Controls.Add(headerPanel);
            this.Controls.Add(bottomPanel);

            // 6. System Tray Icon & Context Menu
            trayIcon = new NotifyIcon {
                Icon = appIcon,
                Text = "Dual Audio Synchronizer (Active)",
                Visible = true
            };
            trayIcon.DoubleClick += (s, e) => RestoreFromTray();

            var trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Open Studio Control Panel", null, (s, e) => RestoreFromTray());
            trayMenu.Items.Add("Pause / Resume Mirroring", null, (s, e) => {
                engine.IsPaused = !engine.IsPaused;
                UpdateStatusUi();
            });
            trayMenu.Items.Add("Play Sync Test Chime", null, (s, e) => PlaySyncChime());
            trayMenu.Items.Add("-");
            trayMenu.Items.Add("\u2615 Buy Dev a Coffee / Smoke (PayPal)", null, (s, e) => {
                try { Process.Start("https://paypal.me/dlinacre16"); } catch { }
            });
            trayMenu.Items.Add("-");
            trayMenu.Items.Add("Exit Dual Audio Synchronizer", null, (s, e) => {
                trayIcon.Visible = false;
                engine.Stop();
                Application.Exit();
            });
            trayIcon.ContextMenuStrip = trayMenu;

            this.FormClosing += (s, e) => {
                SaveSettings();
                if (chkCloseToTray.Checked && e.CloseReason == CloseReason.UserClosing) {
                    e.Cancel = true;
                    this.Hide();
                } else {
                    engine.Stop();
                    trayIcon.Visible = false;
                }
            };

            // 7. Live Telemetry Timer (updates VU meter and buffer lock status smoothly)
            uiTimer = new System.Windows.Forms.Timer { Interval = 50 };
            uiTimer.Tick += (s, e) => {
                if (engine == null || !this.Visible) return;
                levelMeter.LevelL = engine.PeakLevelLeft;
                levelMeter.LevelR = engine.PeakLevelRight;
                levelMeter.Invalidate();

                if (!string.IsNullOrEmpty(engine.ActivePrimaryName)) {
                    string targetTxt = "Capturing: " + engine.ActivePrimaryName + "  (" + engine.SampleRate + " Hz 32-bit Float)";
                    if (lblPrimaryActive.Text != targetTxt) lblPrimaryActive.Text = targetTxt;
                }
                foreach (var updater in cardTelemetryUpdaters) updater();
            };
            uiTimer.Start();
        }

        private void PlaySyncChime() {
            ThreadPool.QueueUserWorkItem(_ => {
                try {
                    Console.Beep(880, 90);
                    Thread.Sleep(30);
                    Console.Beep(1320, 120);
                } catch {
                    try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
                }
            });
        }

        public void RestoreFromTray() {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.Activate();
            this.BringToFront();
        }

        private void UpdateStatusUi() {
            if (engine == null) return;
            if (engine.IsPaused) {
                lblStatusPill.Text = "\u25A0 PAUSED  \u2022  Mirroring Suspended";
                lblStatusPill.ForeColor = Color.FromArgb(248, 113, 113);
                btnTogglePause.Text = "Resume Sync";
                btnTogglePause.BaseColor = Color.FromArgb(22, 163, 74);
            } else {
                int activeCount = 0;
                foreach (var s in engine.Speakers) if (s.Enabled) activeCount++;
                lblStatusPill.Text = "\u25CF IN SYNC  \u2022  " + (activeCount + 1) + " Speakers Playing Simultaneously";
                lblStatusPill.ForeColor = Color.FromArgb(74, 222, 128);
                btnTogglePause.Text = "Pause Sync";
                btnTogglePause.BaseColor = Color.FromArgb(37, 99, 235);
            }
            btnTogglePause.Invalidate();
        }

        private void LoadSettingsAndStart() {
            engine = new AudioEngine();
            isPopulating = true;

            var savedEnabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var savedDelays = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var savedVolumes = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            var savedMutes = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            if (File.Exists(SettingsFile)) {
                try {
                    var lines = File.ReadAllLines(SettingsFile);
                    string currentSection = "";
                    string lastName = "";
                    foreach (var rawLine in lines) {
                        string line = rawLine.Trim();
                        if (line.StartsWith("[") && line.EndsWith("]")) {
                            currentSection = line.Substring(1, line.Length - 2);
                            lastName = "";
                        } else if (line.Contains("=")) {
                            var parts = line.Split(new[] { '=' }, 2);
                            string key = parts[0].Trim();
                            string val = parts[1].Trim();

                            if (currentSection == "General") {
                                if (key == "StartWithWindows") chkStartup.Checked = (val == "True");
                                if (key == "CloseToTray") chkCloseToTray.Checked = (val == "True");
                                if (key == "VoiceClarityMode") {
                                    int m;
                                    if (int.TryParse(val, out m) && m >= 0 && m <= 2) {
                                        engine.VoiceClarityMode = (ClarityPreset)m;
                                        cbClarity.SelectedIndex = (m == 1) ? 0 : (m == 2 ? 1 : 2);
                                    }
                                } else if (key == "VoiceClarity") {
                                    engine.VoiceClarityMode = (val == "False") ? ClarityPreset.Off : ClarityPreset.BalancedVocal;
                                    cbClarity.SelectedIndex = (val == "False") ? 2 : 0;
                                }
                                if (key == "PrimaryDeviceId") engine.SelectedPrimaryId = val;
                            } else if (currentSection.StartsWith("Device_")) {
                                string devId = currentSection.Substring("Device_".Length);
                                if (key == "Name") lastName = val;
                                if (key == "Enabled" && val == "True") {
                                    savedEnabled.Add(devId);
                                    if (!string.IsNullOrEmpty(lastName)) savedEnabled.Add(lastName);
                                }
                                if (key == "DelayMs") {
                                    int d;
                                    if (int.TryParse(val, out d)) {
                                        savedDelays[devId] = d;
                                        if (!string.IsNullOrEmpty(lastName)) savedDelays[lastName] = d;
                                    }
                                }
                                if (key == "Volume") {
                                    float v;
                                    if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) {
                                        savedVolumes[devId] = v;
                                        if (!string.IsNullOrEmpty(lastName)) savedVolumes[lastName] = v;
                                    }
                                }
                                if (key == "Muted") {
                                    bool m;
                                    if (bool.TryParse(val, out m)) {
                                        savedMutes[devId] = m;
                                        if (!string.IsNullOrEmpty(lastName)) savedMutes[lastName] = m;
                                    }
                                }
                            }
                        }
                    }
                } catch { }
            }

            isPopulating = false;
            PopulateDevices(savedEnabled, savedDelays, savedVolumes, savedMutes);
            engine.Start();
            UpdateStatusUi();
            SaveSettings();
        }

        private void PopulateDevices(
            HashSet<string> savedEnabled,
            Dictionary<string, int> savedDelays,
            Dictionary<string, float> savedVolumes,
            Dictionary<string, bool> savedMutes) {

            isPopulating = true;
            try {
                var devices = DeviceHelper.GetAllRenderDevices();
                string defId = DeviceHelper.GetDefaultRenderDeviceId();

                cbPrimary.Items.Clear();
                cbPrimary.Items.Add(new DeviceItem("", "Follow Windows Default Output (Recommended)"));
                int selectedIdx = 0;

                for (int i = 0; i < devices.Count; i++) {
                    cbPrimary.Items.Add(new DeviceItem(devices[i].Item1, devices[i].Item2));
                    if (!string.IsNullOrEmpty(engine.SelectedPrimaryId) && devices[i].Item1 == engine.SelectedPrimaryId) {
                        selectedIdx = i + 1;
                    }
                }
                cbPrimary.SelectedIndex = selectedIdx;

                string effectivePrimaryId = !string.IsNullOrEmpty(engine.SelectedPrimaryId) ? engine.SelectedPrimaryId : defId;

                speakersPanel.SuspendLayout();
                speakersPanel.Controls.Clear();
                cardTelemetryUpdaters.Clear();

                engine.LockExecute(() => {
                    foreach (var old in engine.Speakers) {
                        try { if (old.Client != null) old.Client.Stop(); } catch { }
                    }
                    engine.Speakers.Clear();

                    foreach (var dev in devices) {
                        string id = dev.Item1;
                        string name = dev.Item2;

                        if (id == effectivePrimaryId) continue;
                        // Skip low-bitrate Bluetooth Hands-Free telephony profile so stereo A2DP stays crystal-clear
                        if (name.IndexOf("Hands-Free", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                        bool isTargetSpeaker =
                            name.IndexOf("EDIFIER", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("SK100", StringComparison.OrdinalIgnoreCase) >= 0;

                        bool enableByDefault = savedEnabled.Contains(id) || savedEnabled.Contains(name) || isTargetSpeaker;

                        int delay = 0;
                        if (savedDelays.ContainsKey(id)) delay = savedDelays[id];
                        else if (savedDelays.ContainsKey(name)) delay = savedDelays[name];

                        float vol = 1.0f;
                        if (savedVolumes.ContainsKey(id)) vol = savedVolumes[id];
                        else if (savedVolumes.ContainsKey(name)) vol = savedVolumes[name];

                        bool muted = false;
                        if (savedMutes.ContainsKey(id)) muted = savedMutes[id];
                        else if (savedMutes.ContainsKey(name)) muted = savedMutes[name];

                        var spk = new SecondarySpeaker {
                            Id = id,
                            Name = name,
                            Enabled = enableByDefault,
                            DelayMs = delay,
                            Volume = vol,
                            IsMuted = muted
                        };
                        engine.Speakers.Add(spk);
                    }

                    // Universal first-run experience: if no saved speaker was matched yet, enable the first available speaker automatically
                    if (!engine.Speakers.Exists(s => s.Enabled) && engine.Speakers.Count > 0) {
                        engine.Speakers[0].Enabled = true;
                    }

                    // Sort enabled speakers to the top so active outputs are immediately visible
                    engine.Speakers.Sort((a, b) => {
                        if (a.Enabled != b.Enabled) return b.Enabled.CompareTo(a.Enabled);
                        return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                    });

                    foreach (var spk in engine.Speakers) {
                        speakersPanel.Controls.Add(CreateSpeakerCard(spk));
                    }
                });

                speakersPanel.ResumeLayout(true);
            } finally {
                isPopulating = false;
            }
        }

        private Control CreateSpeakerCard(SecondarySpeaker spk) {
            var card = new Panel {
                Width = 812,
                Height = 148,
                BackColor = Color.FromArgb(24, 27, 44),
                Margin = new Padding(0, 0, 0, 12),
                Padding = new Padding(18, 14, 18, 14)
            };

            card.Paint += (s, e) => {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Color borderCol = spk.Enabled
                    ? Color.FromArgb(115, 56, 189, 248)
                    : Color.FromArgb(45, 100, 116, 139);
                using (var path = BrandArt.RoundRect(1f, 1f, card.Width - 3f, card.Height - 3f, 14f))
                using (var pen = new Pen(borderCol, 1.4f)) {
                    g.DrawPath(pen, path);
                }
            };

            var toggle = new ToggleSwitch {
                Checked = spk.Enabled,
                Location = new Point(18, 15)
            };

            var lblName = new Label {
                Text = spk.Name,
                Font = new Font("Segoe UI Semibold", 11.5f, FontStyle.Bold),
                ForeColor = spk.Enabled ? Color.White : Color.FromArgb(148, 163, 184),
                AutoSize = true,
                Location = new Point(80, 14)
            };

            var lblTelemetry = new Label {
                Text = spk.Enabled ? "\u25CF DRIFT-LOCKED  \u2022  Buffer: 10.0 ms" : "\u25CB STANDBY",
                Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold),
                ForeColor = spk.Enabled ? Color.FromArgb(74, 222, 128) : Color.FromArgb(100, 116, 139),
                AutoSize = true,
                Location = new Point(565, 17)
            };

            cardTelemetryUpdaters.Add(() => {
                if (!spk.Enabled) {
                    if (lblTelemetry.Text != "\u25CB STANDBY") {
                        lblTelemetry.Text = "\u25CB STANDBY";
                        lblTelemetry.ForeColor = Color.FromArgb(100, 116, 139);
                    }
                } else if (spk.Initialized) {
                    string txt = string.Format(CultureInfo.InvariantCulture, "\u25CF PHASE-LOCKED  \u2022  Queue: {0:0.0} ms", spk.AvgBufferMs);
                    if (lblTelemetry.Text != txt) {
                        lblTelemetry.Text = txt;
                        lblTelemetry.ForeColor = Color.FromArgb(74, 222, 128);
                    }
                } else {
                    lblTelemetry.Text = "\u25CC CONNECTING DEVICE...";
                    lblTelemetry.ForeColor = Color.FromArgb(250, 204, 21);
                }
            });

            // Row 2: Timing / Delay Calibration
            var lblDelay = new Label {
                Text = "Timing Offset: " + spk.DelayMs + " ms",
                Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(125, 211, 252),
                Size = new Size(155, 22),
                Location = new Point(18, 56)
            };

            var slDelay = new ModernSlider {
                Minimum = 0,
                Maximum = 500,
                Value = spk.DelayMs,
                AccentColor = Color.FromArgb(56, 189, 248),
                Size = new Size(330, 26),
                Location = new Point(175, 53)
            };

            var btnM5 = new ModernButton { Text = "-5 ms", Size = new Size(54, 26), Location = new Point(518, 53), Font = new Font("Segoe UI", 8.2f) };
            var btnM1 = new ModernButton { Text = "-1 ms", Size = new Size(50, 26), Location = new Point(576, 53), Font = new Font("Segoe UI", 8.2f) };
            var btnP1 = new ModernButton { Text = "+1 ms", Size = new Size(50, 26), Location = new Point(630, 53), Font = new Font("Segoe UI", 8.2f) };
            var btnP5 = new ModernButton { Text = "+5 ms", Size = new Size(54, 26), Location = new Point(684, 53), Font = new Font("Segoe UI", 8.2f) };
            var btnZero = new ModernButton { Text = "0 ms", Size = new Size(50, 26), Location = new Point(742, 53), Font = new Font("Segoe UI", 8.2f), ForeColor = Color.FromArgb(148, 163, 184) };

            Action<int> setDelay = (d) => {
                d = Math.Max(0, Math.Min(500, d));
                spk.DelayMs = d;
                if (spk.SyncBuffer != null) spk.SyncBuffer.DelayMs = d;
                slDelay.Value = d;
                lblDelay.Text = "Timing Offset: " + d + " ms";
                SaveSettings();
            };

            slDelay.ValueChanged += (s, e) => setDelay(slDelay.Value);
            btnM5.Click += (s, e) => setDelay(spk.DelayMs - 5);
            btnM1.Click += (s, e) => setDelay(spk.DelayMs - 1);
            btnP1.Click += (s, e) => setDelay(spk.DelayMs + 1);
            btnP5.Click += (s, e) => setDelay(spk.DelayMs + 5);
            btnZero.Click += (s, e) => setDelay(0);

            // Row 3: Volume & Mute
            int initVolPct = Math.Max(0, Math.Min(150, (int)Math.Round(spk.Volume * 100f)));
            var lblVol = new Label {
                Text = "Speaker Level: " + initVolPct + "%",
                Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(166, 227, 161),
                Size = new Size(155, 22),
                Location = new Point(18, 98)
            };

            var slVol = new ModernSlider {
                Minimum = 0,
                Maximum = 150,
                Value = initVolPct,
                AccentColor = Color.FromArgb(74, 222, 128),
                Size = new Size(330, 26),
                Location = new Point(175, 95)
            };
            slVol.ValueChanged += (s, e) => {
                spk.Volume = slVol.Value / 100.0f;
                lblVol.Text = "Speaker Level: " + slVol.Value + "%";
                SaveSettings();
            };

            var btn100 = new ModernButton {
                Text = "100%",
                Size = new Size(54, 26),
                Location = new Point(518, 95),
                Font = new Font("Segoe UI", 8.2f)
            };
            btn100.Click += (s, e) => {
                slVol.Value = 100;
                spk.Volume = 1.0f;
                lblVol.Text = "Speaker Level: 100%";
                SaveSettings();
            };

            var btnMute = new ModernButton {
                Text = spk.IsMuted ? "Unmute Speaker" : "Mute Speaker",
                Size = new Size(106, 26),
                Location = new Point(576, 95),
                Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold),
                BaseColor = spk.IsMuted ? Color.FromArgb(185, 28, 28) : Color.FromArgb(40, 44, 68)
            };
            btnMute.Click += (s, e) => {
                spk.IsMuted = !spk.IsMuted;
                btnMute.Text = spk.IsMuted ? "Unmute Speaker" : "Mute Speaker";
                btnMute.BaseColor = spk.IsMuted ? Color.FromArgb(185, 28, 28) : Color.FromArgb(40, 44, 68);
                btnMute.Invalidate();
                SaveSettings();
            };

            Func<SpeakerChannelMode, string> getModeLabel = (m) => {
                if (m == SpeakerChannelMode.MonoSum) return "Ch: Mono Sum";
                if (m == SpeakerChannelMode.LeftOnly) return "Ch: Left Only";
                if (m == SpeakerChannelMode.RightOnly) return "Ch: Right Only";
                return "Ch: Stereo (L+R)";
            };

            var btnChannel = new ModernButton {
                Text = getModeLabel(spk.ChannelMode),
                Size = new Size(106, 26),
                Location = new Point(686, 95),
                Font = new Font("Segoe UI", 8.2f),
                ForeColor = Color.FromArgb(186, 230, 253)
            };
            btnChannel.Click += (s, e) => {
                int next = ((int)spk.ChannelMode + 1) % 4;
                spk.ChannelMode = (SpeakerChannelMode)next;
                btnChannel.Text = getModeLabel(spk.ChannelMode);
                btnChannel.Invalidate();
                SaveSettings();
            };

            toggle.CheckedChanged += (s, e) => {
                spk.Enabled = toggle.Checked;
                lblName.ForeColor = spk.Enabled ? Color.White : Color.FromArgb(148, 163, 184);
                card.Invalidate();
                UpdateStatusUi();
                SaveSettings();
            };

            card.Controls.Add(toggle);
            card.Controls.Add(lblName);
            card.Controls.Add(lblTelemetry);
            card.Controls.Add(lblDelay);
            card.Controls.Add(slDelay);
            card.Controls.Add(btnM5);
            card.Controls.Add(btnM1);
            card.Controls.Add(btnP1);
            card.Controls.Add(btnP5);
            card.Controls.Add(btnZero);
            card.Controls.Add(lblVol);
            card.Controls.Add(slVol);
            card.Controls.Add(btn100);
            card.Controls.Add(btnMute);
            card.Controls.Add(btnChannel);

            return card;
        }

        private void CbPrimary_SelectedIndexChanged(object sender, EventArgs e) {
            if (isPopulating) return;
            var item = cbPrimary.SelectedItem as DeviceItem;
            if (item != null && item.Id != engine.SelectedPrimaryId) {
                engine.SelectedPrimaryId = item.Id;
                RefreshDevices();
                SaveSettings();
            }
        }

        private void RefreshDevices() {
            var savedEnabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var savedDelays = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var savedVolumes = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            var savedMutes = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            foreach (var s in engine.Speakers) {
                if (s.Enabled) {
                    savedEnabled.Add(s.Id);
                    savedEnabled.Add(s.Name);
                }
                savedDelays[s.Id] = s.DelayMs;
                savedDelays[s.Name] = s.DelayMs;
                savedVolumes[s.Id] = s.Volume;
                savedVolumes[s.Name] = s.Volume;
                savedMutes[s.Id] = s.IsMuted;
                savedMutes[s.Name] = s.IsMuted;
            }

            PopulateDevices(savedEnabled, savedDelays, savedVolumes, savedMutes);
            UpdateStatusUi();
        }

        private void SaveSettings() {
            if (engine == null) return;
            try {
                if (!Directory.Exists(AppDir)) Directory.CreateDirectory(AppDir);

                using (var sw = new StreamWriter(SettingsFile, false)) {
                    sw.WriteLine("[General]");
                    sw.WriteLine("StartWithWindows=" + chkStartup.Checked);
                    sw.WriteLine("CloseToTray=" + chkCloseToTray.Checked);
                    sw.WriteLine("VoiceClarity=" + (engine.VoiceClarityMode != ClarityPreset.Off));
                    sw.WriteLine("VoiceClarityMode=" + (int)engine.VoiceClarityMode);
                    sw.WriteLine("PrimaryDeviceId=" + engine.SelectedPrimaryId);
                    sw.WriteLine();

                    foreach (var s in engine.Speakers) {
                        sw.WriteLine("[Device_" + s.Id + "]");
                        sw.WriteLine("Name=" + s.Name);
                        sw.WriteLine("Enabled=" + s.Enabled);
                        sw.WriteLine("DelayMs=" + s.DelayMs);
                        sw.WriteLine("Volume=" + s.Volume.ToString(CultureInfo.InvariantCulture));
                        sw.WriteLine("Muted=" + s.IsMuted);
                        sw.WriteLine();
                    }
                }
            } catch { }
        }

        private void ToggleStartup(bool enable) {
            try {
                string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                string shortcutPath = Path.Combine(startupFolder, "Dual Audio Synchronizer.lnk");
                string legacyPath = Path.Combine(startupFolder, "Dual Audio Output.lnk");
                if (File.Exists(legacyPath)) {
                    try { File.Delete(legacyPath); } catch { }
                }

                if (enable) {
                    Type t = Type.GetTypeFromProgID("WScript.Shell");
                    object shell = Activator.CreateInstance(t);
                    object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                    Type scType = sc.GetType();
                    scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { Application.ExecutablePath });
                    scType.InvokeMember("Arguments", BindingFlags.SetProperty, null, sc, new object[] { "--minimized" });
                    scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(Application.ExecutablePath) });
                    scType.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { "Dual Audio Synchronizer v2.0" });
                    string icoPath = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "app.ico");
                    if (File.Exists(icoPath)) {
                        scType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { icoPath + ",0" });
                    }
                    scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
                } else {
                    if (File.Exists(shortcutPath)) File.Delete(shortcutPath);
                }
            } catch { }
        }
    }

    class DeviceItem {
        public string Id;
        public string Name;
        public DeviceItem(string id, string name) {
            Id = id;
            Name = name;
        }
        public override string ToString() {
            return Name;
        }
    }

    static class Program {
        [STAThread]
        static void Main(string[] args) {
            bool startMinimized = false;
            string screenshotPath = null;
            if (args != null) {
                for (int i = 0; i < args.Length; i++) {
                    if (string.Equals(args[i], "--minimized", StringComparison.OrdinalIgnoreCase)) {
                        startMinimized = true;
                    } else if (string.Equals(args[i], "--screenshot", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) {
                        screenshotPath = args[i + 1];
                    }
                }
            }

            bool createdNew = true;
            Mutex singleMutex = null;
            EventWaitHandle showEvent = null;

            if (string.IsNullOrEmpty(screenshotPath)) {
                singleMutex = new Mutex(true, "Local\\DualAudioSynchronizer_SingleInstance_v2", out createdNew);
                showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\DualAudioSynchronizer_ShowWindow_v2");
                if (!createdNew) {
                    try { showEvent.Set(); } catch { }
                    return;
                }
            }

            try {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(startMinimized, screenshotPath, showEvent));
            } catch (Exception ex) {
                try {
                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DualAudio");
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, "crash.log"), ex.ToString());
                } catch { }
            } finally {
                if (singleMutex != null && createdNew) {
                    try { singleMutex.ReleaseMutex(); } catch { }
                }
            }
        }
    }
    #endregion
}
