using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace DesktopMemo
{
    public class VoiceAudioRecorder : IDisposable
    {
        private const int CALLBACK_FUNCTION = 0x00030000;
        private const uint WAVE_MAPPER = 0xFFFFFFFF;
        private const int WIM_DATA = 0x3C0;
        private const ushort WAVE_FORMAT_PCM = 1;

        [StructLayout(LayoutKind.Sequential)]
        private struct WAVEFORMATEX
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public uint nSamplesPerSec;
            public uint nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WAVEHDR
        {
            public IntPtr lpData;
            public uint dwBufferLength;
            public uint dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags;
            public uint dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }

        private delegate void WaveInProc(IntPtr hwi, int uMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2);

        [DllImport("winmm.dll")]
        private static extern uint waveInGetNumDevs();

        [DllImport("winmm.dll")]
        private static extern int waveInOpen(out IntPtr phwi, uint uDeviceID, ref WAVEFORMATEX pwfx, WaveInProc dwCallback, IntPtr dwInstance, int fdwOpen);

        [DllImport("winmm.dll")]
        private static extern int waveInPrepareHeader(IntPtr hwi, IntPtr pwh, int cbwh);

        [DllImport("winmm.dll")]
        private static extern int waveInUnprepareHeader(IntPtr hwi, IntPtr pwh, int cbwh);

        [DllImport("winmm.dll")]
        private static extern int waveInAddBuffer(IntPtr hwi, IntPtr pwh, int cbwh);

        [DllImport("winmm.dll")]
        private static extern int waveInStart(IntPtr hwi);

        [DllImport("winmm.dll")]
        private static extern int waveInStop(IntPtr hwi);

        [DllImport("winmm.dll")]
        private static extern int waveInReset(IntPtr hwi);

        [DllImport("winmm.dll")]
        private static extern int waveInClose(IntPtr hwi);

        private sealed class BufferSlot
        {
            public byte[] Data;
            public GCHandle DataHandle;
            public WAVEHDR Header;
            public GCHandle HeaderHandle;
        }

        private readonly object _sync = new object();
        private readonly List<BufferSlot> _slots = new List<BufferSlot>();
        private MemoryStream _pcmStream;
        private IntPtr _hWaveIn = IntPtr.Zero;
        private WaveInProc _callback;
        private volatile bool _recording = false;

        public event Action<double> LevelChanged;

        public bool IsRecording
        {
            get { return _recording; }
        }

        public static bool HasCaptureDevice()
        {
            try { return waveInGetNumDevs() > 0; }
            catch { return false; }
        }

        public bool Start(out string error)
        {
            error = "";
            if (!HasCaptureDevice())
            {
                error = "未检测到可用的麦克风输入设备，请先连接或启用麦克风。";
                return false;
            }

            StopInternal(false);

            lock (_sync)
            {
                _pcmStream = new MemoryStream();
            }

            var fmt = new WAVEFORMATEX
            {
                wFormatTag = WAVE_FORMAT_PCM,
                nChannels = 1,
                nSamplesPerSec = 16000,
                wBitsPerSample = 16,
                nBlockAlign = 2,
                nAvgBytesPerSec = 32000,
                cbSize = 0
            };

            _callback = new WaveInProc(OnWaveInMessage);
            int openRes = waveInOpen(out _hWaveIn, WAVE_MAPPER, ref fmt, _callback, IntPtr.Zero, CALLBACK_FUNCTION);
            if (openRes != 0 || _hWaveIn == IntPtr.Zero)
            {
                _hWaveIn = IntPtr.Zero;
                error = "打开麦克风失败（winmm 错误码：" + openRes + "）。";
                return false;
            }

            try
            {
                int hdrSize = Marshal.SizeOf(typeof(WAVEHDR));
                for (int i = 0; i < 6; i++)
                {
                    var slot = new BufferSlot();
                    slot.Data = new byte[1280]; // 40ms at 16kHz 16-bit mono (~25Hz waveform updates)
                    slot.DataHandle = GCHandle.Alloc(slot.Data, GCHandleType.Pinned);
                    slot.Header = new WAVEHDR
                    {
                        lpData = slot.DataHandle.AddrOfPinnedObject(),
                        dwBufferLength = (uint)slot.Data.Length,
                        dwUser = new IntPtr(i)
                    };
                    slot.HeaderHandle = GCHandle.Alloc(slot.Header, GCHandleType.Pinned);
                    IntPtr hdrPtr = slot.HeaderHandle.AddrOfPinnedObject();

                    int prepRes = waveInPrepareHeader(_hWaveIn, hdrPtr, hdrSize);
                    if (prepRes != 0) throw new InvalidOperationException("waveInPrepareHeader failed: " + prepRes);

                    int addRes = waveInAddBuffer(_hWaveIn, hdrPtr, hdrSize);
                    if (addRes != 0) throw new InvalidOperationException("waveInAddBuffer failed: " + addRes);

                    _slots.Add(slot);
                }

                _recording = true;
                int startRes = waveInStart(_hWaveIn);
                if (startRes != 0) throw new InvalidOperationException("waveInStart failed: " + startRes);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                StopInternal(false);
                return false;
            }
        }

        private void OnWaveInMessage(IntPtr hwi, int uMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2)
        {
            if (uMsg != WIM_DATA || dwParam1 == IntPtr.Zero) return;
            try
            {
                WAVEHDR hdr = (WAVEHDR)Marshal.PtrToStructure(dwParam1, typeof(WAVEHDR));
                int bytesRecorded = (int)hdr.dwBytesRecorded;
                if (bytesRecorded > 0 && hdr.lpData != IntPtr.Zero)
                {
                    byte[] chunk = new byte[bytesRecorded];
                    Marshal.Copy(hdr.lpData, chunk, 0, bytesRecorded);

                    lock (_sync)
                    {
                        if (_pcmStream != null && _recording)
                        {
                            // Max 115 seconds (~3.68 MB PCM, well under DeepSeek 8 MiB / 120s limit)
                            if (_pcmStream.Length + chunk.Length <= 3800000)
                            {
                                _pcmStream.Write(chunk, 0, chunk.Length);
                            }
                        }
                    }

                    double level = ComputeNormalizedLevel(chunk);
                    Action<double> handler = LevelChanged;
                    if (handler != null) handler(level);
                }

                if (_recording && _hWaveIn != IntPtr.Zero)
                {
                    waveInAddBuffer(_hWaveIn, dwParam1, Marshal.SizeOf(typeof(WAVEHDR)));
                }
            }
            catch { }
        }

        private static double ComputeNormalizedLevel(byte[] pcm16)
        {
            if (pcm16 == null || pcm16.Length < 2) return 0;
            double sumSq = 0;
            double peak = 0;
            int samples = pcm16.Length / 2;
            for (int i = 0; i + 1 < pcm16.Length; i += 2)
            {
                short s = (short)(pcm16[i] | (pcm16[i + 1] << 8));
                double norm = Math.Abs(s / 32768.0);
                sumSq += norm * norm;
                if (norm > peak) peak = norm;
            }
            double rms = Math.Sqrt(sumSq / Math.Max(1, samples));
            double effective = Math.Max(rms * 1.45, peak * 0.42);
            double gated = Math.Max(0.0, effective - 0.004);
            double boosted = Math.Min(1.0, Math.Pow(gated * 7.5, 0.58));
            return boosted;
        }

        public byte[] StopAndGetWavBytes()
        {
            return StopInternal(true);
        }

        public void Cancel()
        {
            StopInternal(false);
        }

        private byte[] StopInternal(bool buildWav)
        {
            _recording = false;
            IntPtr handle = _hWaveIn;
            _hWaveIn = IntPtr.Zero;

            if (handle != IntPtr.Zero)
            {
                try { waveInStop(handle); } catch { }
                try { waveInReset(handle); } catch { }
                Thread.Sleep(35);

                int hdrSize = Marshal.SizeOf(typeof(WAVEHDR));
                for (int i = 0; i < _slots.Count; i++)
                {
                    BufferSlot slot = _slots[i];
                    try
                    {
                        if (slot.HeaderHandle.IsAllocated)
                        {
                            waveInUnprepareHeader(handle, slot.HeaderHandle.AddrOfPinnedObject(), hdrSize);
                        }
                    }
                    catch { }
                }

                try { waveInClose(handle); } catch { }
            }

            for (int i = 0; i < _slots.Count; i++)
            {
                BufferSlot slot = _slots[i];
                try { if (slot.HeaderHandle.IsAllocated) slot.HeaderHandle.Free(); } catch { }
                try { if (slot.DataHandle.IsAllocated) slot.DataHandle.Free(); } catch { }
            }
            _slots.Clear();

            byte[] pcm = null;
            lock (_sync)
            {
                if (buildWav && _pcmStream != null && _pcmStream.Length > 0)
                {
                    pcm = _pcmStream.ToArray();
                }
                if (_pcmStream != null)
                {
                    _pcmStream.Dispose();
                    _pcmStream = null;
                }
            }

            if (pcm == null || pcm.Length == 0) return null;
            return BuildWavBytes(pcm, 16000, 16, 1);
        }

        public static byte[] BuildWavBytes(byte[] pcmData, int sampleRate, short bitsPerSample, short channels)
        {
            int dataLen = pcmData != null ? pcmData.Length : 0;
            short blockAlign = (short)(channels * (bitsPerSample / 8));
            int byteRate = sampleRate * blockAlign;

            using (var ms = new MemoryStream(44 + dataLen))
            using (var bw = new BinaryWriter(ms, Encoding.ASCII))
            {
                bw.Write(Encoding.ASCII.GetBytes("RIFF"));
                bw.Write(36 + dataLen);
                bw.Write(Encoding.ASCII.GetBytes("WAVE"));
                bw.Write(Encoding.ASCII.GetBytes("fmt "));
                bw.Write(16); // PCM fmt chunk size
                bw.Write((short)1); // PCM format
                bw.Write(channels);
                bw.Write(sampleRate);
                bw.Write(byteRate);
                bw.Write(blockAlign);
                bw.Write(bitsPerSample);
                bw.Write(Encoding.ASCII.GetBytes("data"));
                bw.Write(dataLen);
                if (dataLen > 0) bw.Write(pcmData, 0, dataLen);
                bw.Flush();
                return ms.ToArray();
            }
        }

        public void Dispose()
        {
            StopInternal(false);
        }
    }
}
