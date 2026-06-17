using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PlugINPRO.Commands.VK
{
    public class HydraulicCalcViewModel : INotifyPropertyChanged
    {
        private string _systemName;
        private string _systemInfo;
        private ObservableCollection<HydraulicSection> _sections;

        public string SystemName
        {
            get => _systemName;
            set { _systemName = value; OnPropertyChanged(); }
        }

        public string SystemInfo
        {
            get => _systemInfo;
            set { _systemInfo = value; OnPropertyChanged(); }
        }

        public ObservableCollection<HydraulicSection> Sections
        {
            get => _sections;
            set { _sections = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class HydraulicSection : INotifyPropertyChanged
    {
        private int _number;
        private string _diameter;
        private double _length;
        private double _flow;
        private string _velocity;
        private int _fixtureCount;
        private string _fixtureNames;
        private string _pipeIds;

        public int Number
        {
            get => _number;
            set { _number = value; OnPropertyChanged(); }
        }

        public string Diameter
        {
            get => _diameter;
            set { _diameter = value; OnPropertyChanged(); }
        }

        public double Length
        {
            get => _length;
            set { _length = value; OnPropertyChanged(); }
        }

        public double Flow
        {
            get => _flow;
            set { _flow = value; OnPropertyChanged(); }
        }

        public string Velocity
        {
            get => _velocity;
            set { _velocity = value; OnPropertyChanged(); }
        }

        public int FixtureCount
        {
            get => _fixtureCount;
            set { _fixtureCount = value; OnPropertyChanged(); }
        }

        public string FixtureNames
        {
            get => _fixtureNames;
            set { _fixtureNames = value; OnPropertyChanged(); }
        }

        public string PipeIds
        {
            get => _pipeIds;
            set { _pipeIds = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}