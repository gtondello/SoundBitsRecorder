using AutoUpdaterDotNET;
using Microsoft.WindowsAPICodePack.Dialogs;
using NAudio.CoreAudioApi;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Timers;
using System.Windows;
using System.Windows.Input;

namespace SoundBitsRecorder
{
    /// <summary>
    /// Interaction logic for RecordingWindow.xaml
    /// </summary>
    public partial class RecordingWindow : Window
    {
        SoundRecorder _recorder;
        System.Timers.Timer _timer;
        bool _isRecording;
        bool _isStopping;
        bool _initialized;

        public RecordingWindow()
        {
            System.Globalization.CultureInfo culture = new System.Globalization.CultureInfo(Properties.Settings.Default.Language);
            Thread.CurrentThread.CurrentUICulture = culture;
            Thread.CurrentThread.CurrentCulture = culture;
            InitializeComponent();
            Mouse.OverrideCursor = Cursors.Wait;
            menuItemEnglish.IsChecked = Properties.Settings.Default.Language == "en";
            menuItemPortugues.IsChecked = Properties.Settings.Default.Language == "pt";
            buttonRecord.Content = Properties.Resources.StartRecording;
            _isRecording = false;
            _recorder = new SoundRecorder();
            InitializeDevices();
            InitializeDirectory();
            buttonRecord.IsEnabled = true;
            menuItemRecord.IsEnabled = true;
            Mouse.OverrideCursor = null;
            AutoUpdater.Start("http://apps.gamefulbits.com/updates/SoundBitsRecorder.xml");
        }

        private void InitializeDevices()
        {
            _initialized = false;

            comboBoxInput.Items.Add(Properties.Resources.Default + " - " + _recorder.DefaultCaptureDevice.FriendlyName);
            comboBoxInput.SelectedIndex = 0;
            foreach (MMDevice device in _recorder.CaptureDevices)
            {
                comboBoxInput.Items.Add(device.FriendlyName);
                if (device.ID == Properties.Settings.Default.InputID)
                {
                    comboBoxInput.SelectedIndex = comboBoxInput.Items.Count - 1;
                }
            }

            comboBoxOutput.Items.Add(Properties.Resources.Default + " - " + _recorder.DefaultRenderDevice.FriendlyName);
            comboBoxOutput.SelectedIndex = 0;
            foreach (MMDevice device in _recorder.RenderDevices)
            {
                comboBoxOutput.Items.Add(device.FriendlyName);
                if (device.ID == Properties.Settings.Default.OutputID)
                {
                    comboBoxOutput.SelectedIndex = comboBoxOutput.Items.Count - 1;
                }
            }

            _initialized = true;
            comboBox_SelectionChanged(this, null);
        }

        private void InitializeDirectory()
        {
            string directory = Properties.Settings.Default.OutputFolder == "" ?
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SoundBits") :
                Properties.Settings.Default.OutputFolder;
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            textBoxFilename.Text = directory;
        }

        private void comboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!_initialized) return;

            float startInputVolume = Properties.Settings.Default.InputVolume;
            float startOutputVolume = Properties.Settings.Default.OutputVolume;
            bool startInputMute = Properties.Settings.Default.InputMute;
            bool startOutputMute = Properties.Settings.Default.OutputMute;
            foreach (DeviceControl control in panelInputDevice.Children)
            {
                startInputVolume = control.Model.Volume;
                startInputMute = control.Model.Mute;
            }
            foreach (DeviceControl control in panelOutputDevice.Children)
            {
                startOutputVolume = control.Model.Volume;
                startOutputMute = control.Model.Mute;
            }

            RemoveAllDevices();

            int renderDeviceIndex = comboBoxOutput.SelectedIndex - 1;
            int captureDeviceIndex = comboBoxInput.SelectedIndex - 1;
            RecordingDeviceModel renderModel = _recorder.AddDevice(renderDeviceIndex == -1 ? _recorder.DefaultRenderDevice : _recorder.RenderDevices[renderDeviceIndex]);
            RecordingDeviceModel captureModel = _recorder.AddDevice(captureDeviceIndex == -1 ? _recorder.DefaultCaptureDevice : _recorder.CaptureDevices[captureDeviceIndex], renderModel.WaveFormat);
            panelOutputDevice.Children.Add(new DeviceControl(renderModel, startOutputVolume, startOutputMute));
            panelInputDevice.Children.Add(new DeviceControl(captureModel, startInputVolume, startInputMute));
        }

        private void RemoveAllDevices()
        {
            foreach (DeviceControl control in panelInputDevice.Children)
            {
                control.Clear();
            }
            panelInputDevice.Children.Clear();
            foreach (DeviceControl control in panelOutputDevice.Children)
            {
                control.Clear();
            }
            panelOutputDevice.Children.Clear();
            _recorder.RemoveAllDevices();
        }

        private void RecordingWindow_Closing(object sender, CancelEventArgs e)
        {
            if (_isRecording)
            {
                MessageBoxResult result = MessageBox.Show(Properties.Resources.ConfirmInProgress, Properties.Resources.Confirmation, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                switch (result)
                {
                    case MessageBoxResult.Yes:
                        StopRecording();
                        break;
                    case MessageBoxResult.No:
                        e.Cancel = true;
                        break;
                }                
            }
            if (!e.Cancel)
            {
                Properties.Settings.Default.OutputFolder = textBoxFilename.Text.Trim();
                Properties.Settings.Default.InputID = comboBoxInput.SelectedIndex == 0 ? "Default" : _recorder.CaptureDevices[comboBoxInput.SelectedIndex - 1].ID;
                Properties.Settings.Default.OutputID = comboBoxOutput.SelectedIndex == 0 ? "Default" : _recorder.RenderDevices[comboBoxOutput.SelectedIndex - 1].ID;
                foreach (DeviceControl control in panelInputDevice.Children)
                {
                    Properties.Settings.Default.InputVolume = control.Model.Volume;
                    Properties.Settings.Default.InputMute = control.Model.Mute;
                }
                foreach (DeviceControl control in panelOutputDevice.Children)
                {
                    Properties.Settings.Default.OutputVolume = control.Model.Volume;
                    Properties.Settings.Default.OutputMute = control.Model.Mute;
                }
                Properties.Settings.Default.Save();
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            RemoveAllDevices();
        }

        private void buttonFilename_Click(object sender, RoutedEventArgs e)
        {
            CommonOpenFileDialog dialog = new CommonOpenFileDialog
            {
                InitialDirectory = textBoxFilename.Text.Trim(),
                IsFolderPicker = true
            };
            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                textBoxFilename.Text = dialog.FileName;
            }
        }

        private void buttonRecord_Click(object sender, RoutedEventArgs e)
        {
            if (_isRecording)
            {
                StopRecording();
            }
            else
            {
                StartRecording();
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("explorer.exe", textBoxFilename.Text);
        }

        private void StartRecording()
        {
            Mouse.OverrideCursor = Cursors.Wait;
            comboBoxInput.IsEnabled = false;
            comboBoxOutput.IsEnabled = false;
            textBoxFilename.IsEnabled = false;
            menuItemOutputFolder.IsEnabled = false;
            labelRecording.Content = Properties.Resources.Recording;
            labelRecording.Visibility = Visibility.Visible;
            labelTime.Content = "00:00";
            labelTime.Visibility = Visibility.Visible;

            try
            {
                string directory = textBoxFilename.Text.Trim();
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                buttonRecord.Content = Properties.Resources.StopRecording;
                menuItemRecord.Header = Properties.Resources.MenuStopRecording;
                _isStopping = false;
                _isRecording = true;
                _recorder.StartRecording(directory);
                _timer = new System.Timers.Timer(1000);
                _timer.Elapsed += Timer_Elapsed;
                _timer.AutoReset = true;
                _timer.Enabled = true;
                Mouse.OverrideCursor = null;
            }
            catch (Exception e)
            {
                StopRecording();
                ShowErrorMessage(Properties.Resources.ErrorStartRecording + "\n\n" + e.Message);
            }
        }

        private void StopRecording()
        {
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                if (_timer != null)
                {
                    _timer.Stop();
                    _timer.Dispose();
                    _timer = null;
                }
                labelRecording.Content = Properties.Resources.Saving;
                labelTime.Visibility = Visibility.Hidden;
                if (_recorder.IsRecording)
                {
                    _recorder.StopRecording();
                }
                _isRecording = false;
                _isStopping = false;
                labelRecording.Visibility = Visibility.Hidden;
                buttonRecord.Content = Properties.Resources.StartRecording;
                menuItemRecord.Header = Properties.Resources.MenuStartRecording;
                comboBoxInput.IsEnabled = true;
                comboBoxOutput.IsEnabled = true;
                menuItemOutputFolder.IsEnabled = true;
                buttonRecord.IsEnabled = true;
                menuItemRecord.IsEnabled = buttonRecord.IsEnabled;
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (_recorder.Error != null && !_isStopping)
            {
                Dispatcher.Invoke(() =>
                {
                    _isStopping = true;
                    StopRecording();
                    ShowErrorMessage(Properties.Resources.ErrorRecording + "\n\n" + _recorder.Error + "\n\n" + Properties.Resources.RecordingStopped);
                });
            }
            else if (_isRecording)
            {
                string s = "--:--";
                TimeSpan? recordingTime = _recorder.RecordingTime;
                if (recordingTime.HasValue)
                {
                    var t = recordingTime.Value;
                    s = (t.Days > 0 ? t.Days.ToString("00") + ":" : "")
                        + (t.Hours > 0 ? t.Hours.ToString("00") + ":" : "")
                        + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00");
                }
                Dispatcher.Invoke(() =>
                {
                    labelTime.Content = s;
                });
            }
        }

        private void ShowErrorMessage(string message)
        {
            MessageBox.Show(message, Properties.Resources.Error, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void menuItemEnglish_Click(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.Language = "en";
            Properties.Settings.Default.Save();
            menuItemEnglish.IsChecked = true;
            menuItemPortugues.IsChecked = false;
            MessageBox.Show(Properties.Resources.ChangeNextTime, Properties.Resources.Information, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void menuItemPortugues_Click(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.Language = "pt";
            Properties.Settings.Default.Save();
            menuItemEnglish.IsChecked = false;
            menuItemPortugues.IsChecked = true;
            MessageBox.Show(Properties.Resources.ChangeNextTime, Properties.Resources.Information, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            AboutWindow aboutWindow = new AboutWindow
            {
                Owner = this
            };
            aboutWindow.ShowDialog();
        }
    }
}
