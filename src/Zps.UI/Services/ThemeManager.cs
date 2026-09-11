using System.Windows;

namespace Zps.UI.Services;

/// <summary>
/// Alterna entre las paletas Light.xaml/Dark.xaml en caliente, reemplazando solo el primer
/// diccionario combinado de la aplicación (el de colores) y dejando Styles.xaml intacto,
/// ya que todos los estilos referencian los colores con DynamicResource.
/// </summary>
public static class ThemeManager
{
    public static bool EsOscuro { get; private set; }

    public static void Aplicar(bool oscuro)
    {
        EsOscuro = oscuro;
        var uri = new Uri(oscuro ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative);
        var nuevo = new ResourceDictionary { Source = uri };

        var dicts = Application.Current.Resources.MergedDictionaries;
        dicts[0] = nuevo;
    }

    public static void Alternar() => Aplicar(!EsOscuro);
}
