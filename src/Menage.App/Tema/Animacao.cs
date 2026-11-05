using System.Windows;
using System.Windows.Media.Animation;

namespace Menage.App.Tema;

/// <summary>
/// Tokens de movimento. Tudo por tempo e sempre em transform/opacidade (nunca largura ou altura de controle).
/// Em XAML: <c>Duration="{x:Static t:Animacao.Media}"</c>, <c>EasingFunction="{x:Static t:Animacao.Suave}"</c>.
/// </summary>
public static class Animacao
{
    /// <summary>Hover e pressão.</summary>
    public static Duration Rapida { get; } = new(TimeSpan.FromMilliseconds(100));

    /// <summary>Deslizes e abrir/fechar painéis.</summary>
    public static Duration Media { get; } = new(TimeSpan.FromMilliseconds(220));

    /// <summary>Entrada de janela e troca de tema.</summary>
    public static Duration Lenta { get; } = new(TimeSpan.FromMilliseconds(320));

    /// <summary>
    /// "Reduzir movimento" do app ou animações desligadas no Windows. Controles criados pelo XAML (sem injeção) leem aqui; quem tem o
    /// <c>ServicoDeTransicao</c> usa o <c>Nivel</c> dele, que parte desta mesma preferência.
    /// </summary>
    public static bool Reduzir => PreferenciaDeReduzir || !SystemParameters.ClientAreaAnimation;

    /// <summary>Escolha do usuário em Personalização; só <c>ServicoDeTransicao</c> escreve.</summary>
    public static bool PreferenciaDeReduzir { get; set; }

    /// <summary>A duração pedida, ou zero (muda de uma vez) quando o movimento está reduzido.</summary>
    public static Duration Movimento(Duration normal) => Reduzir ? new Duration(TimeSpan.Zero) : normal;

    /// <summary>Atraso entre itens numa entrada em cascata, em milissegundos (XamlFlair).</summary>
    public const double CascataMs = 35;

    /// <summary>Ease-in-out: elementos que já estão na tela e mudam de lugar.</summary>
    public static IEasingFunction Suave { get; } = Congelar(new CubicEase { EasingMode = EasingMode.EaseInOut });

    /// <summary>Ease-out: elementos que entram.</summary>
    public static IEasingFunction Entrada { get; } = Congelar(new CubicEase { EasingMode = EasingMode.EaseOut });

    static CubicEase Congelar(CubicEase ease)
    {
        ease.Freeze();
        return ease;
    }
}
