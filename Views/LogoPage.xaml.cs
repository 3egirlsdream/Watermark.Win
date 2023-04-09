using JointWatermark.Class;
using MaterialDesignThemes.Wpf;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace JointWatermark.Views
{
    /// <summary>
    /// LogoPage.xaml 的交互逻辑
    /// </summary>
    public partial class LogoPage : Page
    {
        public string SelectedLogo;
        LogoPageVM vm;
        public Action<Photo> GetPath;
        public LogoPage()
        {
            InitializeComponent();
            vm = new LogoPageVM(this);
            DataContext = vm;
        }
    }

    public class LogoPageVM : ValidationBase
    {
        LogoPage page;
        public LogoPageVM(LogoPage page)
        {
            this.page = page;
            InitLogoes();
        }
        private ObservableCollection<string> iconList = new();
        public ObservableCollection<string> IconList
        {
            get => iconList;
            set
            {
                iconList = value;
                NotifyPropertyChanged(nameof(IconList));
            }
        }


        public void InitLogoes()
        {
            try
            {
                if (!Directory.Exists(Global.Path_logo))
                {
                    Directory.CreateDirectory(Global.Path_logo);
                }
                DirectoryInfo directory = new(Global.Path_logo);
                var files = directory.GetFiles();
                IconList.Clear();
                foreach (var file in files)
                {
                    var img = new System.Windows.Controls.Image();
                    img.Width = 40;
                    img.Height = 40;
                    BitmapImage map = new(new Uri(file.FullName, UriKind.Absolute));
                    img.Source = map;
                    IconList.Add(file.FullName);
                }
                var cloudIcons = Global.InitConfig().Icons;

                foreach (var icon in cloudIcons)
                {
                    try
                    {
                        var img = new System.Windows.Controls.Image();
                        img.Width = 40;
                        img.Height = 40;
                        BitmapImage map = new BitmapImage(new Uri(icon, UriKind.Absolute));
                        img.Source = map;
                        var menuDel = new MenuItem();
                        menuDel.Icon = new PackIcon() { Kind = PackIconKind.Delete };
                        menuDel.Header = "删除";
                        menuDel.Click += (ss, es) =>
                        {
                            var model = Global.InitConfig();
                            model.Icons.Remove(icon);
                            var js = JsonConvert.SerializeObject(model);
                            Global.SaveConfig(js);
                            InitLogoes();
                        };

                        IconList.Add(icon);
                    }
                    catch (Exception ex)
                    {
                        Global.SendMsg(ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Global.SendMsg(ex.Message);
            }
        }
        public SimpleCommand CmdSetIcon => new()
        {
            ExecuteDelegate = x =>
            {
                if (x is string c && !string.IsNullOrEmpty(c))
                {
                    var photo = new Photo();
                    if (c.StartsWith("http"))
                    {
                        photo.IsCloud = true;
                        photo.Path = c;
                    }
                    else
                    {
                        photo.Path = c.Substring(c.LastIndexOf(Global.SeparatorChar) + 1);
                        photo.IsCloud = false;
                    }
                    page.GetPath?.Invoke(photo);
                }
            },
            CanExecuteDelegate = o => true
        };
    }
}
