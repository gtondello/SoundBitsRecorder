using NAudio.CoreAudioApi;
using NAudio.Lame;
using NAudio.MediaFoundation;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Timers;

namespace SoundBitsRecorder
{
    public class SoundRecorder
    {
        private List<MMDevice> _captureDevices;
        private List<MMDevice> _renderDevices;
        private MMDevice _defaultCaptureDevice;
        private MMDevice _defaultRenderDevice;
        private List<RecordingDeviceModel> _recordingModels;
        private WaveFormat _format;
        private LameMP3FileWriter _writer;
        private Timer _timer;
        private DateTime _started;
        private bool _isRecording;
        private string _error;
        private readonly object _lockObject = new object();

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
                return _isRecording ? (TimeSpan?)(DateTime.Now - _started) : null;
            }
        }

        public SoundRecorder()
        {
            _isRecording = false;
            _captureDevices = new List<MMDevice>();
            _renderDevices = new List<MMDevice>();
            _recordingModels = new List<RecordingDeviceModel>();
            MMDeviceEnumerator enumerator = new MMDeviceEnumerator();
            foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active))
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
            MediaFoundationApi.Startup();
        }

        public void StartRecording(int? captureDeviceIndex, int? renderDeviceIndex, string outputDirectory)
        {
            if (_isRecording)
            {
                throw new ApplicationException(Properties.Resources.ErrorAlreadyStarted);
            }
            if (!renderDeviceIndex.HasValue && !captureDeviceIndex.HasValue)
            {
                throw new ArgumentException(Properties.Resources.ErrorNoDevices);
            }

            try
            {
                _recordingModels.Clear();
                if (renderDeviceIndex.HasValue)
                {
                    MMDevice renderDevice = renderDeviceIndex.Value == -1 ? _defaultRenderDevice : _renderDevices[renderDeviceIndex.Value];
                    RecordingDeviceModel recordingModel = new RecordingDeviceModel(renderDevice);
                    _recordingModels.Add(recordingModel);
                }
                if (captureDeviceIndex.HasValue)
                {
                    MMDevice captureDevice = captureDeviceIndex.Value == -1 ? _defaultCaptureDevice : _captureDevices[captureDeviceIndex.Value];
                    RecordingDeviceModel recordingModel = new RecordingDeviceModel(captureDevice, _recordingModels.Count > 0 ? _recordingModels[0].WaveFormat : null);
                    _recordingModels.Add(recordingModel);
                }
                _format = _recordingModels[0].WaveFormat;
                if (_format.BitsPerSample != 8 && _format.BitsPerSample != 16 && _format.BitsPerSample != 32)
                {
                    throw new FormatException(Properties.Resources.UnsupportedSoundEncoding + $": {_format.BitsPerSample} " + Properties.Resources.BitsPerSample);
                }
            }
            catch (Exception e)
            {
                StopRecording();
                _error = e.Message;
                throw e;
            }

            StartRecording(outputDirectory);
        }

        public void StartRecording(string outputDirectory)
        {
            if (_isRecording)
            {
                throw new ApplicationException(Properties.Resources.ErrorAlreadyStarted);
            }
            if (_recordingModels.Count == 0)
            {
                throw new ArgumentException(Properties.Resources.ErrorNoDevices);
            }

            try
            {
                string fileName = Path.Combine(outputDirectory, DateTime.Now.ToString("yyyyMMddHHmmss") + ".mp3");
                _writer = new LameMP3FileWriter(fileName, _format, 160);

                _timer = new Timer(100);
                _timer.Elapsed += Timer_Elapsed;
                _timer.AutoReset = true;
                _timer.Enabled = true;

                foreach(RecordingDeviceModel recordingModel in _recordingModels)
                {
                    recordingModel.StartRecording();
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

                foreach (RecordingDeviceModel recordingModel in _recordingModels)
                {
                    recordingModel.StopRecording();
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
                _error = null;
                _writer = null;
            }
        }

        public RecordingDeviceModel AddDevice(MMDevice device)
        {
            return AddDevice(device, null);
        }

        public RecordingDeviceModel AddDevice(MMDevice device, WaveFormat format)
        {
            if (_isRecording)
            {
                throw new ApplicationException(Properties.Resources.ErrorChangeWhileRecording);
            }
            RecordingDeviceModel recordingModel = new RecordingDeviceModel(device, format);
            _recordingModels.Add(recordingModel);
            if (_format == null)
            {
                _format = recordingModel.WaveFormat;
            }
            return recordingModel;
        }

        public void RemoveDevice(RecordingDeviceModel recordingModel)
        {
            if (_isRecording)
            {
                throw new ApplicationException(Properties.Resources.ErrorChangeWhileRecording);
            }
            try
            {
                recordingModel.StopRecording();
                recordingModel.Dispose();
            }
            finally
            {
                _recordingModels.Remove(recordingModel);
            }
        }

        public void RemoveAllDevices()
        {
            if (_isRecording)
            {
                throw new ApplicationException(Properties.Resources.ErrorChangeWhileRecording);
            }
            try
            {
                foreach (RecordingDeviceModel recordingModel in _recordingModels)
                {
                    recordingModel.StopRecording();
                    recordingModel.Dispose();
                }
            }
            finally
            {
                _recordingModels.Clear();
                _format = null;
            }
        }

        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (!_isRecording)
            {
                return;
            }
            try
            {
                int n = int.MaxValue;
                foreach (RecordingDeviceModel recordingModel in _recordingModels)
                {
                    if (recordingModel.Error != null)
                    {
                        throw new Exception(recordingModel.Error);
                    }
                    n = Math.Min(n, recordingModel.BufferedBytes);
                }
                if (n > 0)
                {
                    byte[] bytesRecorded = AddSamples(n);
                    lock (_lockObject)
                    {
                        _writer.Write(bytesRecorded, 0, n);
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
            var bytesRecorded = new byte[n];
            var bytesCaptured = new byte[n];
            var waveBufferRecorded = new WaveBuffer(bytesRecorded);
            var waveBufferCaptured = new WaveBuffer(bytesCaptured);

            foreach (RecordingDeviceModel recordingModel in _recordingModels)
            {
                recordingModel.Read(bytesCaptured, 0, n);
                switch (_format.BitsPerSample)
                {
                    case 8:
                        for (int i = 0; i < n; i++)
                        {
                            bytesRecorded[i] += bytesCaptured[i];
                        }
                        break;
                    case 16:
                        for (int i = 0; i < n / 2; i++)
                        {
                            waveBufferRecorded.ShortBuffer[i] += waveBufferCaptured.ShortBuffer[i];
                        }
                        break;
                    case 32:
                        for (int i = 0; i < n / 4; i++)
                        {
                            waveBufferRecorded.FloatBuffer[i] += waveBufferCaptured.FloatBuffer[i];
                        }
                        break;
                    default:
                        throw new FormatException(Properties.Resources.UnsupportedSoundEncoding + $": {_format.BitsPerSample} " + Properties.Resources.BitsPerSample);
                }
            }

            return bytesRecorded;
        }
    }
}
