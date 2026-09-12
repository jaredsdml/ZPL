using System.Globalization;
using System.Windows;
using Zps.Data.Models;

namespace Zps.UI.Views;

/// <summary>
/// Diálogo modal para editar Sku/Lote/Cantidad/Cajas de un folio ya impreso antes de
/// reimprimirlo (acción "Editar y Reimprimir" en Histórico). El LPN nunca se edita: la
/// reimpresión debe conservar el mismo folio original.
/// </summary>
public partial class EditarReimpresionWindow : Window
{
    public string? NuevoSku { get; private set; }
    public string? NuevoLote { get; private set; }
    public decimal? NuevaCantidad { get; private set; }
    public string? NuevoCajas { get; private set; }

    public EditarReimpresionWindow(HistoricoImpresionRecord registro)
    {
        InitializeComponent();
        TxtLpn.Text = registro.Lpn;
        TxtSku.Text = registro.Sku ?? string.Empty;
        TxtLote.Text = registro.Lote ?? string.Empty;
        TxtCantidad.Text = registro.Cantidad?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        TxtCajas.Text = registro.Cajas ?? string.Empty;
    }

    private void BtnGuardar_OnClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(TxtCantidad.Text) &&
            !decimal.TryParse(TxtCantidad.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
        {
            MessageBox.Show(this, "La cantidad ingresada no es un número válido.", "Dato inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        NuevoSku = string.IsNullOrWhiteSpace(TxtSku.Text) ? null : TxtSku.Text.Trim();
        NuevoLote = string.IsNullOrWhiteSpace(TxtLote.Text) ? null : TxtLote.Text.Trim();
        NuevaCantidad = string.IsNullOrWhiteSpace(TxtCantidad.Text)
            ? null
            : decimal.Parse(TxtCantidad.Text, NumberStyles.Number, CultureInfo.InvariantCulture);
        NuevoCajas = string.IsNullOrWhiteSpace(TxtCajas.Text) ? null : TxtCajas.Text.Trim();

        DialogResult = true;
    }

    private void BtnCancelar_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
