using Newtonsoft.Json;

namespace JointWatermark.Class
{
    public class ExifConfigInfo : ValidationBase
    {
        public int SEQ { get; set; }
        public string? Front { get; set; }
        public string? Behind { get; set; }
        public string? Key { get; set; }

        private string? _value;
        public string? Value
        {
            get => _value;
            set
            {
                _value = value;
                NotifyPropertyChanged(nameof(Value));
            }
        }
        [JsonIgnore]
        public bool IsChanged { get; set; } = false;
    }
}
