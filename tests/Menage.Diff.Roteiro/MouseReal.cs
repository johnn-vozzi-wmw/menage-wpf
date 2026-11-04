using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using Menage.App.Modulos.Diff;

namespace Menage.Diff.Roteiro;

/// <summary>Um arraste: o que pegar, de onde a onde (frações da trilha) e em quanto tempo.</summary>
public sealed record Cenario(string Nome, string Arquivo, bool Expandido, Func<PainelDiff, FrameworkElement> Alvo, double Duracao, double De = 0, double Para = 1);

/// <summary>
/// Arrasta com o mouse de verdade (SetCursorPos + mouse_event numa thread auxiliar, com a janela de teste rodando) e
/// registra cada quadro. Todo aperto de botão confere antes que o ponto está sobre a janela criada pelo teste.
/// </summary>
public sealed class MouseReal(JanelaDiff janela, string saida)
{
    const int Rapido = 260;
    const int Devagar = 2600;

    readonly PainelDiff _p = janela.PainelDoDiff;

    public async Task<object> RodarAsync()
    {
        janela.Activate();
        var resultados = new Dictionary<string, object>();
        foreach (var c in Cenarios())
        {
            await Preparar(c);
            resultados[c.Nome] = await ArrastarAsync(c);
        }
        resultados["teclado_selecao"] = await new TecladoReal(janela).RodarAsync();
        return resultados;
    }

    static IEnumerable<Cenario> Cenarios()
    {
        foreach (var (arquivo, expandido, tag) in new[] { ("alinhamento.txt", true, "alinhamento"), ("grande.java", false, "grande-recolhido"), ("grande.java", true, "grande-expandido"), ("so-direita.java", false, "so-direita") })
        {
            foreach (var (lado, barra) in new (string, Func<PainelDiff, FrameworkElement>)[] { ("esq", p => p.BarraVerticalEsquerda), ("dir", p => p.BarraVerticalDireita) })
            {
                yield return new($"{tag}-barra-{lado}-rapido", arquivo, expandido, barra, Rapido);
                yield return new($"{tag}-barra-{lado}-rapido-volta", arquivo, expandido, barra, Rapido, 1, 0);
                yield return new($"{tag}-barra-{lado}-devagar", arquivo, expandido, barra, Devagar);
            }
            yield return new($"{tag}-regua-rapido", arquivo, expandido, p => p.ReguaDoArquivo, Rapido);
        }
        foreach (var (lado, barra) in new (string, Func<PainelDiff, FrameworkElement>)[] { ("esq", p => p.BarraHorizontalEsquerda), ("dir", p => p.BarraHorizontalDireita) })
        {
            yield return new($"longas-horizontal-{lado}-rapido", "longas.txt", false, barra, Rapido);
            yield return new($"longas-horizontal-{lado}-devagar", "longas.txt", false, barra, Devagar);
        }
    }

    async Task Preparar(Cenario c)
    {
        if (janela.ListaDeArquivos.Selecionado?.Caminho != c.Arquivo)
        {
            await janela.RecarregarAsync(c.Arquivo);
            await Execucao.Esperar(700);
        }
        _p.DefinirChaves(recolher: !c.Expandido, ignorar: true);
        await Execucao.Esperar(200);
        var alvoEhHorizontal = c.Alvo(_p) is ScrollBar { Orientation: System.Windows.Controls.Orientation.Horizontal };
        var mapa = _p.Rolagem.Mapa!;
        var inicioNoFim = c.De > c.Para;
        _p.Rolagem.IrParaImediato(0, !alvoEhHorizontal && inicioNoFim ? mapa.Maximo : 0);
        await Execucao.Esperar(250);
    }

    async Task<object> ArrastarAsync(Cenario c)
    {
        var (origem, destino) = Trilha(c);
        if (!Tela.PontoEstaNaJanela(janela, origem)) return new { abortado = "o ponto inicial não está sobre a janela de teste" };
        var registro = new RegistroQuadros(janela);
        registro.Ligar();
        var capturas = new List<(double Fracao, BitmapSource Esq, BitmapSource Dir, double U)>();
        var (areaEsq, areaDir) = (AreaTexto(_p.Esquerdo), AreaTexto(_p.Direito));
        var pronto = new TaskCompletionSource();
        var fio = new Thread(() =>
        {
            Dirigir(origem, destino, c.Duracao, f => capturas.Add((f, Tela.Capturar(areaEsq), Tela.Capturar(areaDir), registro.UltimoU)));
            pronto.SetResult();
        }) { IsBackground = true };
        fio.Start();
        await pronto.Task;
        var soltou = DateTime.UtcNow;
        while (_p.Rolagem.Animando) await Execucao.Esperar(5);
        var assentou = (DateTime.UtcNow - soltou).TotalMilliseconds;
        registro.Desligar();
        return new { resumo = registro.Resumo(c.De > c.Para ? -1 : 1), assentouAposSoltarMs = Math.Round(assentou), telas = Analisar(c, capturas) };
    }

    (Point Origem, Point Destino) Trilha(Cenario c)
    {
        var alvo = c.Alvo(_p);
        if (alvo is not ScrollBar barra)
        {
            var (a, b) = (alvo.PointToScreen(new Point(alvo.ActualWidth / 2, alvo.ActualHeight * Math.Max(0.02, c.De))), alvo.PointToScreen(new Point(alvo.ActualWidth / 2, alvo.ActualHeight * Math.Min(0.98, c.Para))));
            return (a, b);
        }
        var trilho = barra.Track;
        var polegar = trilho.Thumb;
        var centro = polegar.PointToScreen(new Point(polegar.ActualWidth / 2, polegar.ActualHeight / 2));
        var horizontal = barra.Orientation == System.Windows.Controls.Orientation.Horizontal;
        var extensao = (horizontal ? trilho.ActualWidth - polegar.ActualWidth : trilho.ActualHeight - polegar.ActualHeight) * (c.Para - c.De);
        return (centro, horizontal ? new Point(centro.X + extensao, centro.Y) : new Point(centro.X, centro.Y + extensao));
    }

    static Int32Rect AreaTexto(App.Modulos.Diff.Texto.PainelTexto painel)
    {
        var vista = painel.Vista;
        var canto = vista.PointToScreen(new Point(0, 0));
        return new((int)canto.X, (int)canto.Y, (int)Math.Min(420, vista.ActualWidth), (int)vista.ActualHeight);
    }

    /// <summary>Roda na thread auxiliar: a thread de UI fica livre, como quando uma pessoa arrasta.</summary>
    static void Dirigir(Point origem, Point destino, double duracaoMs, Action<double> capturar)
    {
        Tela.MoverMouse(origem);
        Thread.Sleep(220); // a barra do WPF UI engrossa ao passar o mouse
        Tela.Apertar();
        Thread.Sleep(40);
        var relogio = System.Diagnostics.Stopwatch.StartNew();
        var proxima = 0.3;
        while (true)
        {
            var f = Math.Min(1, relogio.Elapsed.TotalMilliseconds / duracaoMs);
            Tela.MoverMouse(new Point(origem.X + ((destino.X - origem.X) * f), origem.Y + ((destino.Y - origem.Y) * f)));
            if (f >= proxima)
            {
                capturar(f);
                proxima += 0.3;
            }
            if (f >= 1) break;
            Thread.Sleep(4);
        }
        Thread.Sleep(60);
        Tela.Soltar();
    }

    /// <summary>
    /// Compara, pixel a pixel, as duas faixas de texto em volta da linha de sincronia: em trecho igual elas têm que
    /// ser idênticas. Painel atrasado ou conteúdo pulando aparece como linhas de pixel diferentes.
    /// </summary>
    List<object> Analisar(Cenario c, List<(double Fracao, BitmapSource Esq, BitmapSource Dir, double U)> capturas)
    {
        var saidas = new List<object>();
        var maximo = _p.Rolagem.Mapa!.Maximo;
        var h = (int)_p.Direito.AlturaLinha;
        for (var i = 0; i < capturas.Count; i++)
        {
            var (fracao, esq, dir, u) = capturas[i];
            var sincronia = (int)(maximo > 0 ? u / maximo * esq.PixelHeight : 0);
            var iguais = ComparadorDePixels.LinhasIguais(esq, dir, Math.Max(0, sincronia - (4 * h)), Math.Min(esq.PixelHeight, sincronia + (4 * h)));
            saidas.Add(new { fracao = Math.Round(fracao, 2), linhasDePixelIguais = Math.Round(iguais, 3) });
            if (i == 1 && c.Nome.Contains("rapido", StringComparison.Ordinal) && !c.Nome.Contains("volta", StringComparison.Ordinal))
            {
                Tela.Salvar(esq, Path.Combine(saida, "arraste", c.Nome + "-esq.png"));
                Tela.Salvar(dir, Path.Combine(saida, "arraste", c.Nome + "-dir.png"));
            }
        }
        return saidas;
    }
}

public static class ComparadorDePixels
{
    /// <summary>Fração de linhas de pixel idênticas nas duas imagens, no intervalo [inicio, fim).</summary>
    public static double LinhasIguais(BitmapSource a, BitmapSource b, int inicio, int fim)
    {
        var largura = Math.Min(a.PixelWidth, b.PixelWidth);
        var (pa, pb) = (Pixels(a), Pixels(b));
        var (passoA, passoB) = (a.PixelWidth * 4, b.PixelWidth * 4);
        var iguais = 0;
        for (var y = inicio; y < fim; y++)
        {
            if (pa.AsSpan(y * passoA, largura * 4).SequenceEqual(pb.AsSpan(y * passoB, largura * 4))) iguais++;
        }
        return fim > inicio ? iguais / (double)(fim - inicio) : 1;
    }

    static byte[] Pixels(BitmapSource imagem)
    {
        var bytes = new byte[imagem.PixelWidth * imagem.PixelHeight * 4];
        imagem.CopyPixels(bytes, imagem.PixelWidth * 4, 0);
        return bytes;
    }
}
