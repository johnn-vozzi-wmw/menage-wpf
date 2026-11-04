using System.Reflection;
using Menage.App.Controles;

namespace Menage.App;

public partial class MainWindow : JanelaBase
{
    public MainWindow(ContextoDeJanela contexto) : base(contexto)
    {
        InitializeComponent();
        var versao = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        Versao.Text = $"Versão {versao?.Split('+')[0]}";
    }
}
