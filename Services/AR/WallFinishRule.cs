using Autodesk.Revit.DB;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace PlugINPRO.Services.AR
{
    public class WallFinishRule : INotifyPropertyChanged
    {
        private WallType _sourceWallType;
        public WallType SourceWallType
        {
            get => _sourceWallType;
            set
            {
                _sourceWallType = value;
                OnPropertyChanged(nameof(SourceWallType));
            }
        }

        public ObservableCollection<FinishLayer> Layers { get; set; }
            = new ObservableCollection<FinishLayer>();

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}