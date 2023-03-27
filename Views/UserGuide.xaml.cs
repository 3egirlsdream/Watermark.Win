using Newtonsoft.Json;
using System.Windows;

namespace JointWatermark.Views
{
    /// <summary>
    /// UserGuide.xaml 的交互逻辑
    /// </summary>
    public partial class UserGuide : Window
    {
        public UserGuide()
        {
            InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            trans.SelectedIndex += 1;
        }

        private void neverShowClick(object sender, RoutedEventArgs e)
        {
            var model = Global.InitConfig();
            model.ShowGuide = false;
            var js = JsonConvert.SerializeObject(model);
            Global.SaveConfig(js);
            this.Close();
        }

        private void nextPageClick(object sender, RoutedEventArgs e)
        {
            trans.SelectedIndex += 1;
        }

        private void previousPage(object sender, RoutedEventArgs e)
        {
            trans.SelectedIndex -= 1;
        }

        private void CloseClick(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
