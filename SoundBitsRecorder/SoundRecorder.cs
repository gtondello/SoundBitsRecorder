using NAudio.CoreAudioApi;
using NAudio.Lame;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Timers;

namespace SoundBitsRecorder
{
    class SoundRecorder
    {
        List<MMDevice> _captureDevices;
        List<MMDevice> _renderDevices;
        MMDevice _defaultCaptureDevice;
        MMDevice _defaultRenderDevice;
        WasapiOut _output;
        WasapiCapture _capture;
        WasapiLoopbackCapture _loopback;
        BufferedWaveProvider _bufferCapture;
        BufferedWaveProvider _bufferLoopback;
        WaveFormat _format;
        LameMP3FileWriter _writer;
        Timer _timer;
        DateTime _started;
        bool _isRecording;
        string _error;
        readonly object _lockObject = new object();

        public List<MMDevice> CaptureDevices => _captureDevices;
        public List<MMDevice> RenderDevices => _renderDevices;
        public MMDevice DefaultCaptureDevice => _defaultCaptureDevice;
        public MMDevice DefaultRenderDevice => _defaultRenderDevice;
        public bool IsRecording => _isRecording;
        public string Error => _error;
        public TimeSpan? RecordingTime
        {
            get
            {
                if (_isRecording)
                {
                    return DateTime.Now - _started;
                }
                else
                {
                    return null;
                }
            }
        }

        public SoundRecorder()
        {
            _isRecording = false;
            _captureDevices = new List<MMDevice>();
            _renderDevices = new List<MMDevice>();
            var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active))
            {
                try
                {
                    if (device.DataFlow == DataFlow.Capture)
                    {
                        _captureDevices.Add(device);
                    } else
                    {
                        _renderDevices.Add(device);
                    }
                } catch { }
            }
            _defaultCaptureDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            _defaultRenderDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
        }

        public void StartRecording(Nullable<int> captureDeviceIndex, Nullable<int> renderDeviceIndex, string outputDirectory)
        {
            if (_isRecording)
            {
                throw new ApplicationException(SoundBitsRecorder.Properties.Resources.ErrorAlreadyStarted);
            }
            if (!renderDeviceIndex.HasValue && !captureDeviceIndex.HasValue)
            {
                throw new ArgumentException(SoundBitsRecorder.Properties.Resources.ErrorNoDevices);
            }

            try
            {
                if (renderDeviceIndex.HasValue)
                {
                    var renderDevice = renderDeviceIndex.Value == -1 ? _defaultRenderDevice : _renderDevices[renderDeviceIndex.Value];
                    _output = new WasapiOut(renderDevice, AudioClientShareMode.Shared, true, 200);
                    _output.Init(new SilenceProvider(_output.OutputWaveFormat));
                    _loopback = new WasapiLoopbackCapture(renderDevice);
                    _bufferLoopback = CreateBuffer(_loopback);
                }
                if (captureDeviceIndex.HasValue)
                {
                    var captureDevice = captureDeviceIndex.Value == -1 ? _defaultCaptureDevice : _captureDevices[captureDeviceIndex.Value];
                    _capture = new WasapiCapture(captureDevice);
                    if (_loopback != null)
                    {
                        _capture.WaveFormat = _loopback.WaveFormat;
                    }
                    _bufferCapture = CreateBuffer(_capture);
                }
                _format = _loopback != null ? _loopback.WaveFormat : _capture.WaveFormat;
                if (_format.BitsPerSample != 8 && _format.BitsPerSample != 16 && _format.BitsPerSample != 32)
                {
                    throw new FormatException(SoundBitsRecorder.Properties.Resources.UnsupportedSoundEncoding + $": {_format.BitsPerSample} " + SoundBitsRecorder.Properties.Resources.BitsPerSample);
                }

                var fileName = Path.Combine(outputDirectory, DateTime.Now.ToString("yyyyMMddHHmmss") + ".mp3");
                _writer = new LameMP3FileWriter(fileName, _loopback != null ? _loopback.WaveFormat : _capture.WaveFormat, 160);

                _timer = new Timer(100);
                _timer.Elapsed += Timer_Elapsed;
                _timer.AutoReset = true;
                _timer.Enabled = true;

                if (_loopback != null)
                {
                    _output.Play();
                    _loopback.StartRecording();
                }
                if (_capture != null)
                {
                    _capture.StartRecording();
                }
                _started = DateTime.Now;
                _error = null;
                _isRecording = true;
            }
            catch (Exception e)
            {
                StopRecording();
                _error = e.Message;
                throw e;
            }
        }

        public void StopRecording()
        {
            _isRecording = false;
            try
            {
                if (_timer != null)
                {
                    _timer.Stop();
                    _timer.Dispose();
                }

                if (_capture != null)
                {
                    _capture.StopRecording();
                    _capture.Dispose();
                }
                if (_loopback != null)
                {
                    _loopback.StopRecording();
                    _output.Stop();
                    _loopback.Dispose();
                    _output.Dispose();
                }

                if (_writer != null)
                {
                    _writer.Close();
                    _writer.Dispose();
                }
            }
            finally
            {
                _timer = null;
                _loopback = null;
                _capture = null;
                _output = null;
                _bufferCapture = null;
                _bufferLoopback = null;
                _format = null;
                _error = null;
                _writer = null;
            }
        }

        private BufferedWaveProvider CreateBuffer(IWaveIn waveIn)
        {
            var buffer = new BufferedWaveProvider(waveIn.WaveFormat);
            waveIn.DataAvailable += (s, a) =>
            {
                try
                {
                    if (!_isRecording)
                    {
                        return;
                    }
                    lock (_lockObject)
                    {
                        buffer.AddSamples(a.Buffer, 0, a.BytesRecorded);
                    }
                }
                catch (Exception e)
                {
                    StopRecording();
                    _error = e.Message;
                }
            };
            return buffer;
        }

        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (!_isRecording)
            {
                return;
            }
            try
            {
                var n = Math.Min(
                    _bufferLoopback != null ? _bufferLoopback.BufferedBytes : Int32.MaxValue,
                    _bufferCapture != null ? _bufferCapture.BufferedBytes : Int32.MaxValue
                    );
                if (n > 0)
                {
                    byte[] bytesRecord = AddSamples(n);
                    lock (_lockObject)
                    {
                        _writer.Write(bytesRecord, 0, n);
                    }
                }
            }
            catch (Exception ex)
            {
                StopRecording();
                _error = ex.Message;
            }
        }

        private byte[] AddSamples(int n)
        {
            var bytesRecord = new byte[n];
            var waveBufferRecord = new WaveBuffer(bytesRecord);

            byte[] bytesLoopback = null;
            WaveBuffer waveBufferLoopback = null;
            byte[] bytesCapture = null;
            WaveBuffer waveBufferCapture = null;

            if (_bufferLoopback != null)
            {
                bytesLoopback = new byte[n];
                waveBufferLoopback = new WaveBuffer(bytesLoopback);
                lock (_lockObject)
                {
                    _bufferLoopback.Read(bytesLoopback, 0, n);
                }
            }
            if (_bufferCapture != null)
            {
                bytesCapture = new byte[n];
                waveBufferCapture = new WaveBuffer(bytesCapture);
                lock (_lockObject)
                {
                    _bufferCapture.Read(bytesCapture, 0, n);
                }
            }

            switch (_format.BitsPerSample)
            {
                case 8:
                    for (var i = 0; i < n; i++)
                    {
                        bytesRecord[i] = (byte)
                            ((bytesLoopback != null ? bytesLoopback[i] : 0) +
                            (bytesCapture != null ? bytesCapture[i] : 0));
                    }
                    break;
                case 16:
                    for (var i = 0; i < n / 2; i++)
                    {
                        waveBufferRecord.ShortBuffer[i] = (short)
                            ((waveBufferLoopback != null ? waveBufferLoopback.ShortBuffer[i] : 0) +
                            (waveBufferCapture != null ? waveBufferCapture.ShortBuffer[i] : 0));
                    }
                    break;
                case 32:
                    for (var i = 0; i < n / 4; i++)
                    {
                        waveBufferRecord.FloatBuffer[i] =
                            (waveBufferLoopback != null ? waveBufferLoopback.FloatBuffer[i] : 0f) +
                            (waveBufferCapture != null ? waveBufferCapture.FloatBuffer[i] : 0f);
                    }
                    break;
                default:
                    throw new FormatException(SoundBitsRecorder.Properties.Resources.UnsupportedSoundEncoding + $": {_format.BitsPerSample} " + SoundBitsRecorder.Properties.Resources.BitsPerSample);
            }

            return bytesRecord;
        }
    }
}
