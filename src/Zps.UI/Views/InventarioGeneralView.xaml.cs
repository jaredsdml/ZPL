using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Zps.UI.Views;

public partial class InventarioGeneralView : UserControl
{
    public InventarioGeneralView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Reemplaza el copiado por defecto de DataGrid: además del texto, WPF agrega al
    /// portapapeles un payload "HTML Format" que en la práctica sale malformado y hace que
    /// Excel rechace el pegado ("Microsoft Excel no puede pegar los datos"). Aquí se arma a
    /// mano un TSV limpio (columnas con \t, filas con \r\n) a partir de las celdas
    /// sombreadas — sueltas, en rango, o filas completas vía el encabezado de fila — y se
    /// deja solo Text/UnicodeText en el DataObject, que Excel sí interpreta de forma nativa.
    /// </summary>
    private void DataGridInventario_OnCopyExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        e.Handled = true;

        var celdasSeleccionadas = DataGridInventario.SelectedCells;
        if (celdasSeleccionadas.Count == 0)
        {
            return;
        }

        // Se reagrupa por fila (en el orden real del ItemsSource, no en el orden en que WPF
        // reporta la selección) y, dentro de cada fila, por la posición visible de columna,
        // para que el TSV resultante sea una tabla coherente sin importar cómo se sombreó.
        var filas = celdasSeleccionadas
            .GroupBy(celda => celda.Item)
            .OrderBy(grupo => DataGridInventario.Items.IndexOf(grupo.Key))
            .Select(grupo => grupo.OrderBy(celda => celda.Column.DisplayIndex).ToList())
            .ToList();

        var tsv = new StringBuilder();
        for (var f = 0; f < filas.Count; f++)
        {
            var columnasDeLaFila = filas[f];
            for (var c = 0; c < columnasDeLaFila.Count; c++)
            {
                tsv.Append(ObtenerTextoCelda(columnasDeLaFila[c].Column, columnasDeLaFila[c].Item));
                if (c < columnasDeLaFila.Count - 1)
                {
                    tsv.Append('\t');
                }
            }

            if (f < filas.Count - 1)
            {
                tsv.Append("\r\n");
            }
        }

        var texto = tsv.ToString();
        var dataObject = new DataObject();
        dataObject.SetData(DataFormats.UnicodeText, texto);
        dataObject.SetData(DataFormats.Text, texto);
        Clipboard.SetDataObject(dataObject, true);
    }

    /// <summary>
    /// Evalúa el Binding de la columna (con su StringFormat, si tiene uno — p. ej. la
    /// columna Caducidad usa StringFormat=d) contra el item de la fila, replicando lo que el
    /// usuario ve en pantalla en vez del ToString() crudo del valor.
    /// </summary>
    private static string ObtenerTextoCelda(DataGridColumn columna, object item)
    {
        if (columna is not DataGridBoundColumn { Binding: Binding binding } || binding.Path?.Path is not { } propiedad)
        {
            return string.Empty;
        }

        var valor = item.GetType().GetProperty(propiedad)?.GetValue(item);
        if (valor is null)
        {
            return string.Empty;
        }

        var texto = !string.IsNullOrEmpty(binding.StringFormat)
            ? string.Format(CultureInfo.CurrentCulture, binding.StringFormat, valor)
            : valor.ToString() ?? string.Empty;

        // Un tab o salto de línea dentro del propio valor rompería la estructura del TSV.
        return texto.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }
}
