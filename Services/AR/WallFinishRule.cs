using Autodesk.Revit.DB;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace PlugINPRO.Services.AR
{
    public class WallFinishRule : INotifyPropertyChanged
    {
        // =========================================================
        // ТИП СТЕНЫ-ОСНОВЫ
        // =========================================================

        private WallType _sourceWallType;

        public WallType SourceWallType
        {
            get => _sourceWallType;

            set
            {
                if (_sourceWallType == value)
                    return;

                _sourceWallType = value;

                OnPropertyChanged(
                    nameof(SourceWallType));
            }
        }


        // =========================================================
        // СЛОИ ОТДЕЛКИ
        // =========================================================

        public ObservableCollection<FinishLayer> Layers
        {
            get;
        } = new ObservableCollection<FinishLayer>();


        // =========================================================
        // ЗАЩИТА ОТ РЕКУРСИИ
        // =========================================================

        private bool _isUpdating;


        // =========================================================
        // КОНСТРУКТОР
        // =========================================================

        public WallFinishRule()
        {
            Layers.CollectionChanged +=
                Layers_CollectionChanged;

            // При создании правила сразу создаем
            // первый слой отделки.
            AddLayer();
        }


        // =========================================================
        // ИЗМЕНЕНИЕ КОЛЛЕКЦИИ СЛОЕВ
        // =========================================================

        private void Layers_CollectionChanged(
            object sender,
            NotifyCollectionChangedEventArgs e)
        {
            // Подписываемся на новые слои
            if (e.NewItems != null)
            {
                foreach (FinishLayer layer in e.NewItems)
                {
                    layer.PropertyChanged +=
                        Layer_PropertyChanged;
                }
            }


            // Отписываемся от удаленных слоев
            if (e.OldItems != null)
            {
                foreach (FinishLayer layer in e.OldItems)
                {
                    layer.PropertyChanged -=
                        Layer_PropertyChanged;
                }
            }


            // После добавления/удаления слоя
            // пересчитываем цепочку.
            RecalculateAllOffsets();
        }


        // =========================================================
        // ИЗМЕНЕНИЕ ПАРАМЕТРОВ СЛОЯ
        // =========================================================

        private void Layer_PropertyChanged(
            object sender,
            PropertyChangedEventArgs e)
        {
            if (_isUpdating)
                return;


            // Нас интересуют только параметры,
            // которые влияют на положение следующих слоев.
            if (e.PropertyName != nameof(FinishLayer.BaseOffsetMm) &&
                e.PropertyName != nameof(FinishLayer.HeightMm) &&
                e.PropertyName != nameof(FinishLayer.BaseOffset) &&
                e.PropertyName != nameof(FinishLayer.Height))
            {
                return;
            }


            FinishLayer changedLayer =
                sender as FinishLayer;

            if (changedLayer == null)
                return;


            int index =
                Layers.IndexOf(changedLayer);

            if (index < 0)
                return;


            // Пересчитываем только слои,
            // расположенные ниже измененного.
            RecalculateFrom(index);
        }


        // =========================================================
        // ДОБАВЛЕНИЕ СЛОЯ
        // =========================================================

        public void AddLayer()
        {
            FinishLayer newLayer =
                new FinishLayer();


            // Если это не первый слой,
            // ставим его сразу после предыдущего.
            if (Layers.Count > 0)
            {
                FinishLayer previous =
                    Layers[Layers.Count - 1];

                newLayer.BaseOffsetMm =
                    previous.BaseOffsetMm +
                    previous.HeightMm;
            }


            Layers.Add(newLayer);
        }


        // =========================================================
        // УДАЛЕНИЕ ПОСЛЕДНЕГО СЛОЯ
        // =========================================================

        public void RemoveLastLayer()
        {
            if (Layers.Count == 0)
                return;


            Layers.RemoveAt(
                Layers.Count - 1);
        }


        // =========================================================
        // ПЕРЕСЧЕТ СЛОЕВ НИЖЕ ИЗМЕНЕННОГО
        // =========================================================

        private void RecalculateFrom(
            int startIndex)
        {
            if (_isUpdating)
                return;


            if (startIndex < 0)
                startIndex = 0;


            _isUpdating = true;

            try
            {
                for (
                    int i = startIndex + 1;
                    i < Layers.Count;
                    i++)
                {
                    FinishLayer previous =
                        Layers[i - 1];

                    FinishLayer current =
                        Layers[i];


                    double newOffset =
                        previous.BaseOffset +
                        previous.Height;


                    current.BaseOffset =
                        newOffset;
                }
            }
            finally
            {
                _isUpdating = false;
            }
        }


        // =========================================================
        // ПОЛНЫЙ ПЕРЕСЧЕТ
        // =========================================================

        private void RecalculateAllOffsets()
        {
            if (_isUpdating)
                return;


            if (Layers.Count <= 1)
                return;


            _isUpdating = true;

            try
            {
                for (
                    int i = 1;
                    i < Layers.Count;
                    i++)
                {
                    FinishLayer previous =
                        Layers[i - 1];

                    FinishLayer current =
                        Layers[i];


                    double newOffset =
                        previous.BaseOffset +
                        previous.Height;


                    current.BaseOffset =
                        newOffset;
                }
            }
            finally
            {
                _isUpdating = false;
            }
        }


        // =========================================================
        // INotifyPropertyChanged
        // =========================================================

        public event PropertyChangedEventHandler PropertyChanged;


        protected void OnPropertyChanged(
            string propertyName)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(
                    propertyName));
        }
    }
}