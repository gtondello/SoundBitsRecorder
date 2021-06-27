using System;
using System.Timers;
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
        private float _previousVolume;
        private bool _initialized;
        private Timer _timer;

        public RecordingDeviceModel Model => _model;

        public DeviceControl(RecordingDeviceModel model, float startingVolume)
        {
            _model = model;
            _model.Volume = startingVolume;
            _previousVolume = startingVolume;
            _initialized = false;
            InitializeComponent();
            _initialized = true;
            label.Content = _model.Device.FriendlyName;
            volumeControl.Value = _model.Volume * 100.0;
            muteButton.Source = volumeControl.Value == 0 ? _muteImage : _unmuteImage;
            UpdateVolumeLabel();
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
            if (volumeControl.Value == 0 && _model.Volume > 0)
            {
                _previousVolume = _model.Volume;
            }
            _model.Volume = (float) (volumeControl.Value / 100.0);
            UpdateVolumeLabel();
            if (muteButton != null)
            {
                muteButton.Source = volumeControl.Value == 0 ? _muteImage : _unmuteImage;
            }
        }

        private void MuteButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (_model.Volume == 0)
            {
                _model.Volume = _previousVolume > 0.0f ? _previousVolume : 0.01f;
                muteButton.Source = _unmuteImage;
            }
            else
            {
                _previousVolume = _model.Volume;
                _model.Volume = 0.0f;
                muteButton.Source = _muteImage;
            }
            volumeControl.Value = _model.Volume * 100.0;
            UpdateVolumeLabel();
        }

        private void UpdateVolumeLabel()
        {
            if (volumeLabel != null)
            {
                volumeLabel.Content = ((int)volumeControl.Value) + "%";
                volumeLabel.Foreground = new SolidColorBrush((int)volumeControl.Value > 100 ? Color.FromRgb(255, 0, 0) : Color.FromRgb(0, 0, 0));
            }
        }
    }
}
