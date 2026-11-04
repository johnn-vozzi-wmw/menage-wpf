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
public sealed class MouseReal(JanelaDiff janela, string saida, string? filtro = null)
{
    const int Rapido = 260;
    const int Devagar = 2600;

    readonly PainelDiff _p = janela.PainelDoDiff;

    public async Task<object> RodarAsync()
    {
        janela.Activate();
        var resultados = new Dictionary<string, object>();
        foreach (var c in Cenarios().Where(c => filtro is null || c.Nome.Contains(filtro, StringComparison.Ordinal)))
        {
            resultados[c.Nome] = await TentarAsync(c);
        }
        if (filtro is null) resultados["teclado_selecao"] = await new TecladoReal(janela).RodarAsync();
        return resultados;
    }

    /// <summary>Interferência (alguém mexeu no mouse, janela por cima) invalida a medida: repete, e diz quantas vezes.</summary>
    async Task<object> TentarAsync(Cenario c)
    {
        for (var tentativa = 1; ; tentativa++)
        {
            await Preparar(c);
            var (resultado, valeu) = await ArrastarAsync(c);
            if (valeu || tentativa == 5) return new { tentativa, valeu, resultado };
            await Execucao.Esperar(1500);
        }
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
            yield return new($"longas-horizontal-{lado}-rapido", "longas.txt", true, barra, Rapido);
            yield return new($"longas-horizontal-{lado}-devagar", "longas.txt", true, barra, Devagar);
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

    async Task<(object Resultado, bool Valeu)> ArrastarAsync(Cenario c)
    {
        // A barra do WPF UI engrossa com o mouse em cima: o polegar só vale depois disso.
        Tela.MoverMouse(Trilha(c).Origem);
        await Execucao.Esperar(350);
        var (origem, destino) = Trilha(c);
        if (!Tela.PontoEstaNaJanela(janela, origem)) return (new { abortado = "o ponto inicial não está sobre a janela de teste" }, false);
        var registro = new RegistroQuadros(janela, (c.Alvo(_p) as ScrollBar)?.Track.Thumb);
        registro.Ligar();
        var capturas = new List<(double Fracao, BitmapSource Tela, double U)>();
        var (areaEsq, areaDir) = (AreaTexto(_p.Esquerdo), AreaTexto(_p.Direito));
        // Uma cópia só dos dois painéis: em duas cópias a tela pode mudar entre elas.
        var area = new Int32Rect(areaEsq.X, areaEsq.Y, areaDir.X + areaDir.Width - areaEsq.X, areaEsq.Height);
        var pronto = new TaskCompletionSource();
        var limpo = false;
        var fio = new Thread(() =>
        {
            limpo = Dirigir(origem, destino, c.Duracao, f => capturas.Add((f, Tela.Capturar(area), registro.UltimoU)));
            pronto.SetResult();
        }) { IsBackground = true };
        fio.Start();
        await pronto.Task;
        var soltou = DateTime.UtcNow;
        while (_p.Rolagem.Animando) await Execucao.Esperar(5);
        var assentou = (DateTime.UtcNow - soltou).TotalMilliseconds;
        registro.Desligar();
        var preso = registro.PolegarPreso is null or > 0;
        return (new { resumo = registro.Resumo(c.De > c.Para ? -1 : 1), assentouAposSoltarMs = Math.Round(assentou), polegarPreso = registro.PolegarPreso, telas = Analisar(c, capturas, areaDir.X - areaEsq.X, areaEsq.Width) }, limpo && preso);
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
    static bool Dirigir(Point origem, Point destino, double duracaoMs, Action<double> capturar)
    {
        Tela.MoverMouse(origem);
        Thread.Sleep(220); // a barra do WPF UI engrossa ao passar o mouse
        Tela.Apertar();
        Thread.Sleep(40);
        var relogio = System.Diagnostics.Stopwatch.StartNew();
        var proxima = 0.3;
        var limpo = true;
        while (true)
        {
            var f = Math.Min(1, relogio.Elapsed.TotalMilliseconds / duracaoMs);
            var ponto = new Point(origem.X + ((destino.X - origem.X) * f), origem.Y + ((destino.Y - origem.Y) * f));
            Tela.MoverMouse(ponto);
            if (f >= proxima)
            {
                capturar(f);
                proxima += 0.3;
            }
            if (f >= 1) break;
            Thread.Sleep(4);
            if ((Tela.PosicaoDoMouse() - ponto).Length > 3) limpo = false; // a posição mudou sem ser o roteiro
        }
        Thread.Sleep(60);
        Tela.Soltar();
        return limpo;
    }

    /// <summary>
    /// Compara, pixel a pixel, as duas faixas de texto em volta da linha de sincronia: em trecho igual elas têm que
    /// ser idênticas. Painel atrasado ou conteúdo pulando aparece como linhas de pixel diferentes.
    /// </summary>
    List<object> Analisar(Cenario c, List<(double Fracao, BitmapSource Tela, double U)> capturas, int deslocamentoDireita, int largura)
    {
        var saidas = new List<object>();
        var maximo = _p.Rolagem.Mapa!.Maximo;
        var h = (int)_p.Direito.AlturaLinha;
        for (var i = 0; i < capturas.Count; i++)
        {
            var (fracao, tela, u) = capturas[i];
            var sincronia = (int)(maximo > 0 ? u / maximo * tela.PixelHeight : 0);
            var iguais = ComparadorDePixels.LinhasIguais(tela, deslocamentoDireita, largura, Math.Max(0, sincronia - (4 * h)), Math.Min(tela.PixelHeight, sincronia + (4 * h)));
            saidas.Add(new { fracao = Math.Round(fracao, 2), linhasDePixelIguais = Math.Round(iguais, 3) });
            if (i == 1 && c.Nome.Contains("rapido", StringComparison.Ordinal) && !c.Nome.Contains("volta", StringComparison.Ordinal))
            {
                Tela.Salvar(tela, Path.Combine(saida, "arraste", c.Nome + ".png"));
            }
        }
        return saidas;
    }
}

public static class ComparadorDePixels
{
    /// <summary>Fração de linhas de pixel em que a faixa da esquerda é idêntica à da direita, no intervalo [inicio, fim).</summary>
    public static double LinhasIguais(BitmapSource tela, int deslocamentoDireita, int largura, int inicio, int fim)
    {
        var pixels = Pixels(tela);
        var passo = tela.PixelWidth * 4;
        var iguais = 0;
        for (var y = inicio; y < fim; y++)
        {
            if (pixels.AsSpan(y * passo, largura * 4).SequenceEqual(pixels.AsSpan((y * passo) + (deslocamentoDireita * 4), largura * 4))) iguais++;
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
