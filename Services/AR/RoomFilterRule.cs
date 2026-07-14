using System.ComponentModel;

namespace PlugINPRO.Services.AR
{
    public class RoomFilterRule : INotifyPropertyChanged
    {
        private string _parameterName;
        private RoomFilterCondition _condition;
        private string _value;

        public string ParameterName
        {
            get => _parameterName;
            set { _parameterName = value; OnPropertyChanged(nameof(ParameterName)); }
        }

        public RoomFilterCondition Condition
        {
            get => _condition;
            set { _condition = value; OnPropertyChanged(nameof(Condition)); }
        }

        public string Value
        {
            get => _value;
            set { _value = value; OnPropertyChanged(nameof(Value)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}