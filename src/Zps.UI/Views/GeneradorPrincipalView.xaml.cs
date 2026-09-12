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
        // "_EsValido"/"_MensajeError" son solo para el resaltado de fila; "_YaGuardado"/
        // "_TarimaActual"/"_TarimaTotal"/"_Consecutivo" son metadatos internos del flujo
        // Generar/Guardar/Imprimir. Ninguna de estas se muestra en la grilla.
        if (e.PropertyName is "_EsValido" or "_MensajeError" or "_Seleccionado"
            or "_YaGuardado" or "_TarimaActual" or "_TarimaTotal" or "_Consecutivo")
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
