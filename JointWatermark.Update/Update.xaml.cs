using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.DirectoryServices.ActiveDirectory;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using WeakToys.Class;

namespace JointWatermark.Update
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class Update : Window
    {
        public Update()
        {
            InitializeComponent();
            Run();
        }

        public async void Run()
        {
            setPercent(0, "关闭程序...");
            await ShundownProgram();

            setPercent(10, "正在下载最新程序...");
            DownloadZip();
        }
        private Task ShundownProgram()
        {
            return Task.Run(() =>
            {
                Process[] processes = Process.GetProcesses();
                foreach (Process p in processes)
                {
                    if (p.ProcessName == "JointWatermark")
                    {
                        p.Kill();
                    }
                }
            });
        }

        private async void DownloadZip()
        {
            string newPath = "";
            string Http = "http://thankful.top:4396";
            var version = await Connections.HttpGetAsync<CLIENT_VERSION>(Http + "/api/CloudSync/GetVersion?Client=Watermark", Encoding.Default);
            if (version != null && version.success && version.data != null && version.data.VERSION != null)
            {
                newPath = version.data.PATH;
            }
            using (var wc = new WebClient())
            {
                try
                {
                    var fileName = newPath.Substring(newPath.LastIndexOf('/') + 1);
                    var path = AppDomain.CurrentDomain.BaseDirectory;// + System.IO.Path.DirectorySeparatorChar + fileName;
                    if (!Directory.Exists(newPath))
                    {
                        Directory.CreateDirectory(path);
                    }
                    wc.DownloadProgressChanged += (ss, e) =>
                    {
                        App.Current.Dispatcher.Invoke(() =>
                        {
                            setPercent(80, $"已下载{e.ProgressPercentage}%");
                        });
                    };
                    await wc.DownloadFileTaskAsync(new Uri(newPath), path + System.IO.Path.DirectorySeparatorChar + fileName);

                    setPercent(80, "正在安装...");
                    await DecompressZip(path + System.IO.Path.DirectorySeparatorChar + fileName, AppDomain.CurrentDomain.BaseDirectory);

                    setPercent(100, "安装完成");
                    await Task.Delay(1000);
                    this.Close();
                }
                catch (Exception ex)
                {
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        setPercent(0, ex.Message);
                    });
                    
                }
            }
        }

        private void setPercent(int percent, string msg)
        {
            int p = percent / 10;
            update.Content = "";
            for(int i = 0; i < 10;i++) 
            { 
                if(i < p)
                {
                    update.Content += "⚑";
                }
                else
                {
                    update.Content += "⚐";
                }
            }
            this.msg.Content = msg;
        }

        private Task DecompressZip(string zipPath, string folderPath)
        {
            return Task.Run(() =>
            {
                try
                {
                    var tempPath = folderPath + System.IO.Path.DirectorySeparatorChar + "temp";
                    DirectoryInfo _tempFolder = new(tempPath);

                    if (!_tempFolder.Exists)
                    {
                        _tempFolder.Create();
                    }

                    var zipFolder = new DirectoryInfo(tempPath);
                    zipFolder.GetFiles().ToImmutableList().ForEach(c =>
                    {
                        File.Delete(c.FullName);
                    });
                    ZipFile.ExtractToDirectory(zipPath, tempPath);

                    //var zipFolder = new DirectoryInfo(tempPath);
                    var ls = zipFolder.GetFiles().Select(c => c.Name).ToList();

                    var folder = new DirectoryInfo(folderPath);
                    foreach (var file in folder.GetFiles())
                    {
                        try
                        {
                            if (ls.Contains(file.Name)) File.Delete(file.FullName);
                        }
                        catch
                        {

                        }
                    }
                    //复制
                    foreach (var name in ls)
                    {
                        try
                        {
                            var sourcePath = System.IO.Path.Combine(tempPath, name);
                            var aimPath = System.IO.Path.Combine(folderPath, name);
                            File.Copy(sourcePath, aimPath);
                        }
                        catch { }
                    }

                    zipFolder.GetFiles().ToImmutableList().ForEach(c =>
                    {
                        File.Delete(c.FullName);
                    });
                    Directory.Delete(tempPath);
                    File.Delete(zipPath);
                }
                catch(Exception ex)
                {
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        setPercent(0, ex.Message);
                    });
                }
            });
            
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
    public class CLIENT_VERSION
    {
        public string? ID { get; set; }
        public DateTime DATETIME { get; set; }
        public string? CLIENT { get; set; }
        public string? VERSION { get; set; }
        public string? PATH { get; set; }
    }
}
