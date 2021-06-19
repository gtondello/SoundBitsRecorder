using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace SoundBitsRecorder
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            if (SoundBitsRecorder.Properties.Settings.Default.Main == "Advanced")
            {
                AdvancedWindow window = new AdvancedWindow();
                window.Show();
            }
            else
            {
                MainWindow window = new MainWindow();
                window.Show();
            }
        }
    }
}
