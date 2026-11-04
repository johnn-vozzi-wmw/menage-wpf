using System.Windows;
using Menage.Pocs.Comum;

namespace Menage.Pocs.Janela;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        new ConfiguradorJanela().Aplicar(this, Raiz);
        SizeChanged += (_, _) => Estado.Text = $"Tamanho: {ActualWidth:0} x {ActualHeight:0}  Estado: {WindowState}";
        Loaded += async (_, _) =>
        {
            var args = new Argumentos(Environment.GetCommandLineArgs());
            if (args.Valor("--autoteste") is { } saida)
                await new AutotesteJanela(this, saida, args.Valor("--capturas")).ExecutarAsync();
        };
    }
}
