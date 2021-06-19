using AutoUpdaterDotNET;
using Microsoft.WindowsAPICodePack.Dialogs;
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
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class AdvancedWindow : Window
    {
        SoundRecorder _recorder;
        System.Timers.Timer _timer;
        bool _isRecording;
        bool _isStopping;

        public AdvancedWindow()
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
            RecordingDeviceModel captureModel = _recorder.AddDevice(_recorder.DefaultCaptureDevice);
            RecordingDeviceModel renderModel = _recorder.AddDevice(_recorder.DefaultRenderDevice);
            panelDevices.Children.Add(new DeviceControl(captureModel));
            panelDevices.Children.Add(new DeviceControl(renderModel));
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

        private void AdvancedWindow_Closing(object sender, CancelEventArgs e)
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
                Properties.Settings.Default.Save();
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            foreach (DeviceControl control in panelDevices.Children)
            {
                control.Clear();
            }
            _recorder.RemoveAllDevices();
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
            /*
            checkBoxInput.IsEnabled = false;
            comboBoxInput.IsEnabled = false;
            checkBoxOutput.IsEnabled = false;
            comboBoxOutput.IsEnabled = false;
            */
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
                /*
                int? captureDeviceIndex = null;
                if (checkBoxInput.IsChecked.GetValueOrDefault(false))
                {
                    captureDeviceIndex = comboBoxInput.SelectedIndex - 1;
                }
                int? renderDeviceIndex = null;
                if (checkBoxOutput.IsChecked.GetValueOrDefault(false))
                {
                    renderDeviceIndex = comboBoxOutput.SelectedIndex - 1;
                }*/
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
                /*
                checkBoxInput.IsEnabled = true;
                comboBoxInput.IsEnabled = checkBoxInput.IsChecked.GetValueOrDefault(false);
                checkBoxOutput.IsEnabled = true;
                comboBoxOutput.IsEnabled = checkBoxOutput.IsChecked.GetValueOrDefault(false);
                textBoxFilename.IsEnabled = true;
                */
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

        private void menuItemBasic_Click(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.Main = "Basic";
            MainWindow mainWindow = new MainWindow();
            mainWindow.Show();
            Close();
        }

        private void menuItemAdvanced_Click(object sender, RoutedEventArgs e)
        {
            menuItemAdvanced.IsChecked = true;
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
