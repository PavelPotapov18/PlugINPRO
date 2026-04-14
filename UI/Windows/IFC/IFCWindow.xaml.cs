using System.Windows;
using System.Globalization;


namespace PlugINPRO.UI.Windows.IFC
{
    /// <summary>
    /// Interaction logic for UserControl1.xaml
    /// </summary>
    public partial class IFCWindow : Window
    {
        public double OffsetX { get; private set; }
        public double OffsetY { get; private set; }
        public double OffsetZ { get; private set; }
        public IFCWindow()
        {
            InitializeComponent();
        }
        private void OnConfirm(object sender, RoutedEventArgs e)
        {
            if (!TryParse())
            {
                MessageBox.Show("Введите корректные числовые значения");
                return;
            }

            DialogResult = true;
            Close();
        }

        private bool TryParse()
        {
            bool okX = double.TryParse(
                XBox.Text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double x);

            bool okY = double.TryParse(
                YBox.Text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double y);

            bool okZ = double.TryParse(
                ZBox.Text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double z);

            if (!okX || !okY || !okZ)
                return false;

            OffsetX = x;
            OffsetY = y;
            OffsetZ = z;

            return true;
        }
    }
}
