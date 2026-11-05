using System.Globalization;
using Menage.Core.Projetos;
using Menage.Core.Repos;

namespace Menage.Core.LinhaDoTempo;

/// <summary>Tags recentes dos projetos. A mesma tag criada na mesma hora em vários projetos vira um evento só.</summary>
public sealed class LeitorDeTags(IGit git)
{
    public async Task<IReadOnlyList<EventoDeRelease>> LerAsync(IEnumerable<Projeto> projetos, int porProjeto = 40, CancellationToken cancelar = default)
    {
        var grupos = new Dictionary<string, Grupo>(StringComparer.Ordinal);
        foreach (var projeto in projetos)
        {
            if (!ConsultasGit.EhRepositorio(projeto.Caminho)) continue;
            var saida = await git.ExecutarBrutoAsync(Git.Em(projeto.Caminho).Comando("for-each-ref")
                .Opcao("--sort=-creatordate").Opcao($"--count={porProjeto}").Argumento("refs/tags")
                .Opcao("--format=%(refname:short)%09%(objectname)%09%(*objectname)%09%(creatordate:iso8601-strict)%09%(subject)"), cancelar);
            if (!saida.Sucesso) continue;
            foreach (var linha in saida.Linhas()) Acumular(grupos, projeto.Nome, linha);
        }

        return [.. grupos.Values.Select(g => g.Evento())];
    }

    static void Acumular(Dictionary<string, Grupo> grupos, string projeto, string linha)
    {
        var p = linha.Split('\t');
        if (p.Length < 4 || !DateTimeOffset.TryParse(p[3], CultureInfo.InvariantCulture, DateTimeStyles.None, out var data)) return;
        var sha = p[2].Length > 0 ? p[2] : p[1];
        sha = sha.Length > 8 ? sha[..8] : sha;
        var chave = $"{p[0]}|{data.LocalDateTime:yyyyMMddHH}";
        if (!grupos.TryGetValue(chave, out var grupo)) grupos[chave] = grupo = new Grupo(p[0], data.LocalDateTime);
        grupo.Itens.Add(new ItemDaTag(projeto, sha, p.Length > 4 ? p[4] : ""));
    }

    sealed record ItemDaTag(string Projeto, string Sha, string Assunto);

    sealed class Grupo(string tag, DateTime data)
    {
        public List<ItemDaTag> Itens { get; } = [];

        public EventoDeRelease Evento() => new(
            data, TipoDeEvento.Tag, $"Tag {tag}",
            string.Join("   |   ", Itens.Select((i, n) => $"{i.Projeto} @{i.Sha}{(n == 0 && i.Assunto.Length > 0 ? $"  \"{i.Assunto}\"" : "")}")),
            "", EstadoDoEvento.Ok, [.. Itens.Select(i => i.Projeto)]);
    }
}
