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
        /// <summary>
        /// Initializes the About window
        /// </summary>
        /// <remarks>
        /// The application version is obtained from the Assembly version.
        /// The license text is read from a text file in the application directory accordingly to the currently selected language.
        /// </remarks>
        public AboutWindow()
        {
            InitializeComponent();
            label3.Content = Assembly.GetExecutingAssembly().GetName().Version
                + " " + Properties.Resources.built
                + " " + Properties.Resources.BuildDate;
            string filename = "LICENSE" + (Properties.Settings.Default.Language == "en" ? "" : "-" + Properties.Settings.Default.Language) + ".txt";
            textBox.Text = File.ReadAllText(filename);
        }

        /// <summary>
        /// Handles the Close button click
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void button_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// Handles the website link click
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            _ = Process.Start(e.Uri.AbsoluteUri);
            e.Handled = true;
        }
    }
}
