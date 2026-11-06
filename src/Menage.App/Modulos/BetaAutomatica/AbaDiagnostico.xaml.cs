using System.Windows;
using System.Windows.Controls;

namespace Menage.App.Modulos.BetaAutomatica;

/// <summary>Aba Diagnóstico. A cópia vai para a área de transferência aqui porque o ViewModel não conhece o Windows.</summary>
public partial class AbaDiagnostico : UserControl
{
    DiagnosticoViewModel? _vm;

    public AbaDiagnostico()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (_vm is not null) _vm.PedirCopia -= Copiar;
            _vm = e.NewValue as DiagnosticoViewModel;
            if (_vm is not null) _vm.PedirCopia += Copiar;
        };
    }

    static void Copiar(string texto)
    {
        if (texto.Length > 0) Clipboard.SetText(texto);
    }
}
