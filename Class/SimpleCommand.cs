using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Input;

namespace JointWatermark.Class
{
    public class SimpleCommand : ICommand
    {
        public SimpleCommand() { }
        public Predicate<object> CanExecuteDelegate { get; set; }
        public Action<object> ExecuteDelegate { get; set; }

        #region ICommand Members

        public bool CanExecute(object parameter)
        {
            if (CanExecuteDelegate != null)
                return CanExecuteDelegate((object)parameter);
            return true;// if there is no can execute default to true
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public void Execute(object parameter)
        {
            if (ExecuteDelegate != null)
                ExecuteDelegate((object)parameter);
        }

        #endregion
    }
}
