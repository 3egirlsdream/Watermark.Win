using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace JointWatermark.Class
{
    public class ValidationBase : IDataErrorInfo, INotifyPropertyChanged
    {
        public ValidationBase() { }

        public string this[string columnName] => throw new NotImplementedException();

        public string Error => "";

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void NotifyPropertyChanged(string propertyName)
        {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
