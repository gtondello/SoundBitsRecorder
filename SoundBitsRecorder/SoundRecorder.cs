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
    /// <summary>
    /// This class implements a Sound Recorder that merges audio data from two or more RecordingDeviceModels into a single audio stream.
    /// The recorded audio is saved to an MP3 file using the Lame MP3 encoder.
    /// </summary>
    public class SoundRecorder
    {
        /// <summary>
        /// List of RecordingDeviceModels that are being included in the recording
        /// </summary>
        private readonly List<RecordingDeviceModel> _recordingModels;

        /// <summary>
        /// The wave format being used for the recording
        /// </summary>
        private WaveFormat _format;

        /// <summary>
        /// Lame MP3 file writer used for MP3 enconding
        /// </summary>
        private LameMP3FileWriter _writer;

        /// <summary>
        /// Timer used to merge the audio from all the RecordingDeviceModels at a fixed interval
        /// </summary>
        private Timer _timer;

        /// <summary>
        /// Recording start time
        /// </summary>
        private DateTime _started;

        /// <summary>
        /// Lock object used to avoid concurrent writing to the MP3 file writer
        /// </summary>
        private readonly object _lockObject = new object();

        /// <summary>
        /// The list of all input audio devices available in the system
        /// </summary>
        public List<MMDevice> CaptureDevices { get; }

        /// <summary>
        ///  The list of all output (loopback) audio devices available in the system
        /// </summary>
        public List<MMDevice> RenderDevices { get; }

        /// <summary>
        /// The default input audio device of the system
        /// </summary>
        public MMDevice DefaultCaptureDevice { get; }

        /// <summary>
        /// The default output audio device of the system
        /// </summary>
        public MMDevice DefaultRenderDevice { get; }

        /// <summary>
        /// Whether recording is active or not
        /// </summary>
        public bool IsRecording { get; private set; }

        /// <summary>
        /// If an error occurs while recording, the error message is stored in this property
        /// </summary>
        public string Error { get; private set; }

        /// <summary>
        /// The amount of time elapsed since the recording started, or null if recording is not active
        /// </summary>
        public TimeSpan? RecordingTime => IsRecording ? (TimeSpan?)(DateTime.Now - _started) : null;

        /// <summary>
        /// Initializes the SoundRecorder
        /// </summary>
        /// <remarks>
        /// This implementation quries the system for the list of available audio devices available, as well as the default audio devices,
        /// and stores this information in the properties.
        /// </remarks>
        public SoundRecorder()
        {
            IsRecording = false;
            CaptureDevices = new List<MMDevice>();
            RenderDevices = new List<MMDevice>();
            _recordingModels = new List<RecordingDeviceModel>();

            // Adds all the audio devices available in the system to the CaptureDevices and RenderDevices lists
            MMDeviceEnumerator enumerator = new MMDeviceEnumerator();
            foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active))
            {
                try
                {
                    if (device.DataFlow == DataFlow.Capture)
                    {
                        CaptureDevices.Add(device);
                    } else
                    {
                        RenderDevices.Add(device);
                    }
                } catch { }
            }

            // Saves the system's default audio devices
            DefaultCaptureDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            DefaultRenderDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);

            // Initializes the MediaFoundationApi, which is used for resampling if needed
            MediaFoundationApi.Startup();
        }


        /// <summary>
        /// Starts recording using only the given capture device and loopback device
        /// </summary>
        /// <remarks>
        /// This implementation removes all the recording models, then add new ones for the given devices, and calls StartRecording(outputDirectory) to actually start recording
        /// </remarks>
        /// <param name="captureDeviceIndex">The index of the input audio device to use, from the CaptureDevices list</param>
        /// <param name="renderDeviceIndex">The index of the output (loopback) device to use, from the RenderDevices list</param>
        /// <param name="outputDirectory">The output directory to save the recorded MP3 file. The file will be named based on the current date and time.</param>
        /// <exception cref="ApplicationException">Throws ApplicationException if recording is already active</exception>
        /// <exception cref="ArgumentException">Throws ArgumentException if invalid device indexes are given</exception>
        public void StartRecording(int? captureDeviceIndex, int? renderDeviceIndex, string outputDirectory)
        {
            if (IsRecording)
            {
                throw new ApplicationException(Properties.Resources.ErrorAlreadyStarted);
            }
            if (!renderDeviceIndex.HasValue && !captureDeviceIndex.HasValue)
            {
                throw new ArgumentException(Properties.Resources.ErrorNoDevices);
            }

            try
            {
                // Clears the recording models list and add the selected devices to add
                _recordingModels.Clear();
                if (renderDeviceIndex.HasValue)
                {
                    MMDevice renderDevice = renderDeviceIndex.Value == -1 ? DefaultRenderDevice : RenderDevices[renderDeviceIndex.Value];
                    RecordingDeviceModel recordingModel = new RecordingDeviceModel(renderDevice);
                    _recordingModels.Add(recordingModel);
                }
                if (captureDeviceIndex.HasValue)
                {
                    MMDevice captureDevice = captureDeviceIndex.Value == -1 ? DefaultCaptureDevice : CaptureDevices[captureDeviceIndex.Value];
                    RecordingDeviceModel recordingModel = new RecordingDeviceModel(captureDevice, _recordingModels.Count > 0 ? _recordingModels[0].WaveFormat : null);
                    _recordingModels.Add(recordingModel);
                }

                // Currently, we are just using the wave format of the first recording device (the loopback device) for the output
                _format = _recordingModels[0].WaveFormat;
                if (_format.BitsPerSample != 8 && _format.BitsPerSample != 16 && _format.BitsPerSample != 32)
                {
                    throw new FormatException(Properties.Resources.UnsupportedSoundEncoding + $": {_format.BitsPerSample} " + Properties.Resources.BitsPerSample);
                }
            }
            catch (Exception e)
            {
                StopRecording();
                Error = e.Message;
                throw e;
            }

            // Start Recording
            StartRecording(outputDirectory);
        }

        /// <summary>
        /// Starts recording using the RecordingDeviceModels currently in the internal recording models list
        /// </summary>
        /// <param name="outputDirectory">The output directory to save the recorded MP3 file. The file will be named based on the current date and time.</param>
        /// <exception cref="ApplicationException">Throws ApplicationException if recording is already active</exception>
        /// <exception cref="ArgumentException">Throws ArgumentException if there are no recording models in the list</exception>
        public void StartRecording(string outputDirectory)
        {
            if (IsRecording)
            {
                throw new ApplicationException(Properties.Resources.ErrorAlreadyStarted);
            }
            if (_recordingModels.Count == 0)
            {
                throw new ArgumentException(Properties.Resources.ErrorNoDevices);
            }

            try
            {
                // Initialize the Lame MP3 file writer
                string fileName = Path.Combine(outputDirectory, DateTime.Now.ToString("yyyyMMddHHmmss") + ".mp3");
                _writer = new LameMP3FileWriter(fileName, _format, 160);

                // Initialize the timer, which will save any captured audio data each 100 milliseconds
                _timer = new Timer(100);
                _timer.Elapsed += Timer_Elapsed;
                _timer.AutoReset = true;
                _timer.Enabled = true;

                // Tell the RecordingDeviceModels to start recording
                foreach(RecordingDeviceModel recordingModel in _recordingModels)
                {
                    recordingModel.StartRecording();
                }
                _started = DateTime.Now;
                Error = null;
                IsRecording = true;
            }
            catch (Exception e)
            {
                StopRecording();
                Error = e.Message;
                throw e;
            }
        }

        /// <summary>
        /// Stops recording
        /// </summary>
        /// <remarks>
        /// After recording is stopped, the MP3 file is closed
        /// </remarks>
        public void StopRecording()
        {
            IsRecording = false;
            try
            {
                // Stop and dispose the timer
                if (_timer != null)
                {
                    _timer.Stop();
                    _timer.Dispose();
                }

                // Tell the RecordingDeviceMOdels to stop recording
                foreach (RecordingDeviceModel recordingModel in _recordingModels)
                {
                    recordingModel.StopRecording();
                }

                // Close the MP3 file writer
                if (_writer != null)
                {
                    _writer.Close();
                    _writer.Dispose();
                }
            }
            finally
            {
                _timer = null;
                Error = null;
                _writer = null;
            }
        }

        /// <summary>
        /// Add an audio device to be recorded, using the devices native wave format
        /// </summary>
        /// <param name="device">The audio device to record</param>
        /// <returns>A model to control recording from the device</returns>
        /// <exception cref="ApplicationException">Throws ApplicationException if recording is already active</exception>
        public RecordingDeviceModel AddDevice(MMDevice device)
        {
            return AddDevice(device, null);
        }

        /// <summary>
        /// Add an audio device to be recorded, using the specified wave format
        /// </summary>
        /// <param name="device">The audio device to record</param>
        /// <param name="format">The desired wave format</param>
        /// <returns>A model to control recording from the device</returns>
        /// <exception cref="ApplicationException">Throws ApplicationException if recording is already active</exception>
        public RecordingDeviceModel AddDevice(MMDevice device, WaveFormat format)
        {
            if (IsRecording)
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

        /// <summary>
        /// Removes an audio device from the list of devices being recorded
        /// </summary>
        /// <param name="recordingModel">A recording model previously added to this SoundRecorder, which is to be removed</param>
        /// <exception cref="ApplicationException">Throws ApplicationException if recording is already active</exception>
        public void RemoveDevice(RecordingDeviceModel recordingModel)
        {
            if (IsRecording)
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

        /// <summary>
        /// Removes all the audio devices from the list of devices being recorded
        /// </summary>
        /// <exception cref="ApplicationException">Throws ApplicationException if recording is already active</exception>
        public void RemoveAllDevices()
        {
            if (IsRecording)
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

        /// <summary>
        /// Handler for the Timer's Elapsed event.
        /// If recording is active and no error occurred, then the audio samples from all devices are merged (by calling <c cref="AddSamples(int)">AddSamples</c>)
        /// and the resulting data is added to the MP3 file.
        /// If an error occurred, then recording is aborted and the message is stored in the <c cref="Error">Error</c> property.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (!IsRecording)
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
                Error = ex.Message;
            }
        }

        /// <summary>
        /// Merges the audio data from all the active recording models into a single audio stream
        /// </summary>
        /// <param name="n">Number of bytes to read from each recording buffer</param>
        /// <returns>A new buffer with n bytes. Each byte is the sum of the corresponding bytes from all recording models.</returns>
        private byte[] AddSamples(int n)
        {
            byte[] bytesRecorded = new byte[n];
            byte[] bytesCaptured = new byte[n];
            WaveBuffer waveBufferRecorded = new WaveBuffer(bytesRecorded);
            WaveBuffer waveBufferCaptured = new WaveBuffer(bytesCaptured);

            foreach (RecordingDeviceModel recordingModel in _recordingModels)
            {
                _ = recordingModel.Read(bytesCaptured, 0, n);
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
