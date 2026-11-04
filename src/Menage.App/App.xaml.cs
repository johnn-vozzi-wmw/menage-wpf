using System.Windows;
using Menage.Core.Atualizacao;
using Microsoft.Extensions.DependencyInjection;

namespace Menage.App;

public partial class App : Application
{
    private ServiceProvider? _servicos;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (Modulos.Diff.DemoDiff.Criar(e.Args) is { } demo)
        {
            MainWindow = demo;
            demo.Show();
            return;
        }
        _servicos = Configurar();
        var principal = _servicos.GetRequiredService<MainWindow>();
        MainWindow = principal;
        principal.Show();
        _ = _servicos.GetRequiredService<CoordenadorDeAtualizacao>().VerificarEmSegundoPlanoAsync(principal);
    }

    private static ServiceProvider Configurar()
    {
        var caminho = OpcoesDeAtualizacao.CaminhoPadrao;
        return new ServiceCollection()
            .AddSingleton<IServicoDeAtualizacao>(_ =>
                new ServicoDeAtualizacaoVelopack(OpcoesDeAtualizacao.Carregar(caminho), caminho))
            .AddSingleton<CoordenadorDeAtualizacao>()
            .AddSingleton<MainWindow>()
            .BuildServiceProvider();
    }
}
