using System.IO;
using System.Windows;
using Menage.App.Galeria;
using Menage.App.Tema;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Menage.App;

public partial class App : Application
{
    private ServiceProvider? _servicos;
    private Inicializacao.InstanciaUnica? _instancia;
    private Task<Inicializacao.RegistroEmArquivo>? _registro;
    private readonly Inicializacao.MedidorDeInicio _medidor = new();
    private Menage.Core.Inicializacao.ArgumentosDoApp _argumentos = Menage.Core.Inicializacao.ArgumentosDoApp.Ler([]);

    private Inicializacao.FabricaDeLogTardia? FabricaDeLog => _registro is null ? null : new Inicializacao.FabricaDeLogTardia(_registro);

    private Menage.Core.Armazenamento.PastaDeDados? PastaEscolhida =>
        _argumentos.PastaDeDados is { } p ? new Menage.Core.Armazenamento.PastaDeDados(p) : null;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _argumentos = Menage.Core.Inicializacao.ArgumentosDoApp.Ler(e.Args);
        if (Modulos.CherryPick.GaleriaCherryPick.Iniciar(this, e.Args)) return;
        if (Modulos.Instalacoes.GaleriaInstalacoes.Iniciar(this, e.Args)) return;
        if (Modulos.Diff.DemoDiff.Criar(e.Args) is { } demo)
        {
            MainWindow = demo;
            demo.Show();
            return;
        }

        var modo = ModoDeInicio.Ler(e.Args);
        _medidor.Marcar("onstartup");
        if (_argumentos.Ajuda)
        {
            MessageBox.Show(Menage.Core.Inicializacao.ArgumentosDoApp.Texto, "Menage", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var pasta = PastaEscolhida ?? Menage.Core.Armazenamento.PastaDeDados.Padrao();
        if (modo.Normal && !IniciarComoInstanciaUnica(pasta)) return;
        _servicos = ComposicaoDoApp.Montar(modo, pasta, FabricaDeLog);
        _medidor.Marcar("servicos");
        _servicos.GetRequiredService<CarregadorDeTemasPrivados>().Carregar();
        _servicos.GetRequiredService<IServicoDeTema>().Iniciar();
        _servicos.GetRequiredService<Modulos.Personalizacao.ServicoDeAparencia>().Iniciar();
        Icones.ServicoDeIcones.Compartilhado = _servicos.GetRequiredService<Icones.ServicoDeIcones>();
        if (modo.Normal && _argumentos.Tema is { } tema && _servicos.GetRequiredService<ICatalogoDeTemas>().Buscar(tema) is { } escolhido)
            _servicos.GetRequiredService<IServicoDeTema>().Trocar(escolhido.Id, animar: false);
        _medidor.Marcar("tema");

        if (modo.GaleriaPersonalizacao) IniciarGaleriaPersonalizacao(_servicos, modo);
        else if (modo.GaleriaBetaMelhorias) IniciarGaleriaBetaMelhorias(_servicos, modo);
        else if (modo.GaleriaDiarioBanco) IniciarGaleriaDiarioBanco(_servicos, modo);
        else if (modo.GaleriaNtp) IniciarGaleriaNtp(_servicos, modo);
        else if (modo.GaleriaConfiguracoes) IniciarGaleriaConfiguracoes(_servicos, modo);
        else if (modo.GaleriaGerador) IniciarGaleriaGerador(_servicos, modo);
        else if (modo.GaleriaPrincipal) IniciarGaleriaPrincipal(_servicos, modo);
        else if (modo.Galeria) IniciarGaleria(_servicos, modo);
        else IniciarNormal(_servicos, modo);
    }

    /// <summary>
    /// Só uma instância por pasta de dados: a segunda pede à primeira que mostre a janela e sai. Também liga o log em arquivo
    /// e a rede de proteção contra exceções. A medição (<c>--medir-inicio</c>) nunca fala com uma instância real.
    /// </summary>
    private bool IniciarComoInstanciaUnica(Menage.Core.Armazenamento.PastaDeDados pasta)
    {
        if (_argumentos.ArquivoDeMedicao is null)
        {
            _instancia = Inicializacao.InstanciaUnica.Adquirir(pasta.Caminho);
            if (!_instancia.EhPrimeira)
            {
                if (!_argumentos.Minimizado) _instancia.PedirParaMostrar();
                _instancia.Dispose();
                _instancia = null;
                Shutdown();
                return false;
            }

            _instancia.AoPedirJanela = () => Dispatcher.BeginInvoke(TrazerJanelaParaFrente);
            _instancia.IniciarEscuta();
        }

        _registro = Task.Run(() => Inicializacao.RegistroEmArquivo.Abrir(pasta.Caminho));
        var log = FabricaDeLog!.CreateLogger("Menage");
        new Inicializacao.TratadorGlobalDeErros(log, System.IO.Path.Combine(pasta.Caminho, "logs")).Instalar(this);
        log.LogInformation("Menage {Versao} iniciado; dados em {Pasta}", typeof(App).Assembly.GetName().Version, pasta.Caminho);
        return true;
    }

    private void TrazerJanelaParaFrente()
    {
        if (MainWindow is not { } janela) return;
        if (janela.WindowState == WindowState.Minimized) janela.WindowState = WindowState.Normal;
        janela.Show();
        janela.Activate();
    }

    private void IniciarNormal(ServiceProvider servicos, ModoDeInicio modo)
    {
        var principal = servicos.GetRequiredService<Modulos.JanelaPrincipal.JanelaPrincipal>();
        _medidor.Marcar("janela-criada");
        MainWindow = principal;
        if (_argumentos.ArquivoDeMedicao is { } arquivo)
        {
            principal.ShowActivated = false;
            principal.ShowInTaskbar = false;
            principal.ContentRendered += (_, _) => _ = Dispatcher.InvokeAsync(() =>
            {
                _medidor.Marcar("renderizada");
                _medidor.Gravar(arquivo);
                Shutdown();
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        if (_argumentos.Minimizado) principal.WindowState = WindowState.Minimized;
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

    private void IniciarGaleriaNtp(ServiceProvider servicos, ModoDeInicio modo)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _ = Dispatcher.InvokeAsync(async () =>
        {
            var codigo = await servicos.GetRequiredService<Galeria.RoteiroDeNexusTomcatPrs>().ExecutarAsync(Path.GetFullPath(modo.PastaDaGaleriaNtp!));
            Shutdown(codigo);
        });
    }

    private void IniciarGaleriaConfiguracoes(ServiceProvider servicos, ModoDeInicio modo)
    {
        var janela = servicos.GetRequiredService<Modulos.Configuracoes.JanelaDeConfiguracoes>();
        MainWindow = janela;
        janela.ShowActivated = false;
        janela.ShowInTaskbar = false;
        janela.ContentRendered += async (_, _) =>
        {
            var codigo = await servicos.GetRequiredService<Modulos.Configuracoes.RoteiroDeConfiguracoes>().ExecutarAsync(janela, Path.GetFullPath(modo.PastaDaGaleriaConfiguracoes!));
            Shutdown(codigo);
        };
        janela.Show();
    }

    private void IniciarGaleriaPersonalizacao(ServiceProvider servicos, ModoDeInicio modo)
    {
        var janela = servicos.GetRequiredService<Modulos.JanelaPrincipal.JanelaPrincipal>();
        MainWindow = janela;
        janela.ShowActivated = false;
        janela.ShowInTaskbar = false;
        janela.ContentRendered += async (_, _) =>
        {
            var codigo = await servicos.GetRequiredService<Modulos.Personalizacao.RoteiroDePersonalizacao>().ExecutarAsync(janela, Path.GetFullPath(modo.PastaDaGaleriaPersonalizacao!));
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
        _instancia?.Dispose();
        if (_registro is { IsCompletedSuccessfully: true }) _registro.Result.Dispose();
        base.OnExit(e);
    }
}
