using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Menage.App.Controles;
using Menage.App.Tema;
using Menage.Core.Atualizacao;
using Microsoft.Extensions.DependencyInjection;

namespace Menage.App.Modulos.Instalacoes;

/// <summary>Feed de mentira da galeria: nada de rede, instalador ou arquivo real.</summary>
sealed class GerenteDeExemplo : IGerenteDeInstalacao
{
    public bool Instalado => true;

    public string? VersaoAtual => "1.1.0";

    public Task<IReadOnlyList<LancamentoDaVersao>> ListarAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<LancamentoDaVersao>>([
            new("1.0.0", "- Primeira versão publicada.", 48_000_000),
            new("1.1.0", "- Cherry-pick em fila.\n- Gerador de beta com favoritos.", 49_500_000),
            new("1.2.0", "- Sobre / Atualizações dentro do app.\n- Voltar para a versão anterior.\n- Canal beta.", 50_200_000),
        ]);

    public Task BaixarAsync(string versao, IProgress<int> progresso, CancellationToken ct = default) => Task.CompletedTask;

    public void AplicarEReiniciar(string versao)
    {
    }
}

/// <summary>
/// <c>--galeria-instalacoes &lt;pasta&gt;</c>: abre a tela Sobre / Atualizações com feed falso e um "Git Manager antigo" de
/// mentira (pasta vazia em %TEMP%), em segundo plano, e grava capturas claro/escuro de três estados. Não instala nem baixa nada.
/// </summary>
public static class GaleriaInstalacoes
{
    const string Argumento = "--galeria-instalacoes";

    public static bool Iniciar(Application app, string[] args)
    {
        var i = Array.IndexOf(args, Argumento);
        if (i < 0 || i + 1 >= args.Length) return false;
        var saida = Path.GetFullPath(args[i + 1]);
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        app.Dispatcher.InvokeAsync(async () => app.Shutdown(await ExecutarAsync(saida)));
        return true;
    }

    static async Task<int> ExecutarAsync(string saida)
    {
        var raiz = Path.Combine(Path.GetTempPath(), "mgin" + Guid.NewGuid().ToString("N")[..4]);
        var verificacoes = new List<string>();
        try
        {
            Directory.CreateDirectory(saida);
            Directory.CreateDirectory(Path.Combine(raiz, "GMV10"));
            using var servicos = ComposicaoDoApp.Montar(new ModoDeInicio(true, null, raiz));
            servicos.GetRequiredService<CarregadorDeTemasPrivados>().Carregar();
            servicos.GetRequiredService<IServicoDeTema>().Iniciar();

            var arquivo = Path.Combine(raiz, "atualizacao.json");
            var fabrica = new FabricaDeExemplo();
            var servico = new ServicoDeInstalacoes(fabrica, new OpcoesDeAtualizacao(@"\\servidor\menage\releases", null), arquivo, "1.1.0");
            var modelo = new SobreViewModel(servico, new DetectorDoAppAntigo(raiz));
            var janela = new JanelaSobre(servicos.GetRequiredService<ContextoDeJanela>(), modelo);
            Mostrar(janela);
            await Esperar(1200);

            verificacoes.Add(Conferir("estado inicial sem verificar", modelo.Versoes.Count == 0 && modelo.VersaoAtual == "1.1.0"));
            await Capturar(janela, servicos, saida, "sobre-inicial");

            await modelo.VerificarAsync();
            await Esperar(500);
            verificacoes.Add(Conferir($"verificou: {modelo.Status}", modelo.PodeAtualizar && modelo.Versoes.Count == 3 && modelo.PodeVoltar));
            verificacoes.Add(Conferir("detectou o app antigo (só leitura)", modelo.Antigo.Contains("GMV10", StringComparison.Ordinal)));
            await Capturar(janela, servicos, saida, "sobre-nova-versao");

            modelo.PedirVoltarCommand.Execute(null);
            await Esperar(500);
            verificacoes.Add(Conferir($"confirmação de voltar: {modelo.TextoDaConfirmacao}", modelo.Confirmando));
            await Capturar(janela, servicos, saida, "sobre-voltar-confirmacao");
            janela.Close();
        }
        catch (Exception ex)
        {
            verificacoes.Add($"FALHOU: exceção {ex}");
        }
        finally
        {
            await File.WriteAllLinesAsync(Path.Combine(saida, "verificacoes.txt"), verificacoes);
            try { Directory.Delete(raiz, true); } catch (IOException) { }
        }

        return verificacoes.Exists(v => v.StartsWith("FALHOU", StringComparison.Ordinal)) ? 1 : 0;
    }

    sealed class FabricaDeExemplo : IFabricaDeGerente
    {
        public IGerenteDeInstalacao? Criar(string? origem, string? canal) => string.IsNullOrWhiteSpace(origem) ? null : new GerenteDeExemplo();
    }

    static async Task Capturar(Window janela, ServiceProvider servicos, string saida, string nome)
    {
        var tema = servicos.GetRequiredService<IServicoDeTema>();
        foreach (var id in new[] { CatalogoDeTemas.IdEscuro, CatalogoDeTemas.IdClaro })
        {
            tema.Trocar(id, animar: false);
            await Esperar(500);
            var elemento = (FrameworkElement)VisualTreeHelper.GetChild(janela, 0);
            var dpi = VisualTreeHelper.GetDpi(elemento);
            var imagem = new RenderTargetBitmap((int)Math.Ceiling(elemento.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(elemento.ActualHeight * dpi.DpiScaleY),
                dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            imagem.Render(elemento);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(imagem));
            using var arquivo = File.Create(Path.Combine(saida, $"{nome}-{id}.png"));
            png.Save(arquivo);
        }
    }

    static void Mostrar(Window janela)
    {
        janela.ShowActivated = false;
        janela.ShowInTaskbar = false;
        janela.WindowStartupLocation = WindowStartupLocation.Manual;
        (janela.Left, janela.Top) = (40, 40);
        janela.Show();
    }

    static string Conferir(string nome, bool ok) => $"{(ok ? "ok" : "FALHOU")}: {nome}";

    static async Task Esperar(int ms)
    {
        await Task.Delay(ms);
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }
}
