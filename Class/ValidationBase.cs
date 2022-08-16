using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace JointWatermark.Class
{
    public class ValidationBase : INotifyPropertyChanged
    {
        public ValidationBase() { }
        
        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void NotifyPropertyChanged(string propertyName)
        {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
