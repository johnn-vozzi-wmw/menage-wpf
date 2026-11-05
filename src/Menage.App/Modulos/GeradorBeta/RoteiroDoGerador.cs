using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Menage.App.Controles;
using Menage.App.Tema;
using Menage.Core.Beta;
using Menage.Core.Entregas;
using Microsoft.Extensions.DependencyInjection;

namespace Menage.App.Modulos.GeradorBeta;

/// <summary>
/// Modo <c>--galeria-gerador &lt;pasta&gt;</c>: abre o Gerador sobre pastas descartáveis em %TEMP% (nenhum Maven roda, nenhuma
/// pasta real é lida) com status e log de exemplo, e captura o Gerador e os diálogos nos temas escuro e claro.
/// </summary>
public sealed class RoteiroDoGerador(IServiceProvider servicos, IServicoDeTema tema, RepositorioDoGerador repositorio, GeradorBetaViewModel modelo)
{
    readonly List<string> _verificacoes = [];
    string _pasta = "";
    string _cenario = "";

    public async Task<int> ExecutarAsync(JanelaDoGerador janela, string pasta)
    {
        _pasta = pasta;
        Directory.CreateDirectory(pasta);
        try
        {
            _cenario = Path.Combine(Path.GetTempPath(), "gg" + Guid.NewGuid().ToString("N")[..6]);
            Preparar();
            await modelo.IniciarAsync();
            await Esperar(1200);
            foreach (var id in new[] { CatalogoDeTemas.IdEscuro, CatalogoDeTemas.IdClaro }) await CapturarTemaAsync(janela, id);
        }
        catch (Exception ex)
        {
            _verificacoes.Add($"FALHOU: exceção {ex}");
        }
        finally
        {
            if (Directory.Exists(_cenario)) Directory.Delete(_cenario, recursive: true);
        }

        await File.WriteAllLinesAsync(Path.Combine(pasta, "verificacoes.txt"), _verificacoes);
        return _verificacoes.Exists(v => v.StartsWith("FALHOU", StringComparison.Ordinal)) ? 1 : 0;
    }

    void Conferir(string nome, bool ok) => _verificacoes.Add($"{(ok ? "ok" : "FALHOU")}: {nome}");

    void Preparar()
    {
        var baseDir = Path.Combine(_cenario, "base");
        foreach (var p in repositorio.Configuracao.Projetos)
        {
            var dir = Directory.CreateDirectory(Path.Combine(baseDir, p.Pastas[^1])).FullName;
            File.WriteAllText(Path.Combine(dir, "pom.xml"), "<project/>");
        }

        File.WriteAllLines(Path.Combine(_cenario, "exemplo.log"),
        [
            "====================================================================================================",
            "Projeto  : lavenderedados", "Comando  : mvn -B clean install -Duser.language.format=pt",
            "====================================================================================================",
            "[INFO] Scanning for projects...", "[INFO] --- maven-clean-plugin:3.1.0:clean (default-clean) @ lavenderedados ---",
            "[INFO] --- maven-compiler-plugin:3.8.1:compile (default-compile) @ lavenderedados ---",
            "[WARNING] Using platform encoding (Cp1252) to copy filtered resources",
            "[INFO] Tests run: 48, Failures: 0, Errors: 0, Skipped: 0",
            "[ERROR] /src/main/java/ExemploDeErro.java:[42,13] cannot find symbol",
            "[INFO] --- maven-war-plugin:3.3.1:war (default-war) @ lavenderedados ---",
        ]);
        var c = repositorio.Configuracao;
        repositorio.Salvar(c with
        {
            PastasBase = [baseDir],
            UltimaBase = baseDir,
            UltimaSelecao = ["lavenderecore", "lavenderedados", "lavendereweb"],
            LimparTarget = new LimparTargetConfig { Ativo = true, Projetos = ["lavenderecore", "lavenderedados", "lavendereweb"] },
            Entregas = c.Entregas with { Pasta = Path.Combine(_cenario, "entregas") },
            Favoritos =
            [
                new FavoritoDoGerador { Nome = "Web completa", Projetos = ["lavenderecore", "lavenderedados", "lavendereweb"] },
                new FavoritoDoGerador { Nome = "PDA", Projetos = ["framework-tc", "lavenderepda"] },
                new FavoritoDoGerador { Nome = "Só o web", Projetos = ["lavendereweb"] },
            ],
        });
    }

    async Task CapturarTemaAsync(JanelaDoGerador janela, string id)
    {
        tema.Trocar(id, animar: false);
        await Esperar(400);
        PrepararTela();
        await Esperar(400);
        Conferir($"{id}: 11 projetos na tabela", modelo.Linhas.Count == 11);
        Conferir($"{id}: 3 favoritos como cartões", modelo.Favoritos.Count == 3);
        Conferir($"{id}: Web completa fica ativo com a seleção atual", modelo.Favoritos[0].Ativo);
        Conferir($"{id}: limpar target mostra o número de projetos", modelo.TextoLimparTarget == "Habilitado em 3 projeto(s)");
        Salvar(janela, $"gerador-{id}");

        modelo.EntregaPronta = new EntregaPronta("Web", new ResultadoDaEntrega(@"C:\wmw\entregas\web_2026-11-04", @"C:\wmw\entregas\web_2026-11-04.zip",
            ["lavenderedados.war  (41,2 MB)", "lavendereweb.war  (87,0 MB)", "ZIP: web_2026-11-04.zip  (118,4 MB)"], []));
        await Esperar(300);
        Salvar(janela, $"gerador-{id}-entrega-pronta");
        modelo.EntregaPronta = null;

        await CapturarDialogoAsync<JanelaDeEntrega>(janela, $"entrega-{id}", new PedidoDeEntrega("Web", "lavendereweb finalizado: lavenderecore, lavenderedados, lavendereweb ok.",
            @"C:\wmw\entregas", true, true,
            ["✔ lavenderedados: lavenderedados-5.0.war  (41,2 MB)", "✔ lavendereweb: lavendereweb-5.0.war  (87,0 MB)"]));
        await CapturarDialogoAsync<JanelaDeLimparTarget>(janela, $"limpar-target-{id}", new PedidoDeLimparTarget(repositorio.Configuracao.LimparTarget, repositorio.Configuracao.Projetos));
        await CapturarDialogoAsync<JanelaDeEntregas>(janela, $"entregas-{id}", new PedidoDeEntregas(repositorio.Configuracao.Entregas));
    }

    void PrepararTela()
    {
        var porNome = modelo.Linhas.ToDictionary(l => l.Nome);
        var fim = DateTime.Now;
        foreach (var l in modelo.Linhas) l.Branch = l.Grupo == "PDA" ? "release/5.0" : "develop";
        porNome["lavenderecore"].Aplicar(new StatusDoProjeto("lavenderecore", FaseDaGeracao.Ok, TimeSpan.FromSeconds(151), "OK - 120 testes (0 falhas, 0 erros)"));
        porNome["lavenderedados"].Aplicar(new StatusDoProjeto("lavenderedados", FaseDaGeracao.Executando, Log: Path.Combine(_cenario, "exemplo.log"), Inicio: fim.AddSeconds(-47)));
        porNome["lavendereweb"].Aplicar(new StatusDoProjeto("lavendereweb", FaseDaGeracao.NaFila));
        porNome["lavenderedados"].AtualizarTempoCorrido();
        modelo.Log.Mostrar(porNome["lavenderedados"]);
        modelo.Log.Tique();
        modelo.SelecionadaNaTela = porNome["lavenderedados"];
    }

    async Task CapturarDialogoAsync<T>(Window dono, string nome, object pedido) where T : JanelaBase, IJanelaComParametro
    {
        var dialogo = servicos.GetRequiredService<T>();
        dialogo.Receber(pedido);
        dialogo.Owner = dono;
        dialogo.ShowActivated = false;
        dialogo.ShowInTaskbar = false;
        dialogo.Show();
        await Esperar(700);
        Salvar(dialogo, nome);
        dialogo.Close();
        await Esperar(200);
    }

    void Salvar(Window janela, string nome) => SalvarPng(janela, nome, _pasta);

    static void SalvarPng(Window janela, string nome, string pasta)
    {
        var elemento = (FrameworkElement)VisualTreeHelper.GetChild(janela, 0);
        var dpi = VisualTreeHelper.GetDpi(elemento);
        var imagem = new RenderTargetBitmap(
            (int)Math.Ceiling(elemento.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(elemento.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        imagem.Render(elemento);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(imagem));
        using var arquivo = File.Create(Path.Combine(pasta, nome + ".png"));
        png.Save(arquivo);
    }

    static async Task Esperar(int ms)
    {
        await Task.Delay(ms);
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }
}
