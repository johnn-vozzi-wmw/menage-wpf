using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Menage.App.Controles;
using Menage.App.Modulos.Banco;
using Menage.App.Tema;
using Menage.Core.Banco;
using Menage.Core.Diario;
using Menage.Core.Projetos;
using Microsoft.Extensions.DependencyInjection;

namespace Menage.App.Modulos.Diario;

/// <summary>
/// Modo <c>--galeria-diario-banco &lt;pasta&gt;</c>: repositório e context.xml descartáveis em %TEMP% (nenhum banco, Jira ou IA é
/// acessado; nada é lançado) e captura das telas do Diário e do Contexto do banco nos temas escuro e claro.
/// </summary>
public sealed class RoteiroDoDiarioEBanco(
    IServiceProvider servicos, IServicoDeTema tema, ProjetosEmMemoria projetos, RepositorioDoDiario diario, RepositorioDeBanco banco)
{
    const string Autor = "dev@menage.local";

    readonly List<string> _verificacoes = [];
    string _pasta = "";
    string _cenario = "";

    public async Task<int> ExecutarAsync(JanelaDoDiario janela, string pasta)
    {
        _pasta = pasta;
        Directory.CreateDirectory(pasta);
        try
        {
            _cenario = Path.Combine(Path.GetTempPath(), "gd" + Guid.NewGuid().ToString("N")[..6]);
            Preparar();
            var vm = (DiarioViewModel)janela.DataContext;
            await vm.CarregarAsync(); // a janela abriu antes do cenário existir
            Conferir("dia carregado com worklogs", vm.Linhas.Count > 0);
            Conferir("a jornada fecha em 8h48", vm.Linhas.Sum(l => l.Minutos) == 528);
            Conferir("commit sem issue vira aviso", vm.Avisos.Contains("sem id de issue", StringComparison.Ordinal));
            foreach (var id in new[] { CatalogoDeTemas.IdEscuro, CatalogoDeTemas.IdClaro }) await CapturarTemaAsync(janela, vm, id);
        }
        catch (Exception ex)
        {
            _verificacoes.Add($"FALHOU: exceção {ex}");
        }
        finally
        {
            LimparCenario();
        }

        await File.WriteAllLinesAsync(Path.Combine(pasta, "verificacoes.txt"), _verificacoes);
        return _verificacoes.Exists(v => v.StartsWith("FALHOU", StringComparison.Ordinal)) ? 1 : 0;
    }

    void Conferir(string nome, bool ok) => _verificacoes.Add($"{(ok ? "ok" : "FALHOU")}: {nome}");

    void Preparar()
    {
        var repo = Directory.CreateDirectory(Path.Combine(_cenario, "web")).FullName;
        Git(repo, "init", "-q", "-b", "main");
        Commit(repo, "src/PedidoService.java", "public class PedidoService {\n    public int calculaDesconto(int qtd) {\n        return qtd * 2;\n    }\n}\n", "VENDAS-24574 aplica desconto por faixa de quantidade", 9, 20);
        Commit(repo, "src/PedidoRepository.java", "public class PedidoRepository {\n    public void salvar() {\n    }\n}\n", "VENDAS-24574 salva o pedido com o desconto", 10, 45);
        Commit(repo, "db/V12.sql", "create table PEDIDO_DESCONTO (id int);\n", "VENDAS-24601 cria a tabela de descontos", 13, 30);
        Commit(repo, "README.md", "ajuste\n", "ajusta a documentação", 15, 10);
        projetos.Adicionar(new Projeto("lavendereweb", repo));
        diario.Salvar(diario.Configuracao with { Autor = Autor });

        var contexto = Path.Combine(_cenario, "context.xml");
        File.WriteAllText(contexto, Contexto);
        banco.DefinirCaminho(contexto);
    }

    static string Linha(string chave, string url, string usuario) =>
        $"""<Resource name="jdbc/LAVENDERE{chave}DS" url="{url}" username="{usuario}" password="ENC(exemplo)"/>""";

    static readonly string Contexto = "<Context>\n  "
        + Linha("WEB", "jdbc:oracle:thin:@10.0.0.9:1521/wmw", "wmw") + "\n  " + Linha("PDA", "jdbc:oracle:thin:@10.0.0.9:1521/wmw", "pda") + "\n  "
        + Linha("INT", "jdbc:oracle:thin:@10.0.0.9:1521/wmw", "wmw") + "\n  <!-- "
        + Linha("WEB", "jdbc:postgresql://pg.local:5432/vendas", "vendas") + "\n  " + Linha("PDA", "jdbc:postgresql://pg.local:5432/vendas_pda", "vendas") + " -->\n</Context>";

    static void Commit(string repo, string arquivo, string conteudo, string mensagem, int hora, int minuto)
    {
        var destino = Path.Combine(repo, arquivo);
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        File.WriteAllText(destino, conteudo);
        var data = string.Create(CultureInfo.InvariantCulture, $"{DateTime.Today:yyyy-MM-dd}T{hora:00}:{minuto:00}:00");
        Git(repo, ["add", "--", arquivo], data);
        Git(repo, ["commit", "-q", "-m", mensagem], data);
    }

    static void Git(string repo, params string[] args) => Git(repo, args, null);

    static void Git(string repo, string[] args, string? data)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = repo, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) info.ArgumentList.Add(a);
        foreach (var (k, v) in new Dictionary<string, string>
        {
            ["GIT_CONFIG_GLOBAL"] = "NUL", ["GIT_CONFIG_NOSYSTEM"] = "1", ["GIT_TERMINAL_PROMPT"] = "0", ["GIT_EDITOR"] = "true", ["GIT_PAGER"] = "cat",
            ["GIT_AUTHOR_NAME"] = "Dev", ["GIT_AUTHOR_EMAIL"] = Autor, ["GIT_COMMITTER_NAME"] = "Dev", ["GIT_COMMITTER_EMAIL"] = Autor,
        }) info.Environment[k] = v;
        if (data is not null) (info.Environment["GIT_AUTHOR_DATE"], info.Environment["GIT_COMMITTER_DATE"]) = (data, data);
        using var p = Process.Start(info)!;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
    }

    void LimparCenario()
    {
        if (!Directory.Exists(_cenario)) return;
        foreach (var f in Directory.EnumerateFiles(_cenario, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_cenario, recursive: true);
    }

    async Task CapturarTemaAsync(JanelaDoDiario janela, DiarioViewModel vm, string id)
    {
        tema.Trocar(id, animar: false);
        await Esperar(500);
        vm.Selecionada = vm.Linhas.FirstOrDefault();
        await Esperar(300);
        Salvar(janela, $"diario-{id}");

        vm.PorCommit = true;
        await Esperar(2500);
        Conferir($"{id}: por commit gera um worklog por commit com issue", vm.Linhas.Count == 3 && vm.Linhas.Sum(l => l.Minutos) == 528);
        Salvar(janela, $"diario-por-commit-{id}");
        vm.PorCommit = false;
        await Esperar(2500);

        await CapturarAsync<JanelaConfigDiario>(janela, $"diario-configurar-{id}", null);
        var dialogoBanco = await CapturarAsync<JanelaDoBanco>(janela, $"banco-gerenciar-{id}", null, antes: PrepararBanco);
        Conferir($"{id}: banco tem 2 perfis importados", ((BancoViewModel)dialogoBanco.DataContext).Perfis.Count == 2);
        await CapturarAsync<JanelaImportarBanco>(janela, $"banco-importar-{id}", ((BancoViewModel)dialogoBanco.DataContext).LerDoArquivo());
    }

    void PrepararBanco()
    {
        if (banco.Perfis.Count > 0) return;
        banco.Importar(ContextoXml.LerTodos(Contexto).Select(b => b.Perfil));
    }

    async Task<T> CapturarAsync<T>(Window dono, string nome, object? parametro, Action? antes = null) where T : JanelaBase
    {
        antes?.Invoke();
        var dialogo = servicos.GetRequiredService<T>();
        (dialogo as IJanelaComParametro)?.Receber(parametro);
        dialogo.Owner = dono;
        dialogo.ShowActivated = false;
        dialogo.ShowInTaskbar = false;
        dialogo.Show();
        await Esperar(900);
        Salvar(dialogo, nome);
        dialogo.Close();
        await Esperar(200);
        return dialogo;
    }

    void Salvar(Window janela, string nome)
    {
        var elemento = (FrameworkElement)VisualTreeHelper.GetChild(janela, 0);
        var dpi = VisualTreeHelper.GetDpi(elemento);
        var imagem = new RenderTargetBitmap(
            (int)Math.Ceiling(elemento.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(elemento.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        imagem.Render(elemento);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(imagem));
        using var arquivo = File.Create(Path.Combine(_pasta, nome + ".png"));
        png.Save(arquivo);
    }

    static async Task Esperar(int ms)
    {
        await Task.Delay(ms);
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }
}
