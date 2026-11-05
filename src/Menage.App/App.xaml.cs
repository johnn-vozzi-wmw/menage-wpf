using System.IO;
using System.Windows;
using Menage.App.Galeria;
using Menage.App.Tema;
using Microsoft.Extensions.DependencyInjection;

namespace Menage.App;

public partial class App : Application
{
    private ServiceProvider? _servicos;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var modo = ModoDeInicio.Ler(e.Args);
        _servicos = ComposicaoDoApp.Montar(modo);
        _servicos.GetRequiredService<CarregadorDeTemasPrivados>().Carregar();
        _servicos.GetRequiredService<IServicoDeTema>().Iniciar();

        if (modo.Galeria) IniciarGaleria(_servicos, modo);
        else IniciarNormal(_servicos, modo);
    }

    private void IniciarNormal(ServiceProvider servicos, ModoDeInicio modo)
    {
        var principal = servicos.GetRequiredService<Modulos.JanelaPrincipal.JanelaPrincipal>();
        MainWindow = principal;
        principal.Show();
        _ = servicos.GetRequiredService<CoordenadorDeAtualizacao>().VerificarEmSegundoPlanoAsync(principal);
    }

    private void IniciarGaleria(ServiceProvider servicos, ModoDeInicio modo)
    {
        var janela = servicos.GetRequiredService<JanelaDaGaleria>();
        MainWindow = janela;
        if (modo.PastaDaGaleria is not { } pasta)
        {
            janela.Show();
            return;
        }

        // Automático: não rouba o foco de quem está usando a máquina.
        janela.ShowActivated = false;
        janela.ShowInTaskbar = false;
        janela.ContentRendered += async (_, _) =>
        {
            var codigo = await servicos.GetRequiredService<RoteiroDaGaleria>().ExecutarAsync(janela, Path.GetFullPath(pasta));
            Shutdown(codigo);
        };
        janela.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _servicos?.Dispose();
        base.OnExit(e);
    }
}
