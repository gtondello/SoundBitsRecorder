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
    public partial class DeviceControl : UserControl
    {
        private static readonly BitmapImage _unmuteImage = new BitmapImage(new Uri(@"/SoundBitsRecorder;component/unmute.png", UriKind.Relative));
        private static readonly BitmapImage _muteImage = new BitmapImage(new Uri(@"/SoundBitsRecorder;component/mute.png", UriKind.Relative));

        private RecordingDeviceModel _model;
        private float _level;
        private bool _initialized;
        private Timer _timer;

        public RecordingDeviceModel Model => _model;

        public DeviceControl(RecordingDeviceModel model, float startingVolume = 1.0f, bool startingMuted = false)
        {
            _model = model;
            _model.Volume = startingVolume;
            _model.Mute = startingMuted;
            _initialized = false;
            InitializeComponent();
            label.Content = _model.Device.FriendlyName;
            volumeControl.Value = _model.Volume * 100.0;
            UpdateVolumeLabel();
            UpdateMuteImage();
            _initialized = true;
            _model.LevelChanged += (sender, args) =>
            {
                _level = args.Level;
            };

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

        public void Clear()
        {
            _timer?.Stop();
            _timer?.Dispose();
            _timer = null;
            _model = null;
        }

        private float CalculateDb(float level)
        {
            float dbVariation = (float) (20 * Math.Log10(1.0 / level));
            float db = 60 - dbVariation;
            if (db < 0) db = 0;
            if (db > 60) db = 60;
            return db;
        }

        private void volumeControl_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_initialized) return;
            _model.Volume = (float) (volumeControl.Value / 100.0);
            _model.Mute = false;
            UpdateVolumeLabel();
            UpdateMuteImage();
        }

        private void MuteButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            _model.Mute = !_model.Mute;
            UpdateVolumeLabel();
            UpdateMuteImage();
        }

        private void UpdateVolumeLabel()
        {
            if (volumeLabel != null)
            {
                int volume = (int)volumeControl.Value;
                volumeLabel.Text = volume + "%";
                volumeLabel.Foreground = new SolidColorBrush(volume > 100 ? Color.FromRgb(255, 0, 0) : Color.FromRgb(0, 0, 0));
                volumeLabel.TextDecorations = _model.Mute ? TextDecorations.Strikethrough : null;
            }
        }

        private void UpdateMuteImage()
        {
            if (muteButton != null)
            {
                muteButton.Source = _model.Mute ? _muteImage : _unmuteImage;
                ToolTip tooltip = new ToolTip
                {
                    Content = _model.Mute ? "Unmute" : "Mute"
                };
                ToolTipService.SetToolTip(muteButton, tooltip);
            }
        }
    }
}
