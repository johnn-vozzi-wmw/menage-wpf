using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Menage.App.Animacoes;
using Menage.App.Controles;
using Menage.App.Modulos.HistoricoReleases;
using Menage.App.Modulos.JanelaPrincipal;
using Menage.App.Tema;
using Menage.Core.Armazenamento;
using Menage.Core.Interacao;
using Menage.Core.LinhaDoTempo;
using Menage.Core.Personalizacao;

namespace Menage.App.Modulos.Personalizacao;

/// <summary>
/// Modo <c>--galeria-personalizacao &lt;pasta&gt;</c>: abre a janela principal com dados de exemplo, muda as preferências ao vivo
/// (como o usuário faria) e captura antes/depois, a janela de Personalização e o Histórico de releases, em claro e escuro.
/// Usa pasta de dados temporária e logs de exemplo; nada do usuário é lido ou gravado.
/// </summary>
public sealed class RoteiroDePersonalizacao(
    IServicoDeTema tema, ServicoDeAparencia aparencia, ServicoDeAtalhos atalhos, IServicoDeJanelas janelas, ServicoDeTransicao transicao,
    PastaDeDados dados, JanelaPrincipalViewModel modelo)
{
    readonly List<string> _verificacoes = [];
    string PastaDosLogs => Path.Combine(dados.Caminho, "gerador-beta", "logs");
    string _pasta = "";

    public async Task<int> ExecutarAsync(JanelaPrincipal.JanelaPrincipal janela, string pasta)
    {
        _pasta = pasta;
        Directory.CreateDirectory(pasta);
        try
        {
            if (Directory.Exists(PastaDosLogs)) Directory.Delete(PastaDosLogs, recursive: true);
            CriarLogsDeExemplo(PastaDosLogs);
            await Esperar(900);
            await CapturarAntesEDepoisAsync(janela);
            foreach (var id in new[] { CatalogoDeTemas.IdEscuro, CatalogoDeTemas.IdClaro })
            {
                tema.Trocar(id, animar: false);
                await Esperar(300);
                await CapturarPersonalizacaoAsync(id);
                await CapturarReleasesAsync(id);
            }

            await ConferirAtalhosAsync(janela);
        }
        catch (Exception ex)
        {
            _verificacoes.Add($"FALHOU: exceção {ex}");
        }

        await File.WriteAllLinesAsync(Path.Combine(pasta, "verificacoes.txt"), _verificacoes);
        return _verificacoes.Exists(v => v.StartsWith("FALHOU", StringComparison.Ordinal)) ? 1 : 0;
    }

    void Conferir(string nome, bool ok) => _verificacoes.Add($"{(ok ? "ok" : "FALHOU")}: {nome}");

    static double Recurso(string chave) => (double)Application.Current.Resources[chave];

    static double Escala(Window janela) => ((ScaleTransform)janela.FindResource("Interface.Escala")).ScaleX;

    async Task CapturarAntesEDepoisAsync(JanelaPrincipal.JanelaPrincipal janela)
    {
        tema.Trocar(CatalogoDeTemas.IdEscuro, animar: false);
        aparencia.Alterar(p => new PreferenciasDeAparencia { Modo = ModoDeTema.Escuro });
        modelo.BarraLateral.AlternarGrupo(modelo.BarraLateral.Grupos.First(g => g.Id == "WEB"), somar: false);
        await Esperar(500);
        var alturaPadrao = Recurso("Altura.Controle");
        Conferir($"padrão: controle de 32 e zoom 100% ({alturaPadrao}, {Escala(janela)})", alturaPadrao == 32 && Escala(janela) == 1);
        SalvarJanela(janela, "principal-padrao-escuro");

        aparencia.Alterar(p => p with
        {
            CorDeDestaque = "#7C3AED", Densidade = Densidade.Compacta, Fonte = TamanhoDeFonte.Grande, Barra = ModeloDaBarra.B, ReduzirMovimento = true,
        });
        await Esperar(700);
        Conferir($"ao vivo: fonte grande = zoom de 115% no conteúdo ({Escala(janela)})", Math.Abs(Escala(janela) - 1.15) < 0.001);
        Conferir($"ao vivo: densidade compacta = controle de 26 ({Recurso("Altura.Controle")})", Recurso("Altura.Controle") == 26);
        Conferir("ao vivo: barra no modelo B", modelo.BarraLateral.ModeloB);
        var destaque = (SolidColorBrush)janela.FindResource("Tema.Destaque");
        Conferir($"ao vivo: destaque violeta no pincel do tema ({destaque.Color})", destaque.Color == Color.FromRgb(0x7C, 0x3A, 0xED));
        Conferir("ao vivo: reduzir movimento chega ao serviço de transição", transicao.ReduzirMovimento);
        SalvarJanela(janela, "principal-personalizado-escuro");

        aparencia.Alterar(p => p with { Modo = ModoDeTema.Claro });
        await Esperar(700);
        Conferir("modo claro troca o tema sem reiniciar", !tema.Atual.Escuro);
        Conferir("o destaque do usuário continua no tema claro", ((SolidColorBrush)janela.FindResource("Tema.Destaque")).Color == Color.FromRgb(0x7C, 0x3A, 0xED));
        SalvarJanela(janela, "principal-personalizado-claro");
    }

    async Task CapturarPersonalizacaoAsync(string id)
    {
        janelas.Abrir(JanelaDePersonalizacao.Id);
        await Esperar(800);
        var janela = Application.Current.Windows.OfType<JanelaDePersonalizacao>().First();
        PrepararCaptura(janela);
        SalvarJanela(janela, $"personalizacao-aparencia-{id}");

        janela.Abas.SelectedIndex = 2;
        await CapturarIconesAsync(janela, id);

        janela.Abas.SelectedIndex = 1;
        janela.Modelo.FiltroDeAtalhos = "";
        await Esperar(300);
        janela.Modelo.Selecionada = janela.Modelo.Atalhos.FirstOrDefault(l => l.Id == "tema.alternar");
        janela.Modelo.DefinirAtalho(new Gesto(true, true, false, "T"));
        await Esperar(500);
        Conferir($"{id}: Ctrl+Shift+T gravado como atalho do tema ({atalhos.Mapa.Efetivo("tema.alternar")})", atalhos.Mapa.Efetivo("tema.alternar") == "Ctrl+Shift+T");
        janela.Modelo.Selecionada = janela.Modelo.Atalhos.FirstOrDefault(l => l.Id == "janela.atualizar");
        janela.Modelo.DefinirAtalho(new Gesto(true, false, false, "K"));
        await Esperar(300);
        Conferir($"{id}: Ctrl+K em uso é recusado com o nome do dono ({janela.Modelo.MensagemDeAtalho})", janela.Modelo.MensagemDeAtalho.Contains("Abrir a paleta", StringComparison.Ordinal));
        janela.Modelo.Selecionada = janela.Modelo.Atalhos.FirstOrDefault(l => l.Id == "tema.alternar");
        await Esperar(300);
        SalvarJanela(janela, $"personalizacao-atalhos-{id}");
        janela.Modelo.RestaurarTodosOsAtalhosCommand.Execute(null);
        janela.Close();
        await Esperar(500);
    }

    // Aba Ícones (passo a passo): antes (ícone padrão) e depois (imagem gerada, zoom 130%, "Usar em todos" e caixas marcadas).
    async Task CapturarIconesAsync(JanelaDePersonalizacao janela, string id)
    {
        aparencia.Alterar(p => p with { Fonte = TamanhoDeFonte.Normal }); // a captura anterior deixou "Grande" (115%), que empilharia as colunas
        var icones = janela.Icones;
        icones.TemaSelecionado = icones.Temas.First(t => t.Id == id);
        await Esperar(400);
        SalvarJanela(janela, $"personalizacao-icones-padrao-{id}");

        var arquivo = GerarIconeDeExemplo(Path.Combine(_pasta, "icone-exemplo.png"));
        icones.ReceberArquivo(arquivo);
        icones.ZoomPercentual = 130;
        icones.AplicarNoMenuEPilulas = true;
        await Esperar(500);
        Conferir($"{id}: ícone escolhido aparece no quadro e o zoom fica em 130% ({icones.ZoomTexto})", icones.Imagem is not null && icones.ZoomPercentual == 130 && icones.TemaSelecionado.TemIcone);
        SalvarJanela(janela, $"personalizacao-icones-escolhido-{id}");

        icones.UsarEmTodosCommand.Execute(null);
        Conferir($"{id}: usar em todos acende o ponto de todos os temas", icones.Temas.Where(t => t.Id != IconesPersonalizados.TodosOsTemas).All(t => t.TemIcone));
        icones.TemaSelecionado = icones.Temas[^1];
        await Esperar(400);
        SalvarJanela(janela, $"personalizacao-icones-todos-{id}");

        icones.TemaSelecionado = icones.Temas.First(t => t.Id == id);
        foreach (var chip in icones.Temas.Where(t => t.Id != IconesPersonalizados.TodosOsTemas)) { icones.TemaSelecionado = chip; icones.RemoverCommand.Execute(null); }
        icones.AplicarNoMenuEPilulas = false;
    }

    static string GerarIconeDeExemplo(string caminho)
    {
        const int lado = 128;
        var pixels = new byte[lado * lado * 4];
        for (var y = 0; y < lado; y++)
            for (var x = 0; x < lado; x++)
            {
                var i = (y * lado + x) * 4;
                var borda = x < 6 || y < 6 || x >= lado - 6 || y >= lado - 6;
                (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = borda ? ((byte)255, (byte)255, (byte)255, (byte)255) : ((byte)(40 + x), (byte)(200 - y), (byte)(220 - x), (byte)255);
            }

        var codificador = new PngBitmapEncoder();
        codificador.Frames.Add(BitmapFrame.Create(BitmapSource.Create(lado, lado, 96, 96, PixelFormats.Bgra32, null, pixels, lado * 4)));
        using var fluxo = File.Create(caminho);
        codificador.Save(fluxo);
        return caminho;
    }

    // Deixa a captura da aba Aparência com a aba certa e sem foco em campo.
    static void PrepararCaptura(JanelaDePersonalizacao janela) => janela.Abas.SelectedIndex = 0;

    async Task CapturarReleasesAsync(string id)
    {
        var logs = PastaDosLogs;


        janelas.Abrir(JanelaDeReleases.Id);
        await Esperar(900);
        var janela = Application.Current.Windows.OfType<JanelaDeReleases>().First();
        var eventos = LeitorDeBetas.Ler(logs, []).Concat(TagsDeExemplo()).OrderByDescending(e => e.Data).ToList();
        janela.Modelo.Mostrar(eventos);
        await Esperar(500);
        Conferir($"{id}: Histórico lê {eventos.Count} eventos (3 betas e 2 tags) dos logs de exemplo", janela.Modelo.Linhas.Count == 5);
        Conferir($"{id}: ambientes na lista: {string.Join(", ", janela.Modelo.Ambientes)}", janela.Modelo.Ambientes.Contains("dev1") && janela.Modelo.Ambientes.Contains("dev2"));
        SalvarJanela(janela, $"historico-releases-{id}");

        janela.Modelo.Ambiente = "dev2";
        janela.Modelo.Tags = false;
        await Esperar(300);
        Conferir($"{id}: filtro ambiente dev2 sem tags mostra só a beta dele ({janela.Modelo.Linhas.Count})", janela.Modelo.Linhas.Count == 1);
        janela.Close();
        await Esperar(500);
    }

    async Task ConferirAtalhosAsync(JanelaPrincipal.JanelaPrincipal janela)
    {
        aparencia.Alterar(p => p with { Atalhos = new Dictionary<string, string> { ["janela.barra-lateral"] = "Ctrl+Shift+B", ["janela.atualizar"] = "" } });
        await Esperar(200);
        Conferir("atalho trocado vale na hora (Ctrl+B some, Ctrl+Shift+B ganha)",
            atalhos.Mapa.ComandoDe(new Gesto(true, false, false, "B")) is null && atalhos.Mapa.ComandoDe(new Gesto(true, true, false, "B")) == "janela.barra-lateral");
        Conferir("F5 removido de Atualizar fica sem comando", atalhos.Mapa.ComandoDe(new Gesto(false, false, false, "F5")) is null);
        Conferir("Ctrl+K segue abrindo a paleta", atalhos.Mapa.ComandoDe(new Gesto(true, false, false, "K")) == "paleta.abrir");
        aparencia.Alterar(p => p with { Atalhos = [] });
        _ = janela;
    }

    static IEnumerable<EventoDeRelease> TagsDeExemplo() =>
    [
        new(DateTime.Now.AddDays(-3), TipoDeEvento.Tag, "Tag v5.0.12", "lavenderecore @a1b2c3d4  \"Fecha release 5.0.12\"   |   lavenderedados @e5f6a7b8   |   lavendereweb @c9d0e1f2", "", EstadoDoEvento.Ok, ["lavenderecore", "lavenderedados", "lavendereweb"]),
        new(DateTime.Now.AddDays(-9), TipoDeEvento.Tag, "Tag v5.0.11", "lavenderecore @0a1b2c3d  \"Fecha release 5.0.11\"", "", EstadoDoEvento.Ok, ["lavenderecore"]),
    ];

    static void CriarLogsDeExemplo(string logs)
    {
        void Log(string geracao, string arquivo, string projeto, string pasta, string branch, string? rodape)
        {
            var dir = Directory.CreateDirectory(Path.Combine(logs, geracao)).FullName;
            var linhas = new List<string>
            {
                new('=', 100), $"Projeto  : {projeto}", $"Pasta    : {pasta}", $"Branch   : {branch}",
                "Comando  : mvn -B clean install", "Inicio   : 04/11/2026 10:00:00", new('=', 100), "[INFO] BUILD",
            };
            if (rodape is not null) linhas.AddRange([new string('=', 100), rodape, new string('=', 100)]);
            File.WriteAllLines(Path.Combine(dir, arquivo), linhas, Encoding.Latin1);
        }

        var agora = DateTime.Now;
        string Nome(TimeSpan atras, string modo) => $"{agora - atras:yyyy-MM-dd_HH-mm-ss}_{modo}";
        var g1 = Nome(TimeSpan.FromHours(2), "install");
        Log(g1, "01-lavenderecore.log", "lavenderecore", @"C:\ambientes\dev1\lavenderecore", "develop", "RESULTADO: SUCESSO - lavenderecore em 02:31");
        Log(g1, "02-lavenderedados.log", "lavenderedados", @"C:\ambientes\dev1\lavenderedados", "develop", "RESULTADO: SUCESSO - lavenderedados em 01:09");
        Log(g1, "03-lavendereweb.log", "lavendereweb", @"C:\ambientes\dev1\web\lavendereweb", "develop", "RESULTADO: SUCESSO - lavendereweb em 03:44");
        var g2 = Nome(TimeSpan.FromDays(1.2), "deploy");
        Log(g2, "01-lavenderepda.log", "lavenderepda", @"C:\ambientes\dev2\pda\lavenderepda", "release/5.0", "RESULTADO: ERRO (exit 1) - lavenderepda em 00:48");
        Log(g2, "01-lavenderepda-surefire.log", "lavenderepda  (repetição: correção do surefire)", @"C:\ambientes\dev2\pda\lavenderepda", "release/5.0", "RESULTADO: ERRO (exit 1) - lavenderepda em 01:02");
        var g3 = Nome(TimeSpan.FromDays(5), "install");
        Log(g3, "01-lavenderecore.log", "lavenderecore", @"C:\ambientes\dev1\lavenderecore", "feature/VENDAS-123", "RESULTADO: SUCESSO - lavenderecore em 02:10");
        Log(g3, "02-lavenderedados.log", "lavenderedados", @"C:\ambientes\dev1\lavenderedados", "feature/VENDAS-123", null);
    }

    void SalvarJanela(Window janela, string nome) => SalvarPng(janela, nome, _pasta);

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
