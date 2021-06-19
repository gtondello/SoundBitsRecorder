using System;
using System.Timers;
using System.Windows.Controls;

namespace SoundBitsRecorder
{
    /// <summary>
    /// Interaction logic for DeviceControl.xaml
    /// </summary>
    public partial class DeviceControl : UserControl
    {
        private RecordingDeviceModel _model;
        private float _level;
        private Timer _timer;

        public RecordingDeviceModel Model => _model;

        public DeviceControl(RecordingDeviceModel model)
        {
            _model = model;
            InitializeComponent();
            label.Content = _model.Device.FriendlyName;
            _model.LevelChanged += (sender, args) =>
            {
                /*
                Dispatcher.Invoke(() =>
                {
                    progressBar.Value = args.Level * 100;
                });
                */
                _level = args.Level;
            };

            _timer = new Timer(50);
            _timer.Elapsed += (s, e) =>
            {
                Dispatcher.Invoke(() =>
                {
                    progressBar.Value = 60 - calculateDb(_level);
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

        private float calculateDb(float level)
        {
            float dbVariation = (float) (20 * Math.Log10(1.0 / level));
            float db = 60 - dbVariation;
            if (db < 0) db = 0;
            if (db > 60) db = 60;
            return db;
        }
    }
}
