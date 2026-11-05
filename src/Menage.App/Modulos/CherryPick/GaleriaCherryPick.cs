using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Menage.App.Tema;
using Menage.Core.CherryPick;
using Menage.Core.Comandos;
using Menage.Core.Operacoes;
using Menage.Core.Projetos;
using Microsoft.Extensions.DependencyInjection;

namespace Menage.App.Modulos.CherryPick;

/// <summary>
/// <c>--galeria-cherry &lt;pasta&gt;</c>: cria repositórios descartáveis em %TEMP%, abre a janela Cherry-pick e a da fila (com um conflito
/// de verdade) em segundo plano, grava as capturas em claro e escuro e uma lista do que foi conferido. Usa a pasta de dados temporária
/// da galeria: não toca nos dados do usuário.
/// </summary>
public static class GaleriaCherryPick
{
    const string Argumento = "--galeria-cherry";

    /// <summary>Verdadeiro se os argumentos eram da galeria (então o app não segue o caminho normal).</summary>
    public static bool Iniciar(Application app, string[] args)
    {
        var i = Array.IndexOf(args, Argumento);
        if (i < 0 || i + 1 >= args.Length) return false;
        var saida = Path.GetFullPath(args[i + 1]);
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        app.Dispatcher.InvokeAsync(async () => app.Shutdown(await ExecutarAsync(app, saida)));
        return true;
    }

    static async Task<int> ExecutarAsync(Application app, string saida)
    {
        var raiz = Path.Combine(Path.GetTempPath(), "mgcg" + Guid.NewGuid().ToString("N")[..4]);
        var verificacoes = new List<string>();
        try
        {
            Directory.CreateDirectory(saida);
            Preparar(raiz);
            using var servicos = ComposicaoDoApp.Montar(new ModoDeInicio(true, null, raiz));
            servicos.GetRequiredService<CarregadorDeTemasPrivados>().Carregar();
            servicos.GetRequiredService<IServicoDeTema>().Iniciar();
            var projetos = servicos.GetRequiredService<IProjetos>();
            foreach (var p in projetos.Todos) projetos.Marcar(p.Caminho, true);

            await HistoricoAsync(servicos, saida, verificacoes);
            await FilaAsync(servicos, saida, verificacoes);
        }
        catch (Exception ex)
        {
            verificacoes.Add($"FALHOU: exceção {ex}");
        }
        finally
        {
            await File.WriteAllLinesAsync(Path.Combine(saida, "verificacoes.txt"), verificacoes);
            ApagarRaiz(raiz);
        }

        return verificacoes.Exists(v => v.StartsWith("FALHOU", StringComparison.Ordinal)) ? 1 : 0;
    }

    static async Task HistoricoAsync(ServiceProvider servicos, string saida, List<string> verificacoes)
    {
        var janela = servicos.GetRequiredService<JanelaCherryPick>();
        Mostrar(janela);
        await Esperar(2000);
        var modelo = janela.Modelo;
        var linhas = modelo.Paineis[0].Visao?.Cast<LinhaDeCommit>().ToList() ?? [];
        verificacoes.Add(Conferir($"histórico: 2 painéis, o primeiro com {linhas.Count} commits (esperado 6)", modelo.Paineis.Count == 2 && linhas.Count == 6));
        PainelDeHistorico.Alternar(linhas.Take(2));
        modelo.Texto = "vendas";
        await Esperar(900);
        var filtradas = modelo.Paineis[0].Visao?.Cast<object>().Count() ?? 0;
        verificacoes.Add(Conferir($"filtro por texto \"vendas\" deixa {filtradas} commits (esperado 5)", filtradas == 5));
        verificacoes.Add(Conferir($"2 marcados somam {modelo.TotalMarcados} e liberam as ações", modelo.TotalMarcados == 2 && modelo.PodeAgir));
        await Capturar(janela, servicos, saida, "cherry-pick-historico");
        janela.Close();
    }

    static async Task FilaAsync(ServiceProvider servicos, string saida, List<string> verificacoes)
    {
        var fila = servicos.GetRequiredService<ServicoDaFila>();
        var projeto = servicos.GetRequiredService<IProjetos>().Todos[0];
        var planos = servicos.GetRequiredService<Menage.Core.Historico.LeitorDeHistorico>();
        var todos = (await planos.LerAsync(projeto.Caminho, "ativ", false)).Valor.Reverse().ToList();
        var commits = todos.Skip(todos.Count - 3).ToList();
        fila.Adicionar(commits.Select(c => LeitorDeDetalhes.ParaItem(projeto, c, "ativ")));
        fila.Configurar("develop", backup: true, atualizarDestino: false);

        await Passo(saida, verificacoes, "fila montada");
        var janela = servicos.GetRequiredService<JanelaFila>();
        Mostrar(janela);
        await Esperar(800);
        await Passo(saida, verificacoes, "janela da fila aberta");
        await servicos.GetRequiredService<CoordenadorDeOperacoes>().ExecutarAsync(servicos.GetRequiredService<FilaDeCherryPickEmLote>(), new OpcoesDaFila());
        await Esperar(900);
        var estados = string.Join(", ", fila.Fila.Itens.Select(i => i.Estado));
        verificacoes.Add(Conferir($"fila pausou no conflito ({estados})", fila.Fila.Estado == EstadoDaFila.Pausada && fila.Fila.ItemAtual?.Estado == EstadoDoItem.Conflito));
        verificacoes.Add(Conferir("janela mostra o aviso de pausa com o arquivo em conflito", janela.Modelo.Pausada && janela.Modelo.ListaDeArquivos.Contains("x.txt")));
        await Capturar(janela, servicos, saida, "cherry-pick-fila-conflito");
        var relatorio = await servicos.GetRequiredService<ControleDaFila>().AbortarAsync(restaurar: true);
        verificacoes.Add(Conferir($"abortar devolve o destino ({string.Join("; ", relatorio)})", fila.Fila.Estado == EstadoDaFila.Abortada));
        janela.Close();
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

    static async Task Passo(string saida, List<string> verificacoes, string texto)
    {
        verificacoes.Add("passo: " + texto);
        await File.WriteAllLinesAsync(Path.Combine(saida, "verificacoes.txt"), verificacoes);
    }

    static string Conferir(string nome, bool ok) => $"{(ok ? "ok" : "FALHOU")}: {nome}";

    static async Task Esperar(int ms)
    {
        await Task.Delay(ms);
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    // Dois repositórios: develop com um commit que conflita com o segundo commit da atividade.
    static void Preparar(string raiz)
    {
        foreach (var nome in new[] { "lavenderecore", "lavendereweb" })
        {
            var repo = Path.Combine(raiz, nome);
            Directory.CreateDirectory(repo);
            Git(repo, "init", "-q", "-b", "main");
            Git(repo, "config", "user.name", "Maria Souza");
            Git(repo, "config", "user.email", "maria@exemplo.local");
            Commit(repo, "README.md", "exemplo\n", "Commit inicial");
            Git(repo, "checkout", "-q", "-b", "develop");
            Commit(repo, "x.txt", "base\n", "VENDAS-100 cria x");
            Git(repo, "checkout", "-q", "-b", "ativ");
            Commit(repo, "a.txt", "a\n", "VENDAS-101 ajusta tela de pedidos");
            Commit(repo, "x.txt", "da atividade\n", "VENDAS-101 muda x");
            Commit(repo, "c.txt", "c\n", "VENDAS-102 relatório de vendas");
            Git(repo, "checkout", "-q", "develop");
            Commit(repo, "x.txt", "da develop\n", "VENDAS-103 corrige x na develop");
            Git(repo, "checkout", "-q", "main");
        }
    }

    static void Commit(string repo, string arquivo, string conteudo, string mensagem)
    {
        File.WriteAllText(Path.Combine(repo, arquivo), conteudo);
        Git(repo, "add", "--", arquivo);
        Git(repo, "commit", "-q", "-m", mensagem);
    }

    static void Git(string repo, params string[] args)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = repo, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) info.ArgumentList.Add(a);
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        using var processo = Process.Start(info)!;
        processo.WaitForExit();
    }

    static void ApagarRaiz(string raiz)
    {
        try
        {
            foreach (var arquivo in Directory.EnumerateFiles(raiz, "*", SearchOption.AllDirectories)) File.SetAttributes(arquivo, FileAttributes.Normal);
            Directory.Delete(raiz, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
