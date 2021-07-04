using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;

namespace SoundBitsRecorder
{
    public class RecordingDeviceModel : IDisposable
    {
        private MMDevice _device;
        private bool _isRecording;
        private float _volume;
        private bool _mute;
        private IWaveIn _capture;
        private WasapiOut _output;
        private WaveFormat _waveFormat;
        private BufferedWaveProvider _buffer;
        private BufferedWaveProvider _resamplerBuffer;
        private MediaFoundationResampler _resampler;
        private bool _isLoopback;
        private string _error;

        public MMDevice Device => _device;
        public bool IsRecording => _isRecording;
        public float Volume
        {
            get { return _volume;  }
            set
            {
                _volume = value > 2 ? 2.0f : value < 0 ? 0.0f : value;
            }
        }
        public bool Mute
        {
            get { return _mute;  }
            set
            {
                _mute = value;
            }
        }
        public WaveFormat WaveFormat => _capture?.WaveFormat;
        public int BufferedBytes => _buffer != null ? _buffer.BufferedBytes : 0;
        public string Error => _error;

        public event EventHandler<LevelMeterEventArgs> LevelChanged;
        //public event EventHandler<WaveInEventArgs> DataAvailable;

        public RecordingDeviceModel(MMDevice device)
        {
            Initialize(device, null);
        }

        public RecordingDeviceModel(MMDevice device, WaveFormat waveFormat)
        {
            Initialize(device, waveFormat);
        }

        private void Initialize(MMDevice device, WaveFormat waveFormat)
        {
            _device = device ?? throw new ArgumentNullException("device");
            _waveFormat = waveFormat;
            _isRecording = false;
            _volume = _device.AudioEndpointVolume.MasterVolumeLevelScalar;
            _mute = _device.AudioEndpointVolume.Mute;
            _isLoopback = _device.DataFlow == DataFlow.Render;
            if (_isLoopback)
            {
                _output = new WasapiOut(_device, AudioClientShareMode.Shared, true, 200);
                _output.Init(new SilenceProvider(_output.OutputWaveFormat));
                _capture = new WasapiLoopbackCapture(_device);
                _waveFormat = _capture.WaveFormat;
            }
            else
            {
                _capture = new WasapiCapture(_device);
                if (_waveFormat != null)
                {
                    if (_device.AudioClient.IsFormatSupported(AudioClientShareMode.Shared, _waveFormat))
                    {
                        _capture.WaveFormat = _waveFormat;
                    } 
                    else
                    {
                        // Unsupported Wave Format, so, we need to resample or convert channels
                        if (_capture.WaveFormat.SampleRate != _waveFormat.SampleRate)
                        {
                            _resamplerBuffer = new BufferedWaveProvider(_waveFormat)
                            {
                                ReadFully = false
                            };
                            _resampler = new MediaFoundationResampler(_resamplerBuffer, _waveFormat);
                        }
                    }
                }
                else
                {
                    _waveFormat = _capture.WaveFormat;
                }
            }
            _buffer = new BufferedWaveProvider(_waveFormat);
            _capture.DataAvailable += OnDataAvailable;
            _output?.Play();
            _capture.StartRecording();
        }

        public void StartRecording()
        {
            _error = null;
            _isRecording = true;
        }

        public void StopRecording()
        {
            _isRecording = false;
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            return _buffer != null ? _buffer.Read(buffer, offset, count) : 0;
        }

        public void Dispose()
        {
            StopRecording();
            _capture.StopRecording();
            _output?.Stop();
            _capture.DataAvailable -= OnDataAvailable;
            _capture.Dispose();
            _output?.Dispose();
            _resampler?.Dispose();
        }

        private void OnDataAvailable(object sender, WaveInEventArgs args)
        {
            try
            {
                float level = CalculateLevelMeter(args, _capture.WaveFormat);
                LevelChanged?.Invoke(sender, new LevelMeterEventArgs(level));
                if (_isRecording)
                {
                    WaveInEventArgs updatedArgs;
                    if (_capture.WaveFormat.Channels == 1 && _waveFormat.Channels == 2)
                    {
                        updatedArgs = ConvertMonoToStereo(args);
                    } else if (_capture.WaveFormat.Channels == 2 && _waveFormat.Channels == 1)
                    {
                        updatedArgs = ConvertStereoToMono(args);
                    } else
                    {
                        updatedArgs = args;
                    }
                    if (_resampler != null)
                    {
                        _resamplerBuffer.AddSamples(updatedArgs.Buffer, 0, updatedArgs.BytesRecorded);
                        byte[] tempBuffer = new byte[_waveFormat.AverageBytesPerSecond + _waveFormat.BlockAlign];
                        int bytesWritten;
                        while ((bytesWritten = _resampler.Read(tempBuffer, 0, _waveFormat.AverageBytesPerSecond)) > 0)
                        {
                            _buffer.AddSamples(AdjustVolume(tempBuffer, bytesWritten), 0, bytesWritten);
                        }
                    }
                    else
                    {
                        _buffer.AddSamples(AdjustVolume(updatedArgs.Buffer, updatedArgs.BytesRecorded), 0, updatedArgs.BytesRecorded);
                    }
                    //DataAvailable?.Invoke(sender, args);
                }
            }
            catch (Exception e)
            {
                StopRecording();
                Console.WriteLine(e);
                _error = e.Message;
            }
        }

        private float CalculateLevelMeter(WaveInEventArgs args, WaveFormat waveFormat)
        {
            if (_mute || _volume <= 0)
            {
                return 0.0f;
            }
            float max = 0.0f;
            WaveBuffer buffer = new WaveBuffer(args.Buffer);
            switch (waveFormat.BitsPerSample)
            {
                case 8:
                    for (int i = 0; i < args.BytesRecorded; i++)
                    {
                        byte sample = args.Buffer[i];
                        float sample32 = sample / 256f;
                        if (sample32 < 0) sample32 = -sample32;
                        if (sample32 > max) max = sample32;
                    }
                    break;
                case 16:
                    for (int i = 0; i < args.BytesRecorded / 2; i++)
                    {
                        short sample = buffer.ShortBuffer[i];
                        float sample32 = sample / 32768f;
                        if (sample32 < 0) sample32 = -sample32;
                        if (sample32 > max) max = sample32;
                    }
                    break;
                case 32:
                    for (int i = 0; i < args.BytesRecorded / 4; i++)
                    {
                        float sample = buffer.FloatBuffer[i];
                        if (sample < 0) sample = -sample;
                        if (sample > max) max = sample;
                    }
                    break;
                default:
                    break;
            }
            return max * _volume;
        }

        private WaveInEventArgs ConvertMonoToStereo(WaveInEventArgs args)
        {
            byte[] outBuffer = new byte[args.BytesRecorded * 2];
            WaveBuffer waveBuffer = new WaveBuffer(args.Buffer);
            WaveBuffer outWaveBuffer = new WaveBuffer(outBuffer);
            int outIndex = 0;
            switch (_waveFormat.BitsPerSample)
            {
                case 8:
                    for (int n = 0; n < args.BytesRecorded; n++)
                    {
                        outBuffer[outIndex++] = args.Buffer[n]; // left
                        outBuffer[outIndex++] = args.Buffer[n]; // right
                    }
                    break;
                case 16:
                    for (int n = 0; n < args.BytesRecorded / 2; n++)
                    {
                        outWaveBuffer.ShortBuffer[outIndex++] = waveBuffer.ShortBuffer[n]; // left
                        outWaveBuffer.ShortBuffer[outIndex++] = waveBuffer.ShortBuffer[n]; // right
                    }
                    break;
                case 32:
                    for (int n = 0; n < args.BytesRecorded / 4; n++)
                    {
                        outWaveBuffer.FloatBuffer[outIndex++] = waveBuffer.FloatBuffer[n]; // left
                        outWaveBuffer.FloatBuffer[outIndex++] = waveBuffer.FloatBuffer[n]; // right
                    }
                    break;
                default:
                    throw new FormatException(Properties.Resources.UnsupportedSoundEncoding + $": {_waveFormat.BitsPerSample} " + Properties.Resources.BitsPerSample);
            }
            return new WaveInEventArgs(outBuffer, args.BytesRecorded * 2);
        }

        private WaveInEventArgs ConvertStereoToMono(WaveInEventArgs args)
        {
            byte[] outBuffer = new byte[args.BytesRecorded / 2];
            WaveBuffer waveBuffer = new WaveBuffer(args.Buffer);
            WaveBuffer outWaveBuffer = new WaveBuffer(outBuffer);
            int outIndex = 0;
            switch (_waveFormat.BitsPerSample)
            {
                case 8:
                    for (int n = 0; n < args.BytesRecorded; n += 2)
                    {
                        byte left = args.Buffer[n];
                        byte right = args.Buffer[n + 1];
                        outBuffer[outIndex++] = (byte)((left / 2) + (right / 2));
                    }
                    break;
                case 16:
                    for (int n = 0; n < args.BytesRecorded / 2; n += 2)
                    {
                        short left = waveBuffer.ShortBuffer[n];
                        short right = waveBuffer.ShortBuffer[n + 1];
                        outWaveBuffer.ShortBuffer[outIndex++] = (short)((left / 2) + (right / 2));
                    }
                    break;
                case 32:
                    for (int n = 0; n < args.BytesRecorded / 4; n += 2)
                    {
                        float left = waveBuffer.FloatBuffer[n];
                        float right = waveBuffer.FloatBuffer[n + 1];
                        outWaveBuffer.FloatBuffer[outIndex++] = (left / 2.0f) + (right / 2.0f);
                    }
                    break;
                default:
                    throw new FormatException(Properties.Resources.UnsupportedSoundEncoding + $": {_waveFormat.BitsPerSample} " + Properties.Resources.BitsPerSample);
            }
            return new WaveInEventArgs(outBuffer, args.BytesRecorded / 2);
        }

        private byte[] AdjustVolume(byte[] buffer, int count)
        {
            if (_volume == 1.0f)
            {
                return buffer;
            }
            byte[] outBuffer = new byte[count];
            if (_mute || _volume <= 0)
            {
                return outBuffer;
            }
            WaveBuffer waveBuffer = new WaveBuffer(buffer);
            WaveBuffer outWaveBuffer = new WaveBuffer(outBuffer);
            switch (_waveFormat.BitsPerSample)
            {
                case 8:
                    for (int i = 0; i < count; i++)
                    {
                        outBuffer[i] += (byte) (buffer[i] * _volume);
                    }
                    break;
                case 16:
                    for (int i = 0; i < count / 2; i++)
                    {
                        outWaveBuffer.ShortBuffer[i] += (short) (waveBuffer.ShortBuffer[i] * _volume);
                    }
                    break;
                case 32:
                    for (int i = 0; i < count / 4; i++)
                    {
                        outWaveBuffer.FloatBuffer[i] += waveBuffer.FloatBuffer[i] * _volume;
                    }
                    break;
                default:
                    throw new FormatException(Properties.Resources.UnsupportedSoundEncoding + $": {_waveFormat.BitsPerSample} " + Properties.Resources.BitsPerSample);
            }
            return outBuffer;
        }
    }

    public class LevelMeterEventArgs
    {
        public LevelMeterEventArgs(float level)
        {
            Level = level;
        }

        public float Level { get; }
    }
}
