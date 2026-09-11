using System.Windows.Controls;
using Zps.Data.Models;
using Zps.UI.ViewModels;

namespace Zps.UI.Views;

public partial class HistoricoView : UserControl
{
    public HistoricoView()
    {
        InitializeComponent();
    }

    private void DataGridHistorico_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is HistoricoViewModel vm)
        {
            vm.ActualizarSeleccion(DataGridHistorico.SelectedItems.Cast<HistoricoImpresionRecord>().ToList());
        }
    }
}
