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
    private readonly string _mensajeContrasenaIncorrecta;

    /// <summary>
    /// Diálogo genérico de contraseña maestra: por defecto arma el texto de borrado seguro
    /// (uso original en Histórico), pero admite personalizar título/mensaje/texto del botón
    /// de confirmación para reutilizarlo en otros accesos protegidos (p. ej. la
    /// administración de plantillas ZPL) sin duplicar la lógica de verificación.
    /// </summary>
    public ConfirmacionPasswordWindow(string? titulo = null, string? mensaje = null, string? textoBotonConfirmar = null)
    {
        InitializeComponent();

        if (titulo is not null)
        {
            Title = titulo;
        }

        if (mensaje is not null)
        {
            TxtMensaje.Text = mensaje;
        }

        if (textoBotonConfirmar is not null)
        {
            BtnConfirmar.Content = textoBotonConfirmar;
        }

        _mensajeContrasenaIncorrecta = mensaje is null
            ? "Contraseña incorrecta. Se canceló la eliminación."
            : "Contraseña incorrecta. Se canceló la operación.";

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
            _mensajeContrasenaIncorrecta,
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
