<div align="center">

<img src="assets/banner.png" alt="Dual Audio Synchronizer Hero Banner" width="100%" />

<br />

# Dual Audio Synchronizer `v2.0`
**Phase-Locked Multi-Speaker Engine & Hearing Clarity Studio for Windows**

[![Release](https://img.shields.io/badge/Release-v2.0.0-38bdf8?style=for-the-badge&logo=github)](https://github.com/LIN4CRE/dual-audio-synchronizer/releases/latest)
[![Live Site](https://img.shields.io/badge/Live_Web_Portal-GitHub_Pages-4ade80?style=for-the-badge&logo=googlechrome&logoColor=white)](https://lin4cre.github.io/dual-audio-synchronizer/)
[![Platform](https://img.shields.io/badge/Platform-Windows_10_%2F_11_(x64)-6366f1?style=for-the-badge&logo=windows)](https://github.com/LIN4CRE/dual-audio-synchronizer)
[![Engine](https://img.shields.io/badge/Audio_Engine-WASAPI_Event_Loopback-facc15?style=for-the-badge)](https://github.com/LIN4CRE/dual-audio-synchronizer)
[![License: MIT](https://img.shields.io/badge/License-MIT-94a3b8?style=for-the-badge)](LICENSE)

<p align="center">
  <a href="https://lin4cre.github.io/dual-audio-synchronizer/"><strong>🌐 Live Deployment & Web Portal</strong></a> •
  <a href="https://github.com/LIN4CRE/dual-audio-synchronizer/releases/latest"><strong>⬇️ Download DualAudioStudio.exe</strong></a> •
  <a href="#-key-engineering-highlights"><strong>⚡ Architecture</strong></a> •
  <a href="#-hearing-clarity--dialogue-enhancement-dsp"><strong>🎧 Accessibility DSP</strong></a>
</p>

</div>

---

## 🎯 Overview

**Dual Audio Synchronizer** is a zero-dependency, studio-grade Windows audio mirroring engine that broadcasts your primary system output simultaneously to one or more secondary speakers (Bluetooth headphones/speakers, studio monitors, optical SPDIF, or HDMI/DisplayPort audio) with **automatic $\pm 1\text{ ms}$ clock-drift correction** and built-in **Hearing Clarity DSP**.

Unlike standard stereo-mix workarounds or passive loopback copiers—which slowly drift out of phase over time due to independent crystal oscillators on Bluetooth and USB DACs—**Dual Audio Synchronizer v2.0** continuously monitors the hardware render queue depth of every secondary device and applies click-free, linear-interpolated micro-rate adjustments to keep all speakers locked in phase indefinitely.

<div align="center">
  <img src="assets/screenshot.png" alt="Dual Audio Synchronizer v2.0 Studio Control Panel" width="880" />
  <p><em>Dual Audio Synchronizer v2.0 Studio Control Panel — Phase-locked mirroring between SK100 & EDIFIER R1280DBs with Balanced Vocal Clarity DSP active.</em></p>
</div>

---

## ✨ Key Engineering Highlights

| Capability | Technical Implementation | Benefit |
| :--- | :--- | :--- |
| **Active Clock-Drift Lock** | Continuous exponential moving average (EMA) tracking of WASAPI `GetCurrentPadding()` pinned to a `10.0 ms` target window with $\pm 1\text{ frame}$ linear-interpolated resampling (`ElasticSyncBuffer`). | Eliminates the "phasing / hollow echo" and periodic desynchronization that happens when two Bluetooth speakers run on independent hardware clocks. |
| **1 ms Kernel Timer & Event Loopback** | Calls `winmm.dll` `timeBeginPeriod(1)` on a `ThreadPriority.Highest` real-time worker paired with `AUDCLNT_STREAMFLAGS_EVENTCALLBACK`. | Removes Windows' default `15.6 ms` thread sleep quantum jitter for rock-solid sub-millisecond packet delivery. |
| **Hearing Clarity Studio DSP** | Cascaded 2nd-order Biquad Peaking EQ centered at $2.8\text{ kHz}$ ($Q = 1.15$, $+3.5\text{ dB}$ or $+5.5\text{ dB}$) + Stereo-to-Center Mono Vocal Blend + Soft-Knee $\tanh$ Limiter. | Specifically tuned for hard-of-hearing listeners: sharpens speech consonants (*s, t, k, th*) and anchors vocals across both speakers without digital clipping. |
| **Smart Bluetooth GUID Persistence** | Matches audio endpoints by **both** Windows MMDevice GUID and Friendly Name, while automatically filtering out low-bitrate `Hands-Free AG` telephony profiles. | Survives Bluetooth power-cycling and Windows endpoint GUID rotation automatically without losing your speaker configuration. |
| **Single-Instance & System Tray Guard** | Named OS `Mutex` (`Local\DualAudioSynchronizer_SingleInstance_v2`) + IPC `EventWaitHandle` wakeup signal. | Prevents duplicate background processes from competing for the same audio buffer and wakes the existing Studio window when launched from the Desktop shortcut. |

---

## 🔧 Signal Flow & Phase-Lock Architecture

```
┌────────────────────────────────────────────────────────────────────────────┐
│                   PRIMARY WINDOWS AUDIO ENDPOINT (WASAPI)                  │
│          e.g., Headphones (SK100) / Default System Playback Device         │
└─────────────────────────────────────┬──────────────────────────────────────┘
                                      │
                   Event-Driven Loopback Capture (32-bit Float)
                   AUDCLNT_STREAMFLAGS_LOOPBACK | EVENTCALLBACK
                                      │
                                      ▼
┌────────────────────────────────────────────────────────────────────────────┐
│                     ELASTIC SYNC RING BUFFER (Per Speaker)                 │
│   • Configurable Delay Calibration Offset (0 ms – 500 ms in 1 ms steps)    │
│   • Real-Time Hardware Padding Telemetry (EMA Queue Target: 10.0 ms)       │
│   • Click-Free Linear-Interpolated ±1 Frame Clock-Drift Rate Controller    │
└─────────────────────────────────────┬──────────────────────────────────────┘
                                      │
                                      ▼
┌────────────────────────────────────────────────────────────────────────────┐
│                   HEARING CLARITY & DIALOGUE DSP PIPELINE                  │
│   1. Center Vocal Mono Blend (50% Balanced / 70% Extra Crisp)              │
│   2. 2.8 kHz Biquad Peaking EQ (+3.5 dB / +5.5 dB Consonant Articulation)  │
│   3. Per-Speaker Gain Stage (0% – 150%) + Soft-Knee Tanh Peak Limiter      │
└─────────────────────────────────────┬──────────────────────────────────────┘
                                      │
                                      ▼
┌────────────────────────────────────────────────────────────────────────────┐
│                 SECONDARY HARDWARE RENDER ENDPOINTS (WASAPI)               │
│          e.g., Headphones (EDIFIER R1280DBs), Studio Monitors, SPDIF       │
└────────────────────────────────────────────────────────────────────────────┘
```

---

## 🎧 Hearing Clarity & Dialogue Enhancement (DSP)

Designed with accessibility first, **Dual Audio Synchronizer v2.0** boots with **Balanced Vocal Clarity** pre-configured out of the box so hard-of-hearing users never need to fiddle with complex equalizers:

1. **Balanced Vocal Clarity (`+3.5 dB @ 2.8 kHz + 50% Mono Blend`) *(Default / Recommended)***
   - Boosts the critical $2\text{ kHz} - 4\text{ kHz}$ human speech intelligibility band where consonant definition lives, while blending left/right stereo channels by $50\%$ so dialogue remains anchored evenly across both speakers regardless of where you sit in the room.
2. **Extra Crisp Speech (`+5.5 dB @ 2.8 kHz + 70% Strong Center Focus`)**
   - Maximum vocal articulation for quiet dialogue tracks, podcasts, lectures, and television streams, protected by a transparent soft-knee $\tanh$ limiter above `-0.17 dBFS`.
3. **Flat Studio Pass-Through (`No EQ / Original Stereo`)**
   - Bit-transparent stereo pass-through with zero frequency shaping.

---

## 🚀 Quick Start

### Option 1: Download Prebuilt Binary (Recommended)
1. Download **[`DualAudioStudio.exe`](https://github.com/LIN4CRE/dual-audio-synchronizer/releases/latest)** from the latest GitHub Release (or directly from [`build/DualAudioStudio.exe`](build/DualAudioStudio.exe)).
2. Run `DualAudioStudio.exe`. It requires **zero external runtime installations** (uses native Windows `.NET Framework 4.x` + `WASAPI` COM interop built into every Windows 10/11 PC).
3. Your primary output is automatically detected, and secondary speakers can be toggled **ON/OFF**, volume-leveled (`0% – 150%`), or fine-tuned with $\pm 1\text{ ms}$ / $\pm 5\text{ ms}$ timing buttons.

### Option 2: Build from Source
No Visual Studio installation is required. Run the included PowerShell build script to regenerate all high-DPI vector icons/logos and compile `DualAudioStudio.exe` using the built-in Windows .NET compiler:

```powershell
git clone https://github.com/LIN4CRE/dual-audio-synchronizer.git
cd dual-audio-synchronizer
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

---

## 📁 Repository Structure

```text
dual-audio-synchronizer/
├── assets/
│   ├── app.ico              # Multi-resolution (16px–256px) 32-bit ARGB Windows icon
│   ├── banner.png           # 1280×640 GitHub repository & social hero banner
│   ├── logo.png             # 512×512 high-DPI transparent studio emblem
│   └── screenshot.png       # Live capture of the v2.0 Studio Control Panel
├── build/
│   └── DualAudioStudio.exe  # Compiled standalone Windows executable
├── docs/
│   ├── index.html           # Live public GitHub Pages web portal
│   └── assets/              # Web portal visual assets
├── src/
│   ├── DualAudioStudio.cs   # Complete WASAPI engine, DSP pipeline & dark-mode WinForms Studio UI
│   └── app.ico              # Embedded Win32 resource icon
├── tools/
│   └── GenerateAssets.cs    # Programmatic GDI+ vector generator for all icons, logos & banners
├── build.ps1                # One-step asset generator & compiler script
├── LICENSE                  # MIT License
└── README.md                # Project documentation
```

---

## 📄 License

Released under the [MIT License](LICENSE). Crafted by **[LIN4CRE](https://github.com/LIN4CRE)**.
