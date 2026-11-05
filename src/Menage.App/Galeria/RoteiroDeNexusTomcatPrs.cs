using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Menage.App.Controles;
using Menage.App.Modulos.Nexus;
using Menage.App.Modulos.PullRequests;
using Menage.App.Modulos.Tomcat;
using Menage.App.Tema;
using Menage.Core.Nexus;
using Menage.Core.PullRequests;
using Menage.Core.Tomcat;
using Microsoft.Extensions.DependencyInjection;

namespace Menage.App.Galeria;

/// <summary>
/// Modo <c>--galeria-nexus-tomcat-prs &lt;pasta&gt;</c>: abre Nexus, Tomcat Express e Pull Requests com dados de exemplo
/// (nenhuma chamada ao Nexus, ao Tomcat, ao GitHub nem ao git) e captura cada janela nos temas escuro e claro.
/// </summary>
public sealed class RoteiroDeNexusTomcatPrs(IServiceProvider servicos, IServicoDeTema tema, NexusViewModel nexus, TomcatViewModel tomcat, PullRequestsViewModel prs)
{
    readonly List<string> _verificacoes = [];

    public async Task<int> ExecutarAsync(string pasta)
    {
        Directory.CreateDirectory(pasta);
        (nexus.ModoExemplo, tomcat.ModoExemplo, prs.ModoExemplo) = (true, true, true);
        try
        {
            var tomcatTemp = Path.Combine(Path.GetTempPath(), "gtb" + Guid.NewGuid().ToString("N")[..6]);
            Directory.CreateDirectory(Path.Combine(tomcatTemp, "bin"));
            await File.WriteAllLinesAsync(BancoDoTomcat.Arquivo(tomcatTemp), ["@echo off", "set \"db_driver=oracle\"", "set \"db_host=10.0.0.5\"", "set \"db_port=1521\"", "set \"db_user=wmw\"", "set \"db_pass=SENHA-CRIPTOGRAFADA\"", "set \"db=wmw\"", "set \"db_host_int=10.0.0.9\""]);
            var janelas = new List<Window> { Abrir<JanelaDoNexus>(), Abrir<JanelaDoTomcat>(), Abrir<JanelaDosPullRequests>() };
            var banco = Abrir<JanelaDoBancoTomcat>();
            banco.Receber(tomcatTemp);
            janelas.Add(banco);
            await Esperar(900);
            Preparar();
            foreach (var id in new[] { CatalogoDeTemas.IdEscuro, CatalogoDeTemas.IdClaro })
            {
                tema.Trocar(id, animar: false);
                await Esperar(500);
                Salvar(janelas[0], $"nexus-{id}", pasta);
                Salvar(janelas[1], $"tomcat-{id}", pasta);
                Salvar(janelas[2], $"pull-requests-{id}", pasta);
                Salvar(janelas[3], $"tomcat-banco-{id}", pasta);
            }

            foreach (var j in janelas) j.Close();
            Directory.Delete(tomcatTemp, recursive: true);
        }
        catch (Exception ex)
        {
            _verificacoes.Add($"FALHOU: exceção {ex}");
        }

        await File.WriteAllLinesAsync(Path.Combine(pasta, "verificacoes.txt"), _verificacoes);
        return _verificacoes.Exists(v => v.StartsWith("FALHOU", StringComparison.Ordinal)) ? 1 : 0;
    }

    T Abrir<T>() where T : Window
    {
        var janela = servicos.GetRequiredService<T>();
        janela.ShowActivated = false;
        janela.ShowInTaskbar = false;
        janela.WindowStartupLocation = WindowStartupLocation.Manual;
        (janela.Left, janela.Top) = (40, 40);
        janela.Show();
        return janela;
    }

    void Conferir(string nome, bool ok) => _verificacoes.Add($"{(ok ? "ok" : "FALHOU")}: {nome}");

    void Preparar()
    {
        var hoje = new DateTime(2026, 11, 4, 9, 30, 0);
        ComponenteNexus C(string nome, string versao, int horas, long war, long jar = 0) =>
            new("releases", "br.com.wmw", nome, versao, "maven2", hoje.AddHours(-horas),
                [.. new[] { new ArquivoNexus($"br/com/wmw/{nome}/{versao}/{nome}-{versao}.war", "http://nexus/x", "war", war), new ArquivoNexus($"br/com/wmw/{nome}/{versao}/{nome}-{versao}.jar", "http://nexus/y", "jar", jar) }.Where(a => a.Tamanho > 0)]);
        nexus.ColocarExemplo(
            [C("wmwvendas", "9.060-13", 2, 87_000_000), C("wmwdados", "9.060-13", 2, 41_200_000), C("wmwvendas", "9.060-5", 30, 86_400_000),
             C("lavenderecore", "9.060-13", 3, 0, 5_100_000), C("wmwdados", "9.060-5", 31, 40_900_000), C("wmwvendas", "9.030", 200, 85_000_000)],
            ["(todos)", "releases", "snapshots", "wmw-internos"],
            [new FavoritoNexus { Nome = "Pacote web", Names = ["wmwvendas", "wmwdados", "lavenderecore"] }, new FavoritoNexus { Nome = "Só vendas", Names = ["wmwvendas"] }]);
        (nexus.Url, nexus.Usuario, nexus.Names, nexus.Versao, nexus.RepositorioEscolhido, nexus.Destino) =
            ("https://nexus.exemplo.wmw/", "usuario.exemplo", "wmwvendas, wmwdados, lavenderecore", "9.060", "(todos)", @"C:\wmw\nexus-downloads");
        nexus.MarcarTodosCommand.Execute(false);
        foreach (var i in nexus.Itens.Take(2)) i.Marcado = true;
        nexus.TemMais = true;
        Conferir("Nexus: 6 componentes e 2 marcados", nexus.Itens.Count == 6 && nexus.Itens.Count(i => i.Marcado) == 2);

        AmbienteTomcat A(string nome, int porta, string vendas, string dados) =>
            new() { Nome = nome, Pasta = $@"C:\wmw\tomcats\{nome}", Porta = porta, InstaladoVendas = vendas, InstaladoDados = dados, Grupo = "br.com.wmw", Repositorio = "releases" };
        tomcat.ColocarExemplo(
            [(A("tomcat-vendas-8082", 8082, "9.060-13", "9.060-13"), SituacaoDoTomcat.NoAr, "wmwvendas: ok   wmwdados: ok"),
             (A("tomcat-teste-8083", 8083, "9.060-5", "9.060-5"), SituacaoDoTomcat.Subindo, "wmwvendas: iniciando   wmwdados: aguardando"),
             (A("tomcat-homolog-8084", 8084, "", ""), SituacaoDoTomcat.Parado, "")],
            ["14:02:11  Validando o ambiente", "14:02:11  Procurando os arquivos no Nexus", "14:02:13  wmwvendas 9.060-3 (66,0 MB) - a mais recente publicada",
             "14:02:13  wmwdados 9.060-3 (34,5 MB) - a mais recente publicada", "14:02:14  Baixando wmwvendas.war e wmwdados.war"]);
        tomcat.Preparando = true;
        Conferir("Tomcat: 3 ambientes", tomcat.Ambientes.Count == 3);

        PullRequest P(int n, string titulo, string autor, string branch, bool? valido, string validacao) =>
            new() { Numero = n, Titulo = titulo, Autor = autor, Branch = branch, Sha = $"sha{n}", ShaValidado = valido == true ? $"sha{n}" : "", Valido = valido, Validacao = validacao, Atualizado = "03/11 16:40" };
        prs.ColocarExemplo(
            [P(41, "Nexus: busca com vários names", "ana", "feature/nexus-busca", true, "ok: mescla sem conflito e compila"),
             P(42, "Tomcat: rollback na troca dos .war", "bruno", "feature/tomcat-rollback", false, "conflito com a main: Directory.Packages.props"),
             P(43, "Diário: filtro por data", "carla", "feature/diario-filtro", null, "")],
            [new LinhaDeLogDoPr(TipoDeLinha.Titulo, "PR #42: Tomcat: rollback na troca dos .war"), new LinhaDeLogDoPr(TipoDeLinha.Discreto, "4 files changed, 120 insertions(+), 8 deletions(-)"),
             new LinhaDeLogDoPr(TipoDeLinha.Erro, "conflito com a main: Directory.Packages.props"), new LinhaDeLogDoPr(TipoDeLinha.Titulo, "PR #41: Nexus: busca com vários names"),
             new LinhaDeLogDoPr(TipoDeLinha.Info, "Merge com a main sem conflito. Compilando (dotnet build Menage.sln -warnaserror)..."), new LinhaDeLogDoPr(TipoDeLinha.Sucesso, "ok: mescla sem conflito e compila")],
            "Fonte: refs de PR do git (sem a API: título vem do último commit)");
        prs.Fontes = @"C:\wmw\menage-wpf";
        prs.ComandoDeValidacao = "dotnet build Menage.sln -warnaserror";
        prs.Prs[0].Marcado = true;
        Conferir("Pull requests: 3 PRs, 1 pronto para mesclar", prs.Prs.Count == 3 && prs.Prs[0].Pr.ProntoParaMesclar && !prs.Prs[1].Pr.ProntoParaMesclar);
    }

    static void Salvar(Window janela, string nome, string pasta)
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
