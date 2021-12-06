using System;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SoundBitsRecorder
{
    /// <summary>
    /// Interaction logic for DeviceControl.xaml
    /// </summary>
    /// <remarks>
    /// This is a UI control that displays the current status of a recording device (selected audio device, audio level, volume, mute status)
    /// and let's user modify it (change audio device, adjust the volume, mute/unmute the device).
    /// </remarks>
    public partial class DeviceControl : UserControl
    {
        /// <summary>
        /// Unmute icon
        /// </summary>
        private static readonly BitmapImage _unmuteImage = new BitmapImage(new Uri(@"/SoundBitsRecorder;component/unmute.png", UriKind.Relative));

        /// <summary>
        /// Mute icon
        /// </summary>
        private static readonly BitmapImage _muteImage = new BitmapImage(new Uri(@"/SoundBitsRecorder;component/mute.png", UriKind.Relative));

        /// <summary>
        /// Whether the UI controls are initialized
        /// </summary>
        private readonly bool _initialized;

        /// <summary>
        /// The latest audio level captured by the audio device, which is displayed in the UI
        /// </summary>
        private float _level;

        /// <summary>
        /// Timer used to update the level display periodically
        /// </summary>
        private Timer _timer;

        /// <summary>
        /// The <c cref="RecordingDeviceModel">RecordingDeviceModel</c> instance that is being controlled
        /// </summary>
        public RecordingDeviceModel Model { get; private set; }

        /// <summary>
        /// Initializes this control with the specified recording model and the initial volume and mute status
        /// </summary>
        /// <param name="model">The <c cref="RecordingDeviceModel">RecordingDeviceModel</c> instance that will be controlled</param>
        /// <param name="startingVolume">The starting volume of the recording model</param>
        /// <param name="startingMuted">The starting mute status of the recording model</param>
        public DeviceControl(RecordingDeviceModel model, float startingVolume = 1.0f, bool startingMuted = false)
        {
            Model = model;
            Model.Volume = startingVolume;
            Model.Mute = startingMuted;

            /// Initialize the UI components
            _initialized = false;
            InitializeComponent();
            label.Content = Model.Device.FriendlyName;
            volumeControl.Value = Model.Volume * 100.0;
            UpdateVolumeLabel();
            UpdateMuteImage();
            _initialized = true;

            // Add the LevelChanged event handler to the model, which will update the _level property
            Model.LevelChanged += (sender, args) =>
            {
                _level = args.Level;
            };

            // Initialize the timer, which will update the progress bar each 50 milliseconds with the current value of _level
            _timer = new Timer(50);
            _timer.Elapsed += (s, e) =>
            {
                Dispatcher.Invoke(() =>
                {
                    progressBar.Value = 60 - CalculateDb(_level);
                });
            };
            _timer.AutoReset = true;
            _timer.Enabled = true;
        }

        /// <summary>
        /// Stops and disposes the Timer (stops updating the UI with the audio levels)
        /// </summary>
        public void Clear()
        {
            _timer?.Stop();
            _timer?.Dispose();
            _timer = null;
            Model = null;
        }

        /// <summary>
        /// Calculates a decibel (db) value from a 32-bit float audio value
        /// </summary>
        /// <param name="level">An audio value in the 32-bit float format</param>
        /// <returns>The db value corresponding to the audio level. This value will be between 0 and 60</returns>
        private float CalculateDb(float level)
        {
            float dbVariation = (float) (20 * Math.Log10(1.0 / level));
            float db = 60 - dbVariation;
            if (db < 0) db = 0;
            if (db > 60) db = 60;
            return db;
        }

        /// <summary>
        /// Handler for the Volume control change
        /// </summary>
        /// <remarks>
        /// Updates the Volume in the Model, unmutes it, and updates the UI
        /// </remarks>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void volumeControl_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_initialized) return;
            Model.Volume = (float) (volumeControl.Value / 100.0);
            Model.Mute = false;
            UpdateVolumeLabel();
            UpdateMuteImage();
        }

        /// <summary>
        /// Handler for the Mute/Unmute control
        /// </summary>
        /// <remarks>
        /// Toggles the mute satus of the Model and updates the UI
        /// </remarks>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MuteButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            Model.Mute = !Model.Mute;
            UpdateVolumeLabel();
            UpdateMuteImage();
        }

        /// <summary>
        /// Helper method to update the text block that displays the volume
        /// </summary>
        /// <remarks>
        /// The current volume is displayed as a percentage. If the value is above 100%, then it is displayed in red.
        /// If the Model is mutted, then the volume value is displayed with a strikethrough decoration.
        /// </remarks>
        private void UpdateVolumeLabel()
        {
            if (volumeLabel != null)
            {
                int volume = (int)volumeControl.Value;
                volumeLabel.Text = volume + "%";
                volumeLabel.Foreground = new SolidColorBrush(volume > 100 ? Color.FromRgb(255, 0, 0) : Color.FromRgb(0, 0, 0));
                volumeLabel.TextDecorations = Model.Mute ? TextDecorations.Strikethrough : null;
            }
        }

        /// <summary>
        /// Helper method to updates the image and the tooltip of the Mute/Unmute button according to the model state
        /// </summary>
        private void UpdateMuteImage()
        {
            if (muteButton != null)
            {
                muteButton.Source = Model.Mute ? _muteImage : _unmuteImage;
                ToolTip tooltip = new ToolTip
                {
                    Content = Model.Mute ? "Unmute" : "Mute"
                };
                ToolTipService.SetToolTip(muteButton, tooltip);
            }
        }
    }
}
