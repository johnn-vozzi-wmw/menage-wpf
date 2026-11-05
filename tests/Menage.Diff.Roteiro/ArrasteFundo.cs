using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using Menage.App.Modulos.Diff;
using Menage.App.Modulos.Diff.Rolagem;
using Menage.App.Modulos.Diff.Texto;

namespace Menage.Diff.Roteiro;

/// <summary>
/// Os mesmos arrastes do <see cref="MouseReal"/>, em segundo plano: em vez do cursor, dispara o evento Scroll que a
/// barra dispara quando o polegar é arrastado, então do controlador em diante o caminho é o mesmo, sem tocar no
/// mouse nem no foco do usuário. O que isto não exercita é só o polegar da barra pegando o mouse.
/// </summary>
public sealed class ArrasteFundo(JanelaDiff janela, string saida, string? filtro)
{
    const int PassoMs = 8;

    readonly PainelDiff _p = janela.PainelDoDiff;

    public async Task<object> RodarAsync()
    {
        var resultados = new Dictionary<string, object>();
        foreach (var c in MouseReal.Cenarios().Where(c => filtro is null || c.Nome.Contains(filtro, StringComparison.Ordinal)))
        {
            await MouseReal.PrepararAsync(janela, c);
            resultados[c.Nome] = await ArrastarAsync(c);
        }
        return resultados;
    }

    async Task<object> ArrastarAsync(Cenario c)
    {
        var registro = new RegistroQuadros(janela);
        registro.Ligar();
        var alvo = c.Alvo(_p);
        var telas = new List<object>();
        var relogio = Stopwatch.StartNew();
        var proxima = 0.3;
        while (true)
        {
            var f = Math.Min(1, relogio.Elapsed.TotalMilliseconds / c.Duracao);
            Mover(alvo, c.De + ((c.Para - c.De) * f));
            await Task.Delay(PassoMs);
            if (f >= proxima && _p.Esquerdo.Linhas.Quantas > 0)
            {
                telas.Add(Comparar(c, f, telas.Count));
                proxima += 0.3;
            }
            if (f >= 1) break;
        }
        var soltou = Stopwatch.StartNew();
        while (_p.Rolagem.Animando) await Execucao.Esperar(5);
        registro.Desligar();
        return new { resumo = registro.Resumo(c.De > c.Para ? -1 : 1), assentouAposSoltarMs = soltou.ElapsedMilliseconds, telas };
    }

    void Mover(FrameworkElement alvo, double fracao)
    {
        if (alvo is ScrollBar barra)
        {
            barra.RaiseEvent(new ScrollEventArgs(ScrollEventType.ThumbTrack, barra.Maximum * fracao) { RoutedEvent = ScrollBar.ScrollEvent });
            return;
        }
        var mapa = _p.Rolagem.Mapa!; // a régua faz esta mesma conta ao arrastar
        _p.Rolagem.IrPara(_p.Rolagem.X, (fracao * mapa.Altura) - (_p.Rolagem.Viewport / 2), arrastando: true);
    }

    /// <summary>Desenha os dois painéis no meio do arraste e compara as faixas de texto em volta da linha de sincronia.</summary>
    object Comparar(Cenario c, double fracao, int indice)
    {
        var (esq, dir) = (Faixa(_p.Esquerdo), Faixa(_p.Direito));
        var imagem = Tela.Renderizar(_p);
        var area = new Int32Rect(esq.X, esq.Y, dir.X + dir.Width - esq.X, esq.Height);
        var recorte = new CroppedBitmap(imagem, area);
        var h = (int)_p.Direito.AlturaLinha;
        var meio = recorte.PixelHeight / 2;
        var iguais = ComparadorDePixels.LinhasIguais(recorte, dir.X - esq.X, esq.Width, Math.Max(0, meio - (4 * h)), Math.Min(recorte.PixelHeight, meio + (4 * h)));
        if (indice == 1 && c.Nome.EndsWith("-rapido", StringComparison.Ordinal)) Tela.Salvar(recorte, Path.Combine(saida, "arraste", c.Nome + ".png"));
        return new { fracao = Math.Round(fracao, 2), linhasDePixelIguais = Math.Round(iguais, 3) };
    }

    Int32Rect Faixa(PainelTexto painel)
    {
        var canto = painel.Vista.TranslatePoint(new Point(0, 0), _p);
        return new((int)canto.X, (int)canto.Y, (int)Math.Min(420, painel.Vista.ActualWidth), (int)painel.Vista.ActualHeight);
    }
}
