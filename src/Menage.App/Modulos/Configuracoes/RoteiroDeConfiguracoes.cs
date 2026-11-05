using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Menage.App.Controles;
using Menage.App.Modulos.Perfis;
using Menage.App.Tema;
using Menage.Core.Armazenamento;
using Menage.Core.Configuracoes;
using Menage.Core.Perfis;
using Microsoft.Extensions.DependencyInjection;

namespace Menage.App.Modulos.Configuracoes;

/// <summary>
/// Modo <c>--galeria-configuracoes &lt;pasta&gt;</c>: abre Configurações e Perfis sobre dados descartáveis (pasta de dados e "Git Manager
/// antigo" de mentira em %TEMP%; nenhuma configuração real é lida nem gravada) e captura as telas nos temas escuro e claro.
/// </summary>
public sealed class RoteiroDeConfiguracoes(
    IServiceProvider servicos, IServicoDeTema tema, ServicoDePerfis perfis, CofreDeCredenciais cofre, PastaDeDados dados)
{
    readonly List<string> _verificacoes = [];
    string _pasta = "";
    string _antigo = "";

    public async Task<int> ExecutarAsync(JanelaDeConfiguracoes janela, string pasta)
    {
        _pasta = pasta;
        Directory.CreateDirectory(pasta);
        _antigo = Path.Combine(Path.GetTempPath(), "gc" + Guid.NewGuid().ToString("N")[..6]);
        try
        {
            PrepararAntigo();
            perfis.Salvar(new PerfilDefinido("", "Revisor de PR", "Consulta e atualiza, sem publicar nada.", PerfilDeUso.Tester,
                ["fetch", "pull", "status", "historico", "buscar-commit", "nexus"]));
            cofre.Guardar(ServicosDeCredencial.Jira, "ana.souza", "token-de-exemplo");
            var vm = (ConfiguracoesViewModel)janela.DataContext;
            vm.DescartarCommand.Execute(null);
            vm.PastaAntiga = _antigo;
            vm.AnalisarCommand.Execute(null);
            foreach (var id in new[] { CatalogoDeTemas.IdEscuro, CatalogoDeTemas.IdClaro }) await CapturarAsync(janela, vm, id);
        }
        catch (Exception ex)
        {
            _verificacoes.Add($"FALHOU: exceção {ex}");
        }
        finally
        {
            if (Directory.Exists(_antigo)) Directory.Delete(_antigo, recursive: true);
            if (Directory.Exists(dados.Caminho)) Directory.Delete(dados.Caminho, recursive: true);
        }

        await File.WriteAllLinesAsync(Path.Combine(pasta, "verificacoes.txt"), _verificacoes);
        return _verificacoes.Exists(v => v.StartsWith("FALHOU", StringComparison.Ordinal)) ? 1 : 0;
    }

    void Conferir(string nome, bool ok) => _verificacoes.Add($"{(ok ? "ok" : "FALHOU")}: {nome}");

    void PrepararAntigo()
    {
        Directory.CreateDirectory(_antigo);
        File.WriteAllText(Path.Combine(_antigo, "perfil.json"), """{"perfil":"dev","perfis":{"tester":{"permitidas":["fetch","pull","status"]}}}""");
        File.WriteAllText(Path.Combine(_antigo, "preferencias.json"), """{"fetchAutomatico":true,"fetchMinutos":15,"ferramentaDiff":"winmerge"}""");
        File.WriteAllText(Path.Combine(_antigo, "protecao.json"), """{"branches":["main","develop","release*","hotfix*"],"validarCommit":true}""");
        File.WriteAllText(Path.Combine(_antigo, "config-geral.cfg"), @"C:\wmw\dev\dev1" + "\r\n");
        File.WriteAllText(Path.Combine(_antigo, "merge.json"), """{"excluirScript":true,"destinos":["develop","release*"]}""");
        File.WriteAllText(Path.Combine(_antigo, "config-update.cfg"), "verificar=1\r\n");
    }

    async Task CapturarAsync(JanelaDeConfiguracoes janela, ConfiguracoesViewModel vm, string id)
    {
        tema.Trocar(id, animar: false);
        await Esperar(400);
        var secoes = new[] { (0, "perfil-de-uso"), (1, "git"), (2, "caminhos"), (3, "jira-credenciais"), (4, "protecao"), (5, "comportamento"), (6, "importar") };
        foreach (var (indice, nome) in secoes)
        {
            janela.Navegacao.SelectedIndex = indice;
            await Esperar(500);
            SalvarPng(janela, $"configuracoes-{nome}-{id}");
        }

        Conferir($"{id}: sete seções na navegação", ConfiguracoesViewModel.Secoes.Count == 7);
        Conferir($"{id}: credencial do Jira aparece como guardada", vm.Credenciais.First(c => c.Servico == "jira") is { Guardada: true, Usuario: "ana.souza" });
        Conferir($"{id}: análise achou os 6 arquivos de exemplo", vm.Achados.Count(a => a.Encontrado) == 6);
        Conferir($"{id}: perfil criado está na lista", vm.Perfis.Any(p => p.Nome == "Revisor de PR"));

        var janelaPerfis = servicos.GetRequiredService<JanelaDePerfis>();
        janelaPerfis.Owner = janela;
        janelaPerfis.ShowActivated = false;
        janelaPerfis.ShowInTaskbar = false;
        janelaPerfis.Show();
        await Esperar(600);
        var pvm = (PerfisViewModel)janelaPerfis.DataContext;
        await pvm.TrocarAsync(pvm.Itens.First(i => i.Nome == "Revisor de PR"));
        await janelaPerfis.Dispatcher.InvokeAsync(() => janelaPerfis.Lista.SelectedItem = pvm.Selecionado);
        await Esperar(500);
        Conferir($"{id}: perfil Revisor tem Push bloqueado e Fetch liberado", pvm.Opcoes.First(o => o.Id == "fetch").Marcada && !pvm.Opcoes.First(o => o.Id == "push").Marcada);
        SalvarPng(janelaPerfis, $"perfis-{id}");
        janelaPerfis.Close();
        await Esperar(300);
    }

    void SalvarPng(Window janela, string nome)
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
