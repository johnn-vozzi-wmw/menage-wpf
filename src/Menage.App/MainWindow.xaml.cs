using System.Reflection;
using System.Windows;

namespace Menage.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var versao = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        Versao.Text = $"Versão {versao?.Split('+')[0]}";
    }
}
