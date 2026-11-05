using System.Windows;
using Menage.App.Tema;
using Menage.Core.Transicoes;

namespace Menage.App.Animacoes;

/// <summary>
/// Ponto único das transições do app: decide o nível de movimento (animações do Windows + preferência "reduzir
/// movimento") e entrega os animadores. Janelas (<c>JanelaBase</c>), painéis e o <see cref="HospedeDeTransicao"/> usam este serviço.
/// </summary>
public sealed class ServicoDeTransicao
{
    /// <summary>Preferência do usuário: só opacidade, sem deslocamento nem escala.</summary>
    bool _reduzir;

    public bool ReduzirMovimento
    {
        get => _reduzir;
        set
        {
            _reduzir = value;
            Tema.Animacao.PreferenciaDeReduzir = value;
        }
    }

    /// <summary>Lido a cada transição, então acompanha a opção do Windows sem reiniciar.</summary>
    public NivelDeMovimento Nivel => CalculoDeTransicao.Resolver(SystemParameters.ClientAreaAnimation, ReduzirMovimento);

    public AnimadorDeElemento CriarAnimador(FrameworkElement elemento, bool vertical = false) => new(elemento, () => Nivel, vertical);

    /// <summary>Janela/painel entrando: opacidade + escala 0.96→1, ease-out.</summary>
    public static Task<bool> EntrarAsync(AnimadorDeElemento animador, EstadoVisual? origem = null)
    {
        var inicio = origem ?? CalculoDeTransicao.EntradaDeJanela;
        animador.Definir(inicio);
        return animador.IrAsync(EstadoVisual.Repouso, inicio, CalculoDeTransicao.EntradaJanelaMs, Animacao.Entrada);
    }

    /// <summary>Janela/painel saindo: o inverso da entrada, ease-in-out. Quem chama fecha/oculta quando devolver true.</summary>
    public static Task<bool> SairAsync(AnimadorDeElemento animador, EstadoVisual? destino = null) =>
        animador.IrAsync(destino ?? CalculoDeTransicao.SaidaDeJanela, EstadoVisual.Repouso, CalculoDeTransicao.SaidaJanelaMs, Animacao.Suave);
}
