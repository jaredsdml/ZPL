using System.Windows;
using System.Windows.Controls;
using Zps.UI.ViewModels;

namespace Zps.UI.Views;

public partial class GeneradorPrincipalView : UserControl
{
    public GeneradorPrincipalView()
    {
        InitializeComponent();
    }

    private void DataGridDatos_OnAutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        // "_Seleccionado" ya se agrega a mano como la primera columna (checkbox);
        // "_EsValido"/"_MensajeError" son solo para el resaltado de fila, nunca se muestran.
        if (e.PropertyName is "_EsValido" or "_MensajeError" or "_Seleccionado")
        {
            e.Cancel = true;
            return;
        }

        // El DataGrid es editable a nivel de control (para permitir el checkbox), pero
        // las columnas de datos del Excel en sí no deben poder editarse.
        e.Column.IsReadOnly = true;
    }

    private void ChkSeleccionarTodos_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox && DataContext is GeneradorPrincipalViewModel vm)
        {
            vm.SeleccionarTodosCommand.Execute(checkBox.IsChecked == true);
        }
    }
}
