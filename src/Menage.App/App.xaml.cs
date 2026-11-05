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
        if (Modulos.CherryPick.GaleriaCherryPick.Iniciar(this, e.Args)) return;
        if (Modulos.Diff.DemoDiff.Criar(e.Args) is { } demo)
        {
            MainWindow = demo;
            demo.Show();
            return;
        }

        var modo = ModoDeInicio.Ler(e.Args);
        _servicos = ComposicaoDoApp.Montar(modo);
        _servicos.GetRequiredService<CarregadorDeTemasPrivados>().Carregar();
        _servicos.GetRequiredService<IServicoDeTema>().Iniciar();

        if (modo.GaleriaBetaMelhorias) IniciarGaleriaBetaMelhorias(_servicos, modo);
        else if (modo.GaleriaDiarioBanco) IniciarGaleriaDiarioBanco(_servicos, modo);
        else if (modo.GaleriaGerador) IniciarGaleriaGerador(_servicos, modo);
        else if (modo.GaleriaPrincipal) IniciarGaleriaPrincipal(_servicos, modo);
        else if (modo.Galeria) IniciarGaleria(_servicos, modo);
        else IniciarNormal(_servicos, modo);
    }

    private void IniciarNormal(ServiceProvider servicos, ModoDeInicio modo)
    {
        var principal = servicos.GetRequiredService<Modulos.JanelaPrincipal.JanelaPrincipal>();
        MainWindow = principal;
        principal.Show();
        servicos.GetRequiredService<Modulos.BetaAutomatica.InicioDaBetaAutomatica>().Iniciar();
        _ = servicos.GetRequiredService<CoordenadorDeAtualizacao>().VerificarEmSegundoPlanoAsync(principal);
    }

    private void IniciarGaleriaBetaMelhorias(ServiceProvider servicos, ModoDeInicio modo)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var dono = servicos.GetRequiredService<Modulos.BetaAutomatica.JanelaBetaAutomatica>();
        dono.ShowActivated = false;
        dono.ShowInTaskbar = false;
        MainWindow = dono;
        dono.ContentRendered += async (_, _) =>
        {
            var codigo = await servicos.GetRequiredService<Galeria.RoteiroDaBetaEMelhorias>().ExecutarAsync(dono, Path.GetFullPath(modo.PastaDaGaleriaBetaMelhorias!));
            Shutdown(codigo);
        };
        dono.Show();
    }

    private void IniciarGaleriaDiarioBanco(ServiceProvider servicos, ModoDeInicio modo)
    {
        var janela = servicos.GetRequiredService<Modulos.Diario.JanelaDoDiario>();
        MainWindow = janela;
        janela.ShowActivated = false;
        janela.ShowInTaskbar = false;
        janela.ContentRendered += async (_, _) =>
        {
            var codigo = await servicos.GetRequiredService<Modulos.Diario.RoteiroDoDiarioEBanco>().ExecutarAsync(janela, Path.GetFullPath(modo.PastaDaGaleriaDiarioBanco!));
            Shutdown(codigo);
        };
        janela.Show();
    }

    private void IniciarGaleriaGerador(ServiceProvider servicos, ModoDeInicio modo)
    {
        var janela = servicos.GetRequiredService<Modulos.GeradorBeta.JanelaDoGerador>();
        MainWindow = janela;
        janela.ShowActivated = false;
        janela.ShowInTaskbar = false;
        janela.ContentRendered += async (_, _) =>
        {
            var codigo = await servicos.GetRequiredService<Modulos.GeradorBeta.RoteiroDoGerador>().ExecutarAsync(janela, Path.GetFullPath(modo.PastaDaGaleriaGerador!));
            Shutdown(codigo);
        };
        janela.Show();
    }

    private void IniciarGaleriaPrincipal(ServiceProvider servicos, ModoDeInicio modo)
    {
        var janela = servicos.GetRequiredService<Modulos.JanelaPrincipal.JanelaPrincipal>();
        MainWindow = janela;
        janela.ShowActivated = false;
        janela.ShowInTaskbar = false;
        janela.ContentRendered += async (_, _) =>
        {
            var codigo = await servicos.GetRequiredService<RoteiroDaJanelaPrincipal>().ExecutarAsync(janela, Path.GetFullPath(modo.PastaDaGaleriaPrincipal!));
            Shutdown(codigo);
        };
        janela.Show();
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
