using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;

namespace SoundBitsRecorder
{
    /// <summary>
    /// This class implements sound capture for a single audio device.
    /// A LevelChanged event is invoked as new sound data is available, even if recording is not active. This allows the UI to display a sound level indicator.
    /// While recording is activated, wave data is stored on an internal buffer, which can be read by calling the Read method.
    /// </summary>
    /// <remarks>
    /// This object also keeps track of the current status (device, volume, and mute status) of the recording device.
    /// </remarks>
    public class RecordingDeviceModel : IDisposable
    {
        /// <summary>
        /// Backing field for the <c cref="Volume">Volume</c> property
        /// </summary>
        private float _volume;

        /// <summary>
        /// NAudio's capture object for the selected audio device
        /// </summary>
        private IWaveIn _capture;

        /// <summary>
        /// NAudio's output object for the selected audio device
        /// </summary>
        /// <remarks>
        /// This is only used to play silence in the audio device if it is a loopback device
        /// because wave data is not generated for a loopback device unless it is playing something
        /// </remarks>
        private WasapiOut _output;

        /// <summary>
        /// Internal Wave buffer where the recorded bytes are stored until they are read
        /// </summary>
        private BufferedWaveProvider _buffer;

        /// <summary>
        /// Internal Wave buffer used if we need to resample the audio
        /// (i.e., if we are recording on a different sample rate than the audio device's native rate)
        /// </summary>
        private BufferedWaveProvider _resamplerBuffer;

        /// <summary>
        /// Media Foundation Resampleer used if we need to resample the audio
        /// (i.e., if we are recording on a different sample rate than the audio device's native rate)
        /// </summary>
        private MediaFoundationResampler _resampler;

        /// <summary>
        /// Whether this is a loopback device or not
        /// (i.e., if we are recording the sound that is being played on an output device)
        /// </summary>
        private bool _isLoopback;

        /// <summary>
        /// The Audio Device that is being used by this model
        /// </summary>
        public MMDevice Device { get; private set; }

        /// <summary>
        /// Whether recording is active or not
        /// </summary>
        public bool IsRecording { get; private set; }

        /// <summary>
        /// The current recording volume
        /// </summary>
        /// <remarks>
        /// This value is capped at zero in the bottom end and 2.0 at the top end
        /// </remarks>
        public float Volume
        {
            get => _volume;
            set => _volume = value > 2 ? 2.0f : value < 0 ? 0.0f : value;
        }

        /// <summary>
        /// The current mute state of the device
        /// </summary>
        /// <remarks>
        /// If the device is mutted, all audio levels are set to zero when recording or displaying the levels
        /// </remarks>
        public bool Mute { get; set; }

        /// <summary>
        /// The wave format being used for the recording
        /// </summary>
        public WaveFormat WaveFormat { get; private set; }

        /// <summary>
        /// The number of bytes currently stored in the internal buffer and available to be read
        /// </summary>
        public int BufferedBytes => _buffer != null ? _buffer.BufferedBytes : 0;

        /// <summary>
        /// If an error occurs while recording, the error message is stored in this property
        /// </summary>
        public string Error { get; private set; }

        /// <summary>
        /// This event is invoked every time new sound data is available, even if recording is not active.
        /// This allows the UI to display a sound level indicator.
        /// </summary>
        public event EventHandler<LevelMeterEventArgs> LevelChanged;

        /// <summary>
        /// Initializes an instance with the provided audio device and using its native wave format
        /// </summary>
        /// <param name="device">The audio device to use for recording</param>
        public RecordingDeviceModel(MMDevice device)
        {
            Initialize(device, null);
        }

        /// <summary>
        /// Initializes an instance with the provided audio device and wave format
        /// </summary>
        /// <param name="device">The audio device to use for recording</param>
        /// <param name="waveFormat">The wave format to use for recording. Currently, this is only used for input devices and ignored for loopback devices.</param>
        public RecordingDeviceModel(MMDevice device, WaveFormat waveFormat)
        {
            Initialize(device, waveFormat);
        }

        /// <summary>
        /// Initializes an instance with the provided audio device and wave format
        /// </summary>
        /// <param name="device">The audio device to use for recording</param>
        /// <param name="waveFormat">The wave format to use for recording. Currently, this is only used for input devices and ignored for loopback devices.</param>
        private void Initialize(MMDevice device, WaveFormat waveFormat)
        {
            // Initialize the internal properties
            Device = device ?? throw new ArgumentNullException("device");
            IsRecording = false;
            Volume = Device.AudioEndpointVolume.MasterVolumeLevelScalar;
            Mute = Device.AudioEndpointVolume.Mute;
            WaveFormat = waveFormat;
            _isLoopback = Device.DataFlow == DataFlow.Render;

            if (_isLoopback)
            {
                // Initialize the output device we will use to play silence on the loopback device
                _output = new WasapiOut(Device, AudioClientShareMode.Shared, true, 200);
                _output.Init(new SilenceProvider(_output.OutputWaveFormat));
                // Initialize the capture device
                _capture = new WasapiLoopbackCapture(Device);
                // Set the recording wave format to the device's native format
                WaveFormat = _capture.WaveFormat;
            }
            else
            {
                // Initialize the capture device
                _capture = new WasapiCapture(Device);
                if (WaveFormat != null)
                {
                    if (Device.AudioClient.IsFormatSupported(AudioClientShareMode.Shared, WaveFormat))
                    {
                        // Ask the capture device to record directly in the desired format
                        _capture.WaveFormat = WaveFormat;
                    }
                    else
                    {
                        // Unsupported Wave Format, so, we need to resample or convert channels
                        if (_capture.WaveFormat.SampleRate != WaveFormat.SampleRate)
                        {
                            _resamplerBuffer = new BufferedWaveProvider(WaveFormat)
                            {
                                ReadFully = false
                            };
                            _resampler = new MediaFoundationResampler(_resamplerBuffer, WaveFormat);
                        }
                    }
                }
                else
                {
                    // Set the recording wave format to the device's native format
                    WaveFormat = _capture.WaveFormat;
                }
            }
            // Initialize the buffer and start audio capture.
            // This will start invoking LevelChanged events immediatelly,
            // but the data will only be saved to the buffer once IsRecording is set to true.
            _buffer = new BufferedWaveProvider(WaveFormat);
            _capture.DataAvailable += OnDataAvailable;
            _output?.Play();
            _capture.StartRecording();
        }

        /// <summary>
        /// Starts recording and saving audio data to the internal buffer
        /// </summary>
        public void StartRecording()
        {
            Error = null;
            IsRecording = true;
        }

        /// <summary>
        /// Stops recording
        /// </summary>
        public void StopRecording()
        {
            IsRecording = false;
        }

        /// <summary>
        /// Reads the desired number of bytes from the internal buffer
        /// </summary>
        /// <param name="buffer">The destination buffer to copy the bytes to</param>
        /// <param name="offset">The starting position to read from the internal buffer</param>
        /// <param name="count">The max number of bytes to read</param>
        /// <returns>The actual number of bytes read</returns>
        public int Read(byte[] buffer, int offset, int count)
        {
            return _buffer != null ? _buffer.Read(buffer, offset, count) : 0;
        }

        /// <summary>
        /// Disposes all the audio devices and the resampler to free resources
        /// </summary>
        /// <remarks>
        /// Before disposing the audio devices, recording is stopped if it is currently active
        /// </remarks>
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

        /// <summary>
        /// Handler for DataAvailable events from the capture device
        /// (i.e., this event is invoked each time new audio data is captured)
        /// </summary>
        /// <remarks>
        /// This implementation always invokes a <c cref="LevelChanged">LevelChanged</c> event with the current audio level.
        /// If recording is active, it also adds the recorded bytes to the internal buffer.
        /// </remarks>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void OnDataAvailable(object sender, WaveInEventArgs args)
        {
            try
            {
                // Invoke the LevelChanged event
                float level = CalculateLevelMeter(args, _capture.WaveFormat);
                LevelChanged?.Invoke(sender, new LevelMeterEventArgs(level));

                // Save the recorded bytes if recording is active
                if (IsRecording)
                {
                    WaveInEventArgs updatedArgs;
                    
                    // Converts the number of channels from the input into the desired number of channels, if necessary
                    if (_capture.WaveFormat.Channels == 1 && WaveFormat.Channels == 2)
                    {
                        updatedArgs = ConvertMonoToStereo(args);
                    }
                    else if (_capture.WaveFormat.Channels == 2 && WaveFormat.Channels == 1)
                    {
                        updatedArgs = ConvertStereoToMono(args);
                    }
                    else
                    {
                        updatedArgs = args;
                    }

                    // Resample the audio if necessary, and add the recorded bytes to the internal buffer
                    if (_resampler != null)
                    {
                        _resamplerBuffer.AddSamples(updatedArgs.Buffer, 0, updatedArgs.BytesRecorded);
                        byte[] tempBuffer = new byte[WaveFormat.AverageBytesPerSecond + WaveFormat.BlockAlign];
                        int bytesWritten;
                        while ((bytesWritten = _resampler.Read(tempBuffer, 0, WaveFormat.AverageBytesPerSecond)) > 0)
                        {
                            _buffer.AddSamples(AdjustVolume(tempBuffer, bytesWritten), 0, bytesWritten);
                        }
                    }
                    else
                    {
                        _buffer.AddSamples(AdjustVolume(updatedArgs.Buffer, updatedArgs.BytesRecorded), 0, updatedArgs.BytesRecorded);
                    }
                }
            }
            catch (Exception e)
            {
                StopRecording();
                Console.WriteLine(e);
                Error = e.Message;
            }
        }

        /// <summary>
        /// Calculates the current audio level coming from the audio device
        /// </summary>
        /// <param name="args">The data captured by the audio device</param>
        /// <param name="waveFormat">The wave format of the data (note that this is the format being used by the audio device, not the final desired wave format)</param>
        /// <returns>The highest audio level in the sample, adjusted according to the current volume</returns>
        private float CalculateLevelMeter(WaveInEventArgs args, WaveFormat waveFormat)
        {
            // Just return zero if the device is mutted or the volume is zero
            if (Mute || Volume <= 0)
            {
                return 0.0f;
            }

            // Find the highest audio level in the captured bytes
            // If the bytes are being captured in 8 bps or 16 bps (integer), convert the value to float (32 bps)
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

            // Return the highest audio level in the sample adjusted according to the volume
            return max * Volume;
        }

        /// <summary>
        /// Converts a Mono (1 channel) audio sample to Stereo (2 channels) by just copying the Mono audio data to both channels
        /// </summary>
        /// <param name="args">The Mono audio data captured by the audio device</param>
        /// <returns>Stero audio data in the same format as the parameter</returns>
        private WaveInEventArgs ConvertMonoToStereo(WaveInEventArgs args)
        {
            byte[] outBuffer = new byte[args.BytesRecorded * 2];
            WaveBuffer waveBuffer = new WaveBuffer(args.Buffer);
            WaveBuffer outWaveBuffer = new WaveBuffer(outBuffer);
            int outIndex = 0;
            switch (WaveFormat.BitsPerSample)
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
                    throw new FormatException(Properties.Resources.UnsupportedSoundEncoding + $": {WaveFormat.BitsPerSample} " + Properties.Resources.BitsPerSample);
            }
            return new WaveInEventArgs(outBuffer, args.BytesRecorded * 2);
        }

        /// <summary>
        /// Converts a Stereo (2 channels) audio sample to Mono (1 channel) by just averaging the values from the 2 channels
        /// </summary>
        /// <param name="args">The Stereo audio data captured by the audio device</param>
        /// <returns>Mono audio data in the same format as the parameter</returns>
        private WaveInEventArgs ConvertStereoToMono(WaveInEventArgs args)
        {
            byte[] outBuffer = new byte[args.BytesRecorded / 2];
            WaveBuffer waveBuffer = new WaveBuffer(args.Buffer);
            WaveBuffer outWaveBuffer = new WaveBuffer(outBuffer);
            int outIndex = 0;
            switch (WaveFormat.BitsPerSample)
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
                    throw new FormatException(Properties.Resources.UnsupportedSoundEncoding + $": {WaveFormat.BitsPerSample} " + Properties.Resources.BitsPerSample);
            }
            return new WaveInEventArgs(outBuffer, args.BytesRecorded / 2);
        }

        /// <summary>
        /// Adjusts the volume of audio data in the buffer according to the current value of the <c cref="Volume">Volume</c> property
        /// </summary>
        /// <param name="buffer">Buffer with audio data</param>
        /// <param name="count">Number of bytes in the buffer</param>
        /// <returns>A buffer with the same audio data with the volume adjusted. This can be the same buffer object received as the parameter or a new one.</returns>
        private byte[] AdjustVolume(byte[] buffer, int count)
        {
            // If the volume is 1.0, no adjustment is necessary, so, we just return the same buffer
            if (Volume == 1.0f)
            {
                return buffer;
            }

            // If the volume is 0, then we just return a buffer with all zeroes
            byte[] outBuffer = new byte[count];
            if (Mute || Volume <= 0)
            {
                return outBuffer;
            }

            // Multiply each value in the buffer by the Volume
            WaveBuffer waveBuffer = new WaveBuffer(buffer);
            WaveBuffer outWaveBuffer = new WaveBuffer(outBuffer);
            switch (WaveFormat.BitsPerSample)
            {
                case 8:
                    for (int i = 0; i < count; i++)
                    {
                        outBuffer[i] += (byte) (buffer[i] * Volume);
                    }
                    break;
                case 16:
                    for (int i = 0; i < count / 2; i++)
                    {
                        outWaveBuffer.ShortBuffer[i] += (short) (waveBuffer.ShortBuffer[i] * Volume);
                    }
                    break;
                case 32:
                    for (int i = 0; i < count / 4; i++)
                    {
                        outWaveBuffer.FloatBuffer[i] += waveBuffer.FloatBuffer[i] * Volume;
                    }
                    break;
                default:
                    throw new FormatException(Properties.Resources.UnsupportedSoundEncoding + $": {WaveFormat.BitsPerSample} " + Properties.Resources.BitsPerSample);
            }

            // Return the buffer with the adjusted data
            return outBuffer;
        }
    }

    /// <summary>
    /// Data for <c>LevelChanged</c> events
    /// </summary>
    public class LevelMeterEventArgs
    {
        /// <summary>
        /// Initializes the data with the given level value
        /// </summary>
        /// <param name="level">The highest audio level for a sample (32-bit float format)</param>
        public LevelMeterEventArgs(float level)
        {
            Level = level;
        }

        /// <summary>
        /// The highest audio level for a sample (32-bit float format)
        /// </summary>
        public float Level { get; }
    }
}
