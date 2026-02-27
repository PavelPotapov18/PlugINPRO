using Autodesk.Revit.DB;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Color = Autodesk.Revit.DB.Color;

namespace PlugINPRO
{
    /// <summary>
    /// Interaction logic for IFCPaintWindow.xaml
    /// </summary>
    public partial class IFCPaintWindow : Window
    {
        private readonly Document _doc;

        public string FilterName { get; private set; }

        public Color SelectedColor { get; private set; }

        public IFCPaintWindow(Document doc)
        {
            InitializeComponent();
            _doc = doc;

            SelectedColor = new Color(255, 0, 0);
            UpdateColorPreview();
        }


        private void PickColor_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.ColorDialog();

            if (dialog.ShowDialog() ==
                System.Windows.Forms.DialogResult.OK)
            {
                SelectedColor = new Color(
                    dialog.Color.R,
                    dialog.Color.G,
                    dialog.Color.B);

                UpdateColorPreview();
            }
        }
        private void UpdateColorPreview()
        {
            ColorPreview.Background =
                new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(
                        SelectedColor.Red,
                        SelectedColor.Green,
                        SelectedColor.Blue));
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            FilterName = FilterNameBox.Text;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void AnyValueChanged(object sender, TextChangedEventArgs e)
        {

        }
    }
}
