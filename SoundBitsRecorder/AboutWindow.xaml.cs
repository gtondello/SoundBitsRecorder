using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;

namespace SoundBitsRecorder
{
    /// <summary>
    /// Interaction logic for AboutWindow.xaml
    /// </summary>
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            label3.Content = Assembly.GetExecutingAssembly().GetName().Version
                + " " + SoundBitsRecorder.Properties.Resources.built
                + " " + SoundBitsRecorder.Properties.Resources.BuildDate;
            var filename = "LICENSE" + (Properties.Settings.Default.Language == "en" ? "" : "-" + Properties.Settings.Default.Language) + ".txt";
            textBox.Text = File.ReadAllText(filename);
        }

        private void button_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            Process.Start(e.Uri.AbsoluteUri);
            e.Handled = true;
        }
    }
}
