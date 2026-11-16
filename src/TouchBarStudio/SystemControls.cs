using System;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace TouchBarStudio
{
    /// <summary>
    /// Cached values shown by slider / toggle tiles. Everything is -1 / false until a read succeeds,
    /// so a missing capability never throws into the UI.
    /// </summary>
    public static class SystemState
    {
        public static int VolumePct = -1;
        public static bool Muted;
        public static int BrightnessPct = -1;
        public static bool BrightnessSupported = true;
        public static int MicMuted = -1; // -1 unknown, 0 live, 1 muted

        /// <summary>While the user drags on the timeline: 0..1, otherwise -1.</summary>
        public static double ScrubFraction = -1;

        public static MediaService Media;

        /// <summary>Re-reads the cheap values (audio). Returns true when something changed.</summary>
        public static bool PollAudio()
        {
            int volume = SystemControls.GetVolumePct(out bool muted);
            int mic = SystemControls.GetMicMuted();
            bool changed = volume != VolumePct || muted != Muted || mic != MicMuted;
            VolumePct = volume;
            Muted = muted;
            MicMuted = mic;
            return changed;
        }
    }

    /// <summary>Volume (Core Audio), microphone mute and display brightness (WMI) helpers.</summary>
    public static class SystemControls
    {
        // ------------------------------------------------------------ Core Audio

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        class MMDeviceEnumeratorCom { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig]
            int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
                [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioEndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int GetChannelCount(out uint count);
            [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid context);
            [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
            [PreserveSig] int GetMasterVolumeLevel(out float level);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
            [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid context);
            [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
            [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
            [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }

        static readonly Guid IidEndpointVolume = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
        const int Render = 0;
        const int Capture = 1;
        const int Multimedia = 1;
        const int ClsctxAll = 23;

        /// <summary>Opens the endpoint volume of the default device. Caller releases the result.</summary>
        static IAudioEndpointVolume Open(int dataFlow)
        {
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                if (enumerator.GetDefaultAudioEndpoint(dataFlow, Multimedia, out device) != 0 || device == null)
                {
                    return null;
                }

                var iid = IidEndpointVolume;
                if (device.Activate(ref iid, ClsctxAll, IntPtr.Zero, out object obj) != 0)
                {
                    return null;
                }

                return (IAudioEndpointVolume)obj;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (device != null) Marshal.ReleaseComObject(device);
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
            }
        }

        /// <summary>System volume 0..100, or -1 when there is no audio device.</summary>
        public static int GetVolumePct(out bool muted)
        {
            muted = false;
            var volume = Open(Render);
            if (volume == null) return -1;
            try
            {
                if (volume.GetMasterVolumeLevelScalar(out float level) != 0) return -1;
                volume.GetMute(out muted);
                return (int)Math.Round(Math.Max(0f, Math.Min(1f, level)) * 100);
            }
            catch
            {
                return -1;
            }
            finally
            {
                Marshal.ReleaseComObject(volume);
            }
        }

        public static void SetVolume(double fraction)
        {
            var volume = Open(Render);
            if (volume == null) return;
            try
            {
                var context = Guid.Empty;
                float level = (float)Math.Max(0, Math.Min(1, fraction));
                volume.SetMasterVolumeLevelScalar(level, ref context);
                if (level > 0) volume.SetMute(false, ref context);
                SystemState.VolumePct = (int)Math.Round(level * 100);
                SystemState.Muted = false;
            }
            catch
            {
            }
            finally
            {
                Marshal.ReleaseComObject(volume);
            }
        }

        /// <summary>-1 unknown / no microphone, 0 live, 1 muted.</summary>
        public static int GetMicMuted()
        {
            var volume = Open(Capture);
            if (volume == null) return -1;
            try
            {
                return volume.GetMute(out bool muted) == 0 ? (muted ? 1 : 0) : -1;
            }
            catch
            {
                return -1;
            }
            finally
            {
                Marshal.ReleaseComObject(volume);
            }
        }

        public static void ToggleMic()
        {
            var volume = Open(Capture);
            if (volume == null) throw new InvalidOperationException("No microphone found");
            try
            {
                volume.GetMute(out bool muted);
                var context = Guid.Empty;
                volume.SetMute(!muted, ref context);
                SystemState.MicMuted = muted ? 0 : 1;
            }
            finally
            {
                Marshal.ReleaseComObject(volume);
            }
        }

        // ------------------------------------------------------------ brightness (WMI, internal display)

        static int _pendingBrightness = -1;
        static int _worker; // 1 while the writer task runs

        /// <summary>Reads the internal display brightness in the background. Sets BrightnessSupported.</summary>
        public static Task RefreshBrightnessAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    using (var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT CurrentBrightness FROM WmiMonitorBrightness"))
                    using (var results = searcher.Get())
                    {
                        foreach (ManagementBaseObject o in results)
                        {
                            SystemState.BrightnessPct = Convert.ToInt32(o["CurrentBrightness"]);
                            SystemState.BrightnessSupported = true;
                            o.Dispose();
                            return;
                        }
                    }

                    SystemState.BrightnessSupported = false;
                    SystemState.BrightnessPct = -1;
                }
                catch
                {
                    SystemState.BrightnessSupported = false;
                    SystemState.BrightnessPct = -1;
                }
            });
        }

        /// <summary>
        /// Requests a brightness level. WMI calls are slow, so the newest request wins:
        /// while one write is running, further calls only replace the pending value.
        /// </summary>
        public static void SetBrightness(int percent)
        {
            if (!SystemState.BrightnessSupported) return;

            percent = Math.Max(0, Math.Min(100, percent));
            SystemState.BrightnessPct = percent;
            Interlocked.Exchange(ref _pendingBrightness, percent);

            if (Interlocked.CompareExchange(ref _worker, 1, 0) != 0) return;

            Task.Run(() =>
            {
                try
                {
                    while (true)
                    {
                        int value = Interlocked.Exchange(ref _pendingBrightness, -1);
                        if (value < 0) break;
                        WriteBrightness(value);
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref _worker, 0);
                    if (Volatile.Read(ref _pendingBrightness) >= 0 && Interlocked.CompareExchange(ref _worker, 1, 0) == 0)
                    {
                        // A request arrived after the loop ended: run once more.
                        Task.Run(() =>
                        {
                            try
                            {
                                int v = Interlocked.Exchange(ref _pendingBrightness, -1);
                                if (v >= 0) WriteBrightness(v);
                            }
                            finally
                            {
                                Interlocked.Exchange(ref _worker, 0);
                            }
                        });
                    }
                }
            });
        }

        static void WriteBrightness(int percent)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorBrightnessMethods"))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject o in results)
                    {
                        o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)percent });
                        o.Dispose();
                    }
                }
            }
            catch
            {
                SystemState.BrightnessSupported = false;
            }
        }
    }
}
