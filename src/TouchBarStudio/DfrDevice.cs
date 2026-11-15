using System;
using System.Buffers.Binary;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using DFRDisplayUm.Interop;

namespace TouchBarStudio
{
    /// <summary>Handle to the DFR display driver. Sends whole frames with IOCTL_DFR_UPDATE_FRAMEBUFFER.</summary>
    public unsafe class DfrDevice : IDisposable
    {
        const int HeaderSize = 16;

        readonly object _lock = new object();
        readonly byte[] _buffer = new byte[HeaderSize + Renderer.W * Renderer.H * 3];
        IntPtr _handle = IntPtr.Zero;

        public bool IsOpen => _handle != IntPtr.Zero;

        public string Error { get; private set; } = "not opened yet";

        public bool Open()
        {
            lock (_lock)
            {
                if (IsOpen)
                {
                    return true;
                }

                try
                {
                    string path = DfrDeviceDiscovery.FindDfrDevice();
                    if (string.IsNullOrEmpty(path))
                    {
                        Error = "display device not found (is the driver installed and the Touch Bar enabled?)";
                        return false;
                    }

                    IntPtr handle = IoCtl.CreateFile(
                        path, FileAccess.Write, FileShare.None,
                        IntPtr.Zero, FileMode.Open, FileOptions.None, IntPtr.Zero);

                    if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                    {
                        Error = new Win32Exception(Marshal.GetLastWin32Error()).Message +
                                " (try \"Restart as administrator\")";
                        return false;
                    }

                    _handle = handle;
                    Error = "";
                    return true;
                }
                catch (Exception ex)
                {
                    Error = ex.Message;
                    return false;
                }
            }
        }

        public bool SendFrame(Bitmap bitmap)
        {
            lock (_lock)
            {
                if (!IsOpen)
                {
                    return false;
                }

                var data = bitmap.LockBits(
                    new Rectangle(0, 0, Renderer.W, Renderer.H),
                    ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

                try
                {
                    // Header: x, y, width, height (ushort), pixel format, vertical-flip flag (uint)
                    Array.Clear(_buffer, 0, HeaderSize);
                    BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(4), (ushort)Renderer.W);
                    BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(6), (ushort)Renderer.H);
                    BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(8), DfrHostIo.DFR_FRAMEBUFFER_FORMAT);

                    bool ok;
                    fixed (byte* dst0 = _buffer)
                    {
                        byte* src = (byte*)data.Scan0;
                        int stride = data.Stride;
                        byte* d = dst0 + HeaderSize;

                        // The driver wants the image column by column, each column from the bottom row up, as RGB.
                        for (int x = 0; x < Renderer.W; x++)
                        {
                            for (int y = Renderer.H - 1; y >= 0; y--)
                            {
                                byte* p = src + y * stride + x * 4; // B, G, R, A
                                *d++ = p[2];
                                *d++ = p[1];
                                *d++ = p[0];
                            }
                        }

                        ok = IoCtl.DeviceIoControl(
                            _handle, DfrHostIo.IOCTL_DFR_UPDATE_FRAMEBUFFER,
                            (IntPtr)dst0, _buffer.Length,
                            IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero);
                    }

                    if (!ok)
                    {
                        Error = "frame update failed: " + new Win32Exception(Marshal.GetLastWin32Error()).Message;
                    }

                    return ok;
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }
            }
        }

        public bool Clear()
        {
            lock (_lock)
            {
                return IsOpen && IoCtl.ClearDfrFrameBuffer(_handle);
            }
        }

        public void Close()
        {
            lock (_lock)
            {
                if (_handle != IntPtr.Zero)
                {
                    IoCtl.CloseHandle(_handle);
                    _handle = IntPtr.Zero;
                }
            }
        }

        public void Dispose() => Close();
    }
}
