namespace Menage.Core.ModoBeta;

/// <summary>
/// Os principais declaram framework.version com -SNAPSHOT (5.060-SNAPSHOT), mas o framework vem da tag 5.060. Para o build casar, o
/// pom.xml do framework e dos módulos filhos recebe o -SNAPSHOT. Só mexe no arquivo: não faz commit.
/// </summary>
public static class SnapshotNoPom
{
    const int ProfundidadeMaxima = 4;

    /// <summary>Quantos arquivos mudaram; os avisos são versões que não batem com a tag e ficaram como estavam.</summary>
    public static (int Alterados, IReadOnlyList<string> Avisos) Aplicar(string repo, string versao, string tag)
    {
        var raiz = Path.Combine(repo, "pom.xml");
        if (versao.Equals(tag, StringComparison.OrdinalIgnoreCase) || !File.Exists(raiz)) return (0, []);

        var artefatoRaiz = LeitorDePom.Interpretar(LeitorDePom.Ler(raiz)).Artefato;
        var alterados = 0;
        var avisos = new List<string>();
        foreach (var arquivo in PomsFilhos(repo, 0).Prepend(raiz))
        {
            var xml = LeitorDePom.Ler(arquivo);
            var info = LeitorDePom.Interpretar(xml);
            var (atual, inicio) = arquivo == raiz ? (info.Versao, info.InicioDaVersao) : VersaoDoPai(info, artefatoRaiz);
            if (atual is null || atual.Equals(versao, StringComparison.OrdinalIgnoreCase)) continue;
            if (!atual.Equals(tag, StringComparison.OrdinalIgnoreCase))
            {
                avisos.Add($"{Path.GetFileName(Path.GetDirectoryName(arquivo))}\\pom.xml está na versão {atual} (a tag é {tag}); não alterei.");
                continue;
            }

            LeitorDePom.Gravar(arquivo, xml[..inicio] + versao + xml[(inicio + atual.Length)..]);
            alterados++;
        }

        return (alterados, avisos);
    }

    static (string? Versao, int Inicio) VersaoDoPai(PomInfo info, string artefatoRaiz) =>
        info.PaiArtefato.Equals(artefatoRaiz, StringComparison.OrdinalIgnoreCase) ? (info.PaiVersao, info.InicioDaVersaoDoPai) : (null, 0);

    static IEnumerable<string> PomsFilhos(string pasta, int nivel)
    {
        if (nivel > ProfundidadeMaxima) yield break;
        foreach (var filha in Directory.GetDirectories(pasta))
        {
            var nome = Path.GetFileName(filha);
            if (nome.StartsWith('.') || nome.Equals("target", StringComparison.OrdinalIgnoreCase) || nome.Equals("node_modules", StringComparison.OrdinalIgnoreCase)) continue;

            var pom = Path.Combine(filha, "pom.xml");
            if (File.Exists(pom)) yield return pom;
            foreach (var neto in PomsFilhos(filha, nivel + 1)) yield return neto;
        }
    }
}
