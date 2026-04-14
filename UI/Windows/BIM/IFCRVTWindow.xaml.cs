using System.Windows;
using WinForms = System.Windows.Forms;


namespace PlugINPRO.UI.Windows.BIM
{
    /// <summary>
    /// Interaction logic for IFCRVTWindow.xaml
    /// </summary>
    public partial class IFCRVTWindow : Window
    {
        public string InputDirectory { get; private set; }
        public string OutputDirectory { get; private set; }
        public IFCRVTWindow()
        {
            InitializeComponent();
        }
        private void BrowseSource(object sender, RoutedEventArgs e)
        {
            using (var dialog = new WinForms.FolderBrowserDialog())
            {
                if (dialog.ShowDialog() == WinForms.DialogResult.OK)
                {
                    SourceBox.Text = dialog.SelectedPath;
                }
            }
        }
        private void BrowseTarget(object sender, RoutedEventArgs e)
        {
            using (var dialog = new WinForms.FolderBrowserDialog())
            {
                if (dialog.ShowDialog() == WinForms.DialogResult.OK)
                {
                    TargetBox.Text = dialog.SelectedPath;
                }
            }
        }
        private void OnConfirm(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SourceBox.Text)
                || string.IsNullOrWhiteSpace(TargetBox.Text))
            {
                MessageBox.Show("Выберите обе папки");
                return;
            }

            InputDirectory = SourceBox.Text;
            OutputDirectory = TargetBox.Text;

            DialogResult = true;
            Close();
        }
    }
}
