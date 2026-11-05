namespace Menage.Core.BetaAutomatica;

/// <summary>
/// Ordem de execução dos projetos (dependências): frameworks (util, service, integrator, integrator-tc, core, web), framework-tc (PDA),
/// web (lavenderecore, lavenderedados, lavendereweb) e por fim lavenderepda. Fora da lista, o usuário define a ordem.
/// </summary>
public static class OrdemDeProjetos
{
    public static readonly IReadOnlyList<string> Padrao =
    [
        "framework-util", "framework-service", "framework-integrator", "framework-integrator-tc", "framework-core", "framework-web",
        "framework-tc", "lavenderecore", "lavenderedados", "lavendereweb", "lavenderepda",
    ];

    public static List<string> Efetiva(IReadOnlyList<string>? personalizada) =>
        personalizada is { Count: > 0 } ? [.. personalizada] : [.. Padrao];

    /// <summary>Os desconhecidos vão no fim (na ordem recebida) e também são devolvidos em <paramref name="semOrdem"/>.</summary>
    public static List<string> Ordenar(IEnumerable<string> nomes, IReadOnlyList<string>? personalizada, out List<string> semOrdem)
    {
        var ordem = Efetiva(personalizada);
        var lista = nomes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        semOrdem = [.. lista.Where(n => Indice(ordem, n) < 0)];
        var conhecidos = lista.Where(n => Indice(ordem, n) >= 0).OrderBy(n => Indice(ordem, n)).ToList();
        conhecidos.AddRange(semOrdem);
        return conhecidos;
    }

    /// <summary>Guarda a ordem escolhida para um lote: os novos entram logo depois do que veio antes deles na escolha.</summary>
    public static List<string> Incorporar(IReadOnlyList<string>? personalizada, IReadOnlyList<string> escolhida)
    {
        var efetiva = Efetiva(personalizada);
        for (var k = 0; k < escolhida.Count; k++)
        {
            if (Indice(efetiva, escolhida[k]) >= 0) continue;
            var posicao = k == 0 ? 0 : Indice(efetiva, escolhida[k - 1]) + 1;
            efetiva.Insert(Math.Clamp(posicao, 0, efetiva.Count), escolhida[k]);
        }

        return efetiva;
    }

    static int Indice(List<string> ordem, string nome) => ordem.FindIndex(x => x.Equals(nome, StringComparison.OrdinalIgnoreCase));
}
