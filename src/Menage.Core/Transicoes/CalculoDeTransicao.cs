namespace Menage.Core.Transicoes;

/// <summary>Quanto movimento a tela mostra. Resolvido do sistema (animações do Windows) e da preferência "reduzir movimento".</summary>
public enum NivelDeMovimento
{
    /// <summary>Sem animação: o estado final aparece de uma vez.</summary>
    Nenhum,

    /// <summary>Só opacidade, sem deslocamento nem escala.</summary>
    Reduzido,

    /// <summary>Opacidade, deslocamento e escala.</summary>
    Completo,
}

/// <summary>Para onde a navegação foi, em relação à tela anterior.</summary>
public enum Sentido { Nenhum, Avancar, Voltar }

/// <summary>Aparência de um elemento durante a transição. Só o que o WPF anima sem refazer o layout.</summary>
public readonly record struct EstadoVisual(double Opacidade, double Deslocamento, double Escala)
{
    public static EstadoVisual Repouso { get; } = new(1, 0, 1);
}

/// <summary>Um trecho de animação já decidido: de onde, para onde, quanto dura. <see cref="Instantaneo"/> = só aplicar o alvo.</summary>
public readonly record struct PassoDeTransicao(EstadoVisual De, EstadoVisual Para, TimeSpan Duracao, bool Instantaneo);

/// <summary>
/// Algoritmo das transições (sem WPF, testável). Duas ideias: (1) a duração é proporcional ao que falta andar, então uma
/// transição que chega no meio de outra sai do estado atual sem pulo e sem esperar o tempo cheio; (2) o nível de movimento
/// remove deslocamento/escala ou tudo.
/// </summary>
public static class CalculoDeTransicao
{
    // Durações cheias (ms), dentro de 120–220. O app expõe os mesmos valores como Duration em Tema/Animacao.
    public const int EntradaPaginaMs = 180;
    public const int SaidaPaginaMs = 120;
    public const int EntradaJanelaMs = 200;
    public const int SaidaJanelaMs = 140;

    /// <summary>Menor trecho animado: abaixo disso o olho não vê e só gastaria quadros.</summary>
    public const int MinimoMs = 60;

    /// <summary>Deslocamento horizontal (px) da página que entra; a que sai anda a metade, no sentido oposto.</summary>
    public const double DeslocamentoPagina = 24;

    /// <summary>Escala de onde a janela nasce e para onde volta ao fechar.</summary>
    public const double EscalaDeJanela = 0.96;

    /// <summary>Abaixo disto o estado atual já é o alvo.</summary>
    const double Tolerancia = 0.005;

    /// <summary>Avançar quando o índice destino é maior; voltar quando menor. Sem índice anterior ou igual, não há direção.</summary>
    public static Sentido Direcao(int? indiceAnterior, int indiceNovo) => indiceAnterior switch
    {
        null => Sentido.Nenhum,
        var anterior when indiceNovo > anterior => Sentido.Avancar,
        var anterior when indiceNovo < anterior => Sentido.Voltar,
        _ => Sentido.Nenhum,
    };

    static double Sinal(Sentido sentido) => sentido switch { Sentido.Avancar => 1, Sentido.Voltar => -1, _ => 0 };

    /// <summary>De onde a página nova parte: já deslocada para o lado de onde vem, invisível.</summary>
    public static EstadoVisual EntradaDePagina(Sentido sentido) => new(0, Sinal(sentido) * DeslocamentoPagina, 1);

    /// <summary>Para onde a página velha vai: invisível, empurrada para o lado oposto, metade da distância.</summary>
    public static EstadoVisual SaidaDePagina(Sentido sentido) => new(0, -Sinal(sentido) * DeslocamentoPagina / 2, 1);

    public static EstadoVisual EntradaDeJanela { get; } = new(0, 0, EscalaDeJanela);

    public static EstadoVisual SaidaDeJanela { get; } = new(0, 0, EscalaDeJanela);

    /// <summary>Distância normalizada (0..~1) entre dois estados: o maior dos componentes, cada um na sua escala.</summary>
    public static double Distancia(EstadoVisual a, EstadoVisual b) => Math.Max(
        Math.Abs(a.Opacidade - b.Opacidade),
        Math.Max(Math.Abs(a.Deslocamento - b.Deslocamento) / DeslocamentoPagina, Math.Abs(a.Escala - b.Escala) / (1 - EscalaDeJanela)));

    /// <summary>Tira do estado o que o nível não permite (Reduzido: sem deslocamento nem escala).</summary>
    public static EstadoVisual Limitar(EstadoVisual estado, NivelDeMovimento nivel) =>
        nivel == NivelDeMovimento.Completo ? estado : estado with { Deslocamento = 0, Escala = 1 };

    /// <summary>
    /// Planeja ir do estado <paramref name="atual"/> ao <paramref name="alvo"/>. <paramref name="origem"/> é de onde essa
    /// transição partiria se começasse do zero: a duração é a cheia vezes a fração do caminho que ainda falta.
    /// </summary>
    public static PassoDeTransicao Planejar(EstadoVisual atual, EstadoVisual alvo, EstadoVisual origem, int duracaoCheiaMs, NivelDeMovimento nivel)
    {
        alvo = Limitar(alvo, nivel);
        origem = Limitar(origem, nivel);
        atual = Limitar(atual, nivel);
        if (nivel == NivelDeMovimento.Nenhum || Distancia(atual, alvo) < Tolerancia)
            return new PassoDeTransicao(alvo, alvo, TimeSpan.Zero, true);

        var total = Distancia(origem, alvo);
        var fracao = total < Tolerancia ? 1 : Math.Clamp(Distancia(atual, alvo) / total, 0, 1);
        var ms = Math.Max(MinimoMs, (int)Math.Round(duracaoCheiaMs * fracao));
        return new PassoDeTransicao(atual, alvo, TimeSpan.FromMilliseconds(ms), false);
    }

    /// <summary>Nível efetivo: sem animações no Windows → nenhum; preferência de reduzir → só opacidade.</summary>
    public static NivelDeMovimento Resolver(bool animacoesDoSistema, bool reduzirMovimento) =>
        !animacoesDoSistema ? NivelDeMovimento.Nenhum : reduzirMovimento ? NivelDeMovimento.Reduzido : NivelDeMovimento.Completo;
}
