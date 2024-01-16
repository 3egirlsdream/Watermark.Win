using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Shapes;
using Watermark.Win.Models;

namespace Watermark.Win
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, error) =>
            {
                try
                {
                    var text = error.ExceptionObject.ToString();
                    var path = $"{AppDomain.CurrentDomain.BaseDirectory}Logs{System.IO.Path.DirectorySeparatorChar}";
                    if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                    System.IO.File.WriteAllText($"{path}{DateTime.Now.ToString("yyyyMMddHHmmss")}.txt", text);
                }
                catch { }
            };
        }
    }

}
