namespace JointWatermark.Class
{
    public class ExifInfo : ValidationBase
    {
        private bool isSelected;
        public bool IsSelected
        {
            get => isSelected;
            set
            {
                isSelected = value;
                NotifyPropertyChanged(nameof(IsSelected));
            }
        }

        public string Key { get; set; } = "";

        private string name = "";
        public string Name
        {
            get => name;
            set
            {
                name = value;
                NotifyPropertyChanged(nameof(Name));
            }
        }

        private string _value = "";
        public string Value
        {
            get => _value;
            set
            {
                _value = value;
                NotifyPropertyChanged(nameof(Value));
            }
        }


    }
}
