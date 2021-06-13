﻿using AutoUpdaterDotNET;
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
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        SoundRecorder _recorder;
        System.Timers.Timer _timer;
        bool _isRecording;
        bool _isStopping;

        public MainWindow()
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
            Mouse.OverrideCursor = null;
            AutoUpdater.Start("http://apps.gamefulbits.com/updates/SoundBitsRecorder.xml");
        }

        private void InitializeDevices()
        {
            checkBoxInput.IsChecked = Properties.Settings.Default.Basic_RecordInput;
            comboBoxInput.Items.Add(Properties.Resources.Default + " - " + _recorder.DefaultCaptureDevice.FriendlyName);
            comboBoxInput.SelectedIndex = 0;
            foreach (MMDevice device in _recorder.CaptureDevices)
            {
                comboBoxInput.Items.Add(device.FriendlyName);
                if (device.ID == Properties.Settings.Default.Basic_InputID)
                {
                    comboBoxInput.SelectedIndex = comboBoxInput.Items.Count - 1;
                }
            }
            checkBoxInput_Checked(checkBoxInput, null);

            checkBoxOutput.IsChecked = Properties.Settings.Default.Basic_RecordOutput;
            comboBoxOutput.Items.Add(Properties.Resources.Default + " - " + _recorder.DefaultRenderDevice.FriendlyName);
            comboBoxOutput.SelectedIndex = 0;
            foreach (MMDevice device in _recorder.RenderDevices)
            {
                comboBoxOutput.Items.Add(device.FriendlyName);
                if (device.ID == Properties.Settings.Default.Basic_OutputID)
                {
                    comboBoxOutput.SelectedIndex = comboBoxOutput.Items.Count - 1;
                }
            }
            checkBoxOutput_Checked(checkBoxOutput, null);
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

        private void MainWindow_Closing(object sender, CancelEventArgs e)
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
                Properties.Settings.Default.Basic_RecordInput = checkBoxInput.IsChecked.GetValueOrDefault(false);
                Properties.Settings.Default.Basic_RecordOutput = checkBoxOutput.IsChecked.GetValueOrDefault(false);
                Properties.Settings.Default.Basic_InputID = comboBoxInput.SelectedIndex == 0 ? "Default" : _recorder.CaptureDevices[comboBoxInput.SelectedIndex - 1].ID;
                Properties.Settings.Default.Basic_OutputID = comboBoxOutput.SelectedIndex == 0 ? "Default" : _recorder.RenderDevices[comboBoxOutput.SelectedIndex - 1].ID;
                Properties.Settings.Default.Save();
            }
        }

        private void checkBoxInput_Checked(object sender, RoutedEventArgs e)
        {
            if (comboBoxInput != null)
            {
                comboBoxInput.IsEnabled = checkBoxInput.IsChecked.GetValueOrDefault(false);
            }
            if (buttonRecord != null)
            {
                buttonRecord.IsEnabled = comboBoxInput.IsEnabled || comboBoxOutput.IsEnabled;
            }
            if (menuItemRecord != null)
            {
                menuItemRecord.IsEnabled = comboBoxInput.IsEnabled || comboBoxOutput.IsEnabled;
            }
        }

        private void checkBoxOutput_Checked(object sender, RoutedEventArgs e)
        {
            if (comboBoxOutput != null)
            {
                comboBoxOutput.IsEnabled = checkBoxOutput.IsChecked.GetValueOrDefault(false);
            }
            if (buttonRecord != null)
            {
                buttonRecord.IsEnabled = comboBoxInput.IsEnabled || comboBoxOutput.IsEnabled;
            }
            if (menuItemRecord != null)
            {
                menuItemRecord.IsEnabled = comboBoxInput.IsEnabled || comboBoxOutput.IsEnabled;
            }
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
            checkBoxInput.IsEnabled = false;
            comboBoxInput.IsEnabled = false;
            checkBoxOutput.IsEnabled = false;
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
                int? captureDeviceIndex = null;
                if (checkBoxInput.IsChecked.GetValueOrDefault(false))
                {
                    captureDeviceIndex = comboBoxInput.SelectedIndex - 1;
                }
                int? renderDeviceIndex = null;
                if (checkBoxOutput.IsChecked.GetValueOrDefault(false))
                {
                    renderDeviceIndex = comboBoxOutput.SelectedIndex - 1;
                }
                buttonRecord.Content = Properties.Resources.StopRecording;
                menuItemRecord.Header = Properties.Resources.MenuStopRecording;
                _isStopping = false;
                _isRecording = true;
                _recorder.StartRecording(captureDeviceIndex, renderDeviceIndex, directory);
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
                checkBoxInput.IsEnabled = true;
                comboBoxInput.IsEnabled = checkBoxInput.IsChecked.GetValueOrDefault(false);
                checkBoxOutput.IsEnabled = true;
                comboBoxOutput.IsEnabled = checkBoxOutput.IsChecked.GetValueOrDefault(false);
                textBoxFilename.IsEnabled = true;
                menuItemOutputFolder.IsEnabled = true;
                buttonRecord.IsEnabled = comboBoxInput.IsEnabled || comboBoxOutput.IsEnabled;
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
                    ShowErrorMessage(SoundBitsRecorder.Properties.Resources.ErrorRecording + "\n\n" + _recorder.Error + "\n\n" + SoundBitsRecorder.Properties.Resources.RecordingStopped);
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
            MessageBox.Show(message, SoundBitsRecorder.Properties.Resources.Error, MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show(SoundBitsRecorder.Properties.Resources.ChangeNextTime, SoundBitsRecorder.Properties.Resources.Information, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void menuItemPortugues_Click(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.Language = "pt";
            Properties.Settings.Default.Save();
            menuItemEnglish.IsChecked = false;
            menuItemPortugues.IsChecked = true;
            MessageBox.Show(SoundBitsRecorder.Properties.Resources.ChangeNextTime, SoundBitsRecorder.Properties.Resources.Information, MessageBoxButton.OK, MessageBoxImage.Information);
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