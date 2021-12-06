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
    /// <remarks>
    /// This is the main application window
    /// </remarks>
    public partial class RecordingWindow : Window
    {
        /// <summary>
        /// The instance of the <c cref="SoundRecorder">SoundRecorder</c> object, which contains the recording logic
        /// </summary>
        private readonly SoundRecorder _recorder;

        /// <summary>
        /// A Timer that is used to update the recording status and time each second, while recording is active
        /// </summary>
        private System.Timers.Timer _timer;

        /// <summary>
        /// Whether recording is active or not
        /// </summary>
        private bool _isRecording;

        /// <summary>
        /// Whether recording is stopping. This occurs after the user has pressed the Stop button, but before the <c cref="SoundRecorder">SoundRecorder</c> has fully deactivated
        /// </summary>
        private bool _isStopping;

        /// <summary>
        /// Whether the UI is fully initialized or not. This value is set to true after the UI has finished loading the list of available sound devices
        /// </summary>
        private bool _initialized;

        /// <summary>
        /// Creates and initializes the RecordingWindow (main application window)
        /// </summary>
        /// <remarks>
        /// Before initializing the UI components, the application settings are checked to identify the selected language, and the corresponding culture is selected.
        /// The methods InitializeDevices and InitializeDirectory are called to finish initializing the UI.
        /// Finally, the <c cref="AutoUpdater">AutoUpdater</c> tool is called to check if a new version is available.
        /// </remarks>
        public RecordingWindow()
        {
            // Adjust the UI language according to the application settings
            System.Globalization.CultureInfo culture = new System.Globalization.CultureInfo(Properties.Settings.Default.Language);
            Thread.CurrentThread.CurrentUICulture = culture;
            Thread.CurrentThread.CurrentCulture = culture;

            // Initialize the UI components
            InitializeComponent();
            Mouse.OverrideCursor = Cursors.Wait;
            menuItemEnglish.IsChecked = Properties.Settings.Default.Language == "en";
            menuItemPortugues.IsChecked = Properties.Settings.Default.Language == "pt";
            buttonRecord.Content = Properties.Resources.StartRecording;

            // Initialize the SoundRecorder and loads the Devices
            _isRecording = false;
            _recorder = new SoundRecorder();
            InitializeDevices();
            InitializeDirectory();

            // Enable the UI
            buttonRecord.IsEnabled = true;
            menuItemRecord.IsEnabled = true;
            Mouse.OverrideCursor = null;

            // Check if a new version is available for download
            AutoUpdater.Start("http://apps.gamefulbits.com/updates/SoundBitsRecorder.xml");
        }

        /// <summary>
        /// Loads the list of available sound devices in the computer and initializes the device selection combo boxes
        /// </summary>
        private void InitializeDevices()
        {
            _initialized = false;

            // Initialize Input devices combo box
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

            // Initialize the Output devices combo box
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

        /// <summary>
        /// Loads the output directory path from the application settings.
        /// If it is not set, then the default if a folder named <c>SoundBits</c> inside the user's My Documents folder.
        /// </summary>
        /// <remarks>
        /// If the folder does not exist, it is created
        /// </remarks>
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

        /// <summary>
        /// Handles the selection of a new input or output device by the user
        /// </summary>
        /// <remarks>
        /// This implementation removes all the devices from the <c cref="SoundRecorder">SoundRecorder</c> and re-adds the selected input and output devices
        /// </remarks>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void comboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!_initialized) return;

            // Identify the volume and mute status for each device.
            // These are fist initialized to the values saved in the application settings.
            float startInputVolume = Properties.Settings.Default.InputVolume;
            float startOutputVolume = Properties.Settings.Default.OutputVolume;
            bool startInputMute = Properties.Settings.Default.InputMute;
            bool startOutputMute = Properties.Settings.Default.OutputMute;
            
            // Then, we check if we already have device controls in the screen with different volume/mute status than what is in the application settings.
            // If they exist, then it means that the UI had already been initialized before and the user is changing to a new device.
            // In this case, we want to switch to a new device but keep the current volume and mute status.
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

            // Remove all devices from the Sound Recorder
            RemoveAllDevices();

            // Create new RecordingDeviceModels for the selected devices and add them to the SoundRecorder
            int renderDeviceIndex = comboBoxOutput.SelectedIndex - 1;
            int captureDeviceIndex = comboBoxInput.SelectedIndex - 1;
            RecordingDeviceModel renderModel = _recorder.AddDevice(renderDeviceIndex == -1 ? _recorder.DefaultRenderDevice : _recorder.RenderDevices[renderDeviceIndex]);
            RecordingDeviceModel captureModel = _recorder.AddDevice(captureDeviceIndex == -1 ? _recorder.DefaultCaptureDevice : _recorder.CaptureDevices[captureDeviceIndex], renderModel.WaveFormat);

            // Create new DeviceControls for the models we just created and add them to the UI
            panelOutputDevice.Children.Add(new DeviceControl(renderModel, startOutputVolume, startOutputMute));
            panelInputDevice.Children.Add(new DeviceControl(captureModel, startInputVolume, startInputMute));
        }

        /// <summary>
        /// Removes all <c cref="RecordingDeviceModel">RecordingDeviceModel</c>s from the <c cref="SoundRecorder">SoundRecorder</c> and all the <c cref="DeviceControl">DeviceControl</c>s from the UI
        /// </summary>
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

        /// <summary>
        /// Handler for the Window's Closing event.
        /// If recording is active, a confirmation dialog is displayed asking if the user wnats to stop recording or cancel closing the window.
        /// Before closing the window, the current settings (devices, volume, mute status, and output folder) are saved to the application settings.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RecordingWindow_Closing(object sender, CancelEventArgs e)
        {
            // Check if recording is active and display a closing confirmation dialog if it is
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

            // Save the current application settings before closing the application
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

        /// <summary>
        /// Handler for the Window's Closed event
        /// </summary>
        /// <remarks>
        /// This implementation just removes all the recording devices as the window is closed
        /// </remarks>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_Closed(object sender, EventArgs e)
        {
            RemoveAllDevices();
        }

        /// <summary>
        /// Handler for the button that changes the output folder
        /// </summary>
        /// <remarks>
        /// An Open File dialog is displayed where the user can select a new output folder
        /// </remarks>
        /// <param name="sender"></param>
        /// <param name="e"></param>
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

        /// <summary>
        /// Handler for the Recording Start/Stop button
        /// </summary>
        /// <remarks>
        /// The methods <c cref="StartRecording">StartRecording</c> or <c cref="StopRecording">StopRecording</c> are called to carry out the operation
        /// </remarks>
        /// <param name="sender"></param>
        /// <param name="e"></param>
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

        /// <summary>
        /// Handler for the Open Folder link, which opens Windows Explorer to browse the selected folder
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("explorer.exe", textBoxFilename.Text);
        }

        /// <summary>
        /// Starts the Recording
        /// </summary>
        /// <remarks>
        /// This implementation disables the configuration buttons in the UI so that devices cannot be changed while recording,
        /// makes the <c cref="SoundRecorder">SoundRecorder</c> start recording, and activates the timer to update the recording time in the UI each second.
        /// </remarks>
        private void StartRecording()
        {
            // Update the UI to disable configuration changes while recording and to display the recording time
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
                // Create the output directory if it does not exist
                string directory = textBoxFilename.Text.Trim();
                if (!Directory.Exists(directory))
                {
                    _ = Directory.CreateDirectory(directory);
                }
                
                // Start recording
                buttonRecord.Content = Properties.Resources.StopRecording;
                menuItemRecord.Header = Properties.Resources.MenuStopRecording;
                _isStopping = false;
                _isRecording = true;
                _recorder.StartRecording(directory);

                // Enable the timer to update the time in the UI each second
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

        /// <summary>
        /// Stops the recording
        /// </summary>
        /// <remarks>
        /// This implementation disables the timer, stops recording in the <c cref="SoundRecorder">SoundRecorder</c>, and updates the UI to reenable all the controls.
        /// </remarks>
        private void StopRecording()
        {
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                // Stop and dispose the timer
                if (_timer != null)
                {
                    _timer.Stop();
                    _timer.Dispose();
                    _timer = null;
                }

                // Stop recording
                labelRecording.Content = Properties.Resources.Saving;
                labelTime.Visibility = Visibility.Hidden;
                if (_recorder.IsRecording)
                {
                    _recorder.StopRecording();
                }
                _isRecording = false;
                _isStopping = false;

                // Update the UI
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

        /// <summary>
        /// Handler for the Timer's elapsed event.
        /// Checks if the <c cref="SoundRecorder">SoundRecorder</c> reported an error and stops recording if it did.
        /// Otherwise, updates the recording time label in the UI.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            // Check if the SoundRecorder reported any error.
            if (_recorder.Error != null && !_isStopping)
            {
                // Stop recording and display the error
                Dispatcher.Invoke(() =>
                {
                    _isStopping = true;
                    StopRecording();
                    ShowErrorMessage(Properties.Resources.ErrorRecording + "\n\n" + _recorder.Error + "\n\n" + Properties.Resources.RecordingStopped);
                });
            }
            else if (_isRecording)
            {
                // Update the recording time in the UI
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

        /// <summary>
        /// Utility method to display a dialog box with an error message
        /// </summary>
        /// <param name="message">The message to display in the dialog box</param>
        private void ShowErrorMessage(string message)
        {
            MessageBox.Show(message, Properties.Resources.Error, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        /// <summary>
        /// Handler for the Exit menu item. Closes the window.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// Handler for the English menu item. Changes the language to English in the application settings.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void menuItemEnglish_Click(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.Language = "en";
            Properties.Settings.Default.Save();
            menuItemEnglish.IsChecked = true;
            menuItemPortugues.IsChecked = false;
            MessageBox.Show(Properties.Resources.ChangeNextTime, Properties.Resources.Information, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Handler for the Portuguese menu item. Changes the language to Portuguese in the application settings.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void menuItemPortugues_Click(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.Language = "pt";
            Properties.Settings.Default.Save();
            menuItemEnglish.IsChecked = false;
            menuItemPortugues.IsChecked = true;
            MessageBox.Show(Properties.Resources.ChangeNextTime, Properties.Resources.Information, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Handler for the About menu item. Opens the <c cref="AboutWindow">About</c> window.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
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
