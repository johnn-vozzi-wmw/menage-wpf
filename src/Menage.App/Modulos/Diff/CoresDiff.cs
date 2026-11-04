using System.Windows;
using System.Windows.Media;
using Menage.Core.Diff;
using Wpf.Ui.Appearance;

namespace Menage.App.Modulos.Diff;

/// <summary>
/// Pincéis do que é desenhado por código (fundos de linha, faixas, régua). Tudo sai dos tokens do tema atual
/// misturados com o fundo do editor, então acompanha claro/escuro e qualquer paleta.
/// </summary>
public sealed class CoresDiff
{
    /// <param name="dono">Só escuta o tema enquanto o dono está na tela: o evento é estático e prenderia a janela na memória.</param>
    public CoresDiff(FrameworkElement dono)
    {
        Ler();
        dono.Loaded += (_, _) => ApplicationThemeManager.Changed += AoMudarTema;
        dono.Unloaded += (_, _) => ApplicationThemeManager.Changed -= AoMudarTema;
    }

    public event Action? Mudou;

    public Brush Fundo { get; private set; } = Brushes.White;

    public Brush Texto { get; private set; } = Brushes.Black;

    public Brush TextoFraco { get; private set; } = Brushes.Gray;

    public Brush Linha { get; private set; } = Brushes.LightGray;

    public Brush Selecao { get; private set; } = Brushes.LightBlue;

    public Color Sucesso { get; private set; }

    public Color Perigo { get; private set; }

    public Color Ambar { get; private set; }

    public Color Destaque { get; private set; }

    public Brush PalavraNova { get; private set; } = Brushes.LightGreen;

    public Brush PalavraVelha { get; private set; } = Brushes.LightPink;

    public Brush EtiquetaFundo { get; private set; } = Brushes.LightYellow;

    readonly Dictionary<Color, Brush> _pinceis = [];
    Brush _adicionada = Brushes.Honeydew;
    Brush _removida = Brushes.MistyRose;
    Brush _invisivel = Brushes.LightYellow;
    Brush _dobra = Brushes.WhiteSmoke;
    Brush _conflito = Brushes.LightYellow;
    Brush _alteradaAtual = Brushes.AliceBlue;

    public Brush? FundoDe(TipoLinha tipo) => tipo switch
    {
        TipoLinha.Adicionada or TipoLinha.AlteradaEntrando or TipoLinha.Veio => _adicionada,
        TipoLinha.Removida => _removida,
        TipoLinha.Invisivel => _invisivel,
        TipoLinha.Dobra => _dobra,
        TipoLinha.Conflito => _conflito,
        TipoLinha.AlteradaAtual => _alteradaAtual,
        _ => null,
    };

    public Color? BarraDe(TipoLinha tipo) => tipo switch
    {
        TipoLinha.Adicionada or TipoLinha.AlteradaEntrando or TipoLinha.Veio => Sucesso,
        TipoLinha.Removida => Perigo,
        TipoLinha.Invisivel or TipoLinha.Conflito => Ambar,
        TipoLinha.AlteradaAtual => Destaque,
        _ => null,
    };

    /// <summary>Pincel sólido de uma cor do tema, guardado para não criar um por linha a cada quadro.</summary>
    public Brush Pincel(Color cor)
    {
        if (_pinceis.TryGetValue(cor, out var pronto)) return pronto;
        return _pinceis[cor] = Solido(cor);
    }

    static Color Token(string chave, Color padrao) => Application.Current?.TryFindResource(chave) switch
    {
        SolidColorBrush b => b.Color,
        Color c => c,
        _ => padrao,
    };

    static Color Misturar(Color fundo, Color cor, double quanto)
    {
        static byte Canal(byte a, byte b, double t) => (byte)Math.Round(a + ((b - a) * t));
        return Color.FromRgb(Canal(fundo.R, cor.R, quanto), Canal(fundo.G, cor.G, quanto), Canal(fundo.B, cor.B, quanto));
    }

    static SolidColorBrush Solido(Color cor)
    {
        var pincel = new SolidColorBrush(cor);
        pincel.Freeze();
        return pincel;
    }

    void AoMudarTema(ApplicationTheme tema, Color destaque)
    {
        Ler();
        Mudou?.Invoke();
    }

    void Ler()
    {
        var fundo = Token("SolidBackgroundFillColorTertiaryBrush", Colors.White);
        var texto = Token("TextFillColorPrimaryBrush", Colors.Black);
        Sucesso = Opaco(Token("SystemFillColorSuccessBrush", Colors.Green));
        Perigo = Opaco(Token("SystemFillColorCriticalBrush", Colors.Red));
        Ambar = Opaco(Token("SystemFillColorCautionBrush", Colors.Orange));
        Destaque = Opaco(Token("SystemAccentColorPrimary", Colors.DodgerBlue));
        var forca = fundo.R < 128 ? 0.22 : 0.14;
        Fundo = Solido(fundo);
        Texto = Solido(Opaco(texto));
        TextoFraco = Solido(Misturar(fundo, Opaco(texto), 0.55));
        Linha = Solido(Misturar(fundo, Opaco(texto), 0.14));
        Selecao = Solido(Color.FromArgb(0x60, Destaque.R, Destaque.G, Destaque.B));
        _adicionada = Solido(Misturar(fundo, Sucesso, forca));
        _removida = Solido(Misturar(fundo, Perigo, forca));
        _invisivel = Solido(Misturar(fundo, Ambar, forca * 0.7));
        _conflito = Solido(Misturar(fundo, Ambar, forca * 1.5));
        _alteradaAtual = Solido(Misturar(fundo, Destaque, forca));
        _dobra = Solido(Misturar(fundo, Opaco(texto), 0.05));
        PalavraNova = Solido(Misturar(fundo, Sucesso, forca * 2.6));
        PalavraVelha = Solido(Misturar(fundo, Perigo, forca * 2.6));
        EtiquetaFundo = Solido(Misturar(fundo, Ambar, forca * 2));
    }

    static Color Opaco(Color c) => Color.FromRgb(c.R, c.G, c.B);
}
