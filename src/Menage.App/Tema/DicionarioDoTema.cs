using System.Windows;
using System.Windows.Media;
using static Menage.App.Tema.PapelDeCor;

namespace Menage.App.Tema;

/// <summary>
/// Converte um tema em recursos: <c>Tema.&lt;Papel&gt;</c> (pincel) e <c>Tema.&lt;Papel&gt;.Cor</c> (cor), mais as chaves
/// do WPF UI que precisam seguir a paleta. O WPF UI resolve as cores dos pincéis por StaticResource,
/// então trocar só a cor não basta: é o pincel que tem de ser substituído. Os preenchimentos dele são
/// translúcidos sobre o fundo e acompanham a paleta sozinhos, por isso não aparecem aqui.
/// </summary>
public static class DicionarioDoTema
{
    /// <summary>Texto sobre botões coloridos. Branco de propósito: é a única cor que não vem da paleta.</summary>
    public static Color TextoSobreCor { get; } = Colors.White;

    static readonly (string Chave, PapelDeCor Papel)[] _chavesDoWpfUi =
    [
        ("ApplicationBackgroundBrush", Fundo), ("WindowBackground", Fundo), ("WindowForeground", Texto),
        ("TextFillColorPrimaryBrush", Texto), ("TextFillColorSecondaryBrush", TextoFraco),
        ("TextFillColorTertiaryBrush", TextoFraco), ("TextFillColorDisabledBrush", TextoDesab),
        ("TextPlaceholderColorBrush", TextoFraco),
        ("CardBackground", Painel), ("CardBackgroundFillColorDefaultBrush", Painel),
        ("CardBackgroundFillColorSecondaryBrush", Cartao2), ("CardForeground", Texto), ("CardBorderBrush", Borda),
        ("CardStrokeColorDefaultBrush", Borda), ("DividerStrokeColorDefaultBrush", Borda),
        ("SolidBackgroundFillColorBaseBrush", Fundo), ("SolidBackgroundFillColorSecondaryBrush", Lateral),
        ("SolidBackgroundFillColorTertiaryBrush", Painel), ("SolidBackgroundFillColorQuarternaryBrush", PainelClaro),
        ("ContentDialogBackground", Painel), ("ContentDialogForeground", Texto), ("ContentDialogBorderBrush", Borda),
        ("ContentDialogSeparatorBorderBrush", Borda), ("ContentDialogTopOverlay", Painel),
        ("ContextMenuBackground", Painel), ("ContextMenuBorderBrush", Borda), ("ContextMenuForeground", Texto),
        ("FlyoutBackground", Painel), ("FlyoutBorderBrush", Borda),
        ("ToolTipBackground", PainelClaro), ("ToolTipBorderBrush", Borda), ("ToolTipForeground", Texto),
    ];

    static readonly string[] _textoSobreDestaque =
    [
        "TextOnAccentFillColorPrimaryBrush", "AccentButtonForeground", "AccentButtonForegroundPointerOver",
    ];

    public static ResourceDictionary Montar(DefinicaoDeTema tema)
    {
        var dicionario = new ResourceDictionary();
        foreach (var papel in Enum.GetValues<PapelDeCor>())
        {
            dicionario[$"Tema.{papel}.Cor"] = tema[papel];
            dicionario[$"Tema.{papel}"] = Pincel(tema[papel]);
        }

        dicionario["Tema.TextoSobreCor"] = Pincel(TextoSobreCor);
        foreach (var (chave, papel) in _chavesDoWpfUi) dicionario[chave] = Pincel(tema[papel]);
        foreach (var chave in _textoSobreDestaque) dicionario[chave] = Pincel(TextoSobreCor);
        return dicionario;
    }

    static SolidColorBrush Pincel(Color cor)
    {
        var pincel = new SolidColorBrush(cor);
        pincel.Freeze();
        return pincel;
    }
}
