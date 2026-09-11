using System.Windows;
using System.Windows.Input;

namespace Zps.UI.Views;

/// <summary>
/// Diálogo modal de confirmación con contraseña para el borrado seguro en Histórico.
/// DialogResult=true solo si la contraseña ingresada coincide exactamente; cualquier
/// intento incorrecto muestra un error y cierra el diálogo como cancelado (sin reintentos).
/// </summary>
public partial class ConfirmacionPasswordWindow : Window
{
    private const string PasswordEsperada = "4ba4e13d1F";

    public ConfirmacionPasswordWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => CajaPassword.Focus();
    }

    private void BtnConfirmar_OnClick(object sender, RoutedEventArgs e) => Confirmar();

    private void CajaPassword_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Confirmar();
        }
    }

    private void Confirmar()
    {
        if (CajaPassword.Password == PasswordEsperada)
        {
            DialogResult = true;
            return;
        }

        MessageBox.Show(
            this,
            "Contraseña incorrecta. Se canceló la eliminación.",
            "Acceso denegado",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        DialogResult = false;
    }

    private void BtnCancelar_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
