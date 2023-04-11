using System.Windows;

namespace JointWatermark.Class
{
    public class BottomProcessInstance : ValidationBase
    {
        public BottomProcessInstance(Visibility v, bool s)
        {
            Visibility = v;
            IsLoading = s;
        }


        private Visibility visibility;
        public Visibility Visibility
        {
            get => visibility;
            set
            {
                visibility = value;
                NotifyPropertyChanged(nameof(Visibility));
            }
        }


        private bool isLoading;
        public bool IsLoading
        {
            get => isLoading;
            set
            {
                isLoading = value;
                NotifyPropertyChanged(nameof(IsLoading));
            }
        }
    }
}
