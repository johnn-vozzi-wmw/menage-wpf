using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Menage.App.Modulos.JanelaPrincipal;
using Menage.App.Tema;
using Menage.Core.Saida;

namespace Menage.App.Galeria;

/// <summary>
/// Modo <c>--galeria-principal &lt;pasta&gt;</c>: abre a janela principal com dados de exemplo (nenhum repositório real), tira as capturas
/// (claro/escuro, barra expandida/recolhida, Saída aberta/recolhida) e grava em <c>verificacoes.txt</c> o que foi medido.
/// </summary>
public sealed class RoteiroDaJanelaPrincipal(IServicoDeTema tema, SaidaDeOperacoes saida, JanelaPrincipalViewModel modelo, Modulos.OperacoesLote.RoteiroDeOperacoesLote lote, RoteiroDeParidadeGit paridade, RoteiroDeIconesELayouts iconesELayouts)
{
    readonly List<string> _verificacoes = [];
    JanelaPrincipal _janela = null!;
    string _pasta = "";

    public async Task<int> ExecutarAsync(JanelaPrincipal janela, string pasta)
    {
        (_janela, _pasta) = (janela, pasta);
        Directory.CreateDirectory(pasta);
        try
        {
            await Esperar(900);
            foreach (var id in new[] { CatalogoDeTemas.IdEscuro, CatalogoDeTemas.IdClaro }) await CapturarTemaAsync(id);
            _verificacoes.AddRange(await lote.ExecutarAsync(_janela, pasta));
            _verificacoes.AddRange(await paridade.ExecutarAsync(_janela, pasta));
            _verificacoes.AddRange(await iconesELayouts.ExecutarAsync(_janela, pasta, Salvar, Esperar));
        }
        catch (Exception ex)
        {
            _verificacoes.Add($"FALHOU: exceção {ex}");
        }

        await File.WriteAllLinesAsync(Path.Combine(pasta, "verificacoes.txt"), _verificacoes);
        return _verificacoes.Exists(v => v.StartsWith("FALHOU", StringComparison.Ordinal)) ? 1 : 0;
    }

    void Conferir(string nome, bool ok) => _verificacoes.Add($"{(ok ? "ok" : "FALHOU")}: {nome}");

    async Task CapturarTemaAsync(string id)
    {
        tema.Trocar(id, animar: false);
        await Esperar(300);
        PrepararSaida();
        var barra = modelo.BarraLateral;
        var web = barra.Grupos.First(g => g.Id == "WEB");
        barra.AlternarGrupo(web, somar: false);
        await Esperar(500);
        Conferir($"{id}: clicar no grupo WEB marca os 3 projetos do grupo ({web.Texto})", web.Completo && modelo.Grade.QuantidadeMarcada == 3);
        Conferir($"{id}: as pílulas ficam habilitadas com projetos marcados", modelo.Acoes.Acoes[0].Habilitada);
        Conferir($"{id}: Commit fica habilitado com projetos marcados", modelo.Acoes.Acoes[4].Habilitada);
        var console = (SolidColorBrush)_janela.FindResource("Tema.Console");
        Conferir($"{id}: Saída preta", console.Color == Colors.Black);
        Conferir($"{id}: o cartão Configurações entra no Tab e tem nome para leitor de tela", _janela.FindName("CartaoConfiguracoes") is System.Windows.Controls.Border { Focusable: true } cartao && System.Windows.Automation.AutomationProperties.GetName(cartao) == "Configurações");
        Conferir($"{id}: o botão da Saída tem nome acessível", System.Windows.Automation.AutomationProperties.GetName((DependencyObject)_janela.FindName("BotaoSaida")).Length > 0);
        Salvar($"principal-{id}-expandida");

        await RecolherBarraAsync(id);
        modelo.Saida.Alternar();
        await Esperar(300);
        Conferir($"{id}: Saída recolhida dá o resto à grade", modelo.Saida.AlturaDaSaida <= 40 && modelo.Saida.AlturaDaGrade > 300);
        Salvar($"principal-{id}-saida-recolhida");
        modelo.Saida.Alternar();
        await Esperar(300);

        _janela.AlternarBarra();
        await Esperar(500);
        modelo.Grade.Busca = "zzz";
        await Esperar(300);
        Conferir($"{id}: busca sem resultado mostra o estado vazio", modelo.Grade.SemLinhas && modelo.Grade.TituloVazio.Contains("zzz"));
        Salvar($"principal-{id}-vazio");
        modelo.Grade.Busca = "";
        modelo.BarraLateral.AlternarGrupo(web, somar: false);
        await Esperar(300);
    }

    async Task RecolherBarraAsync(string id)
    {
        var passes = 0;
        void AoAtualizar(object? o, EventArgs e) => passes++;
        _janela.LayoutUpdated += AoAtualizar;
        _janela.AlternarBarra();
        await Esperar(600);
        _janela.LayoutUpdated -= AoAtualizar;
        Conferir($"{id}: recolher a barra gasta poucos passes de layout ({passes} em ~600 ms, ~36 quadros)", passes is > 0 and <= 12);
        Conferir($"{id}: a coluna fica no trilho de 56 px ({_janela.ColunaDaBarra:0} px)", Math.Abs(_janela.ColunaDaBarra - 56) < 1);
        Salvar($"principal-{id}-barra-recolhida");
    }

    void PrepararSaida()
    {
        saida.Limpar();
        saida.Escrever("> Fetch", TipoDeLinha.Comando);
        saida.Escrever("[1/3] lavenderecore...", TipoDeLinha.Fraco);
        saida.Escrever("== Fetch: 2 ok, 1 com aviso, 0 com erro ==", TipoDeLinha.Titulo);
        saida.Escrever("  ✓ lavenderecore: Atualizado", TipoDeLinha.Ok);
        saida.Escrever("  ✓ lavendereweb: Já estava atualizado", TipoDeLinha.Ok);
        saida.Escrever("  ! lavenderedados: Sem remoto configurado", TipoDeLinha.Aviso);
        saida.Escrever("Texto comum de saída do git.");
    }

    void Salvar(string nome)
    {
        var elemento = (FrameworkElement)VisualTreeHelper.GetChild(_janela, 0);
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
