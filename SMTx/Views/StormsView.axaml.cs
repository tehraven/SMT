using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SMTx.Views
{
    public partial class StormsView : UserControl
    {
        public StormsView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
