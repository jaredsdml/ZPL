using System.Collections.Generic;
using System.Windows;
using Zps.UI.Models;

namespace Zps.UI.Views;

public partial class AuditoriaVirtualWindow : Window
{
    public AuditoriaVirtualWindow(IReadOnlyList<AuditoriaEtiqueta> etiquetas)
    {
        InitializeComponent();
        DataContext = etiquetas;
    }
}
