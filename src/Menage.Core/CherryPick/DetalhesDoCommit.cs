using Menage.Core.Historico;

namespace Menage.Core.CherryPick;

public sealed record DetalhesDoCommit(string Hash, string Mensagem, IReadOnlyList<string> Arquivos);

/// <summary>Painel de detalhes do commit: mensagem inteira e arquivos alterados (com +/-).</summary>
public sealed class LeitorDeDetalhes(IGit git)
{
    public async Task<DetalhesDoCommit> LerAsync(string repo, string hash, CancellationToken cancelar = default)
    {
        var mensagem = (await git.ExecutarBrutoAsync(Git.Em(repo).Comando("log").Opcao("-1").Opcao("--format=%B").Argumento(hash), cancelar)).Texto.Trim();
        var args = Git.Em(repo).Comando("show").Opcao("--numstat").Opcao("--format=").Opcao("-m").Opcao("--first-parent").Argumento(hash);
        var arquivos = (await git.ExecutarBrutoAsync(args, cancelar)).Linhas().Select(Formatar).ToList();
        return new(hash, mensagem, arquivos);
    }

    static string Formatar(string linha)
    {
        var p = linha.Split('\t');
        if (p.Length < 3) return linha;
        return p[0] == "-" ? $"{p[2]}   (binário)" : $"{p[2]}   (+{p[0]} -{p[1]})";
    }

    /// <summary>Linha do assunto, pronta para a fila.</summary>
    public static ItemDaFila ParaItem(Projeto projeto, CommitLinha commit, string origem) => new()
    {
        Projeto = projeto.Nome,
        Caminho = projeto.Caminho,
        Hash = commit.Hash,
        Curto = commit.Curto,
        Assunto = commit.Assunto,
        Autor = commit.Autor,
        Data = commit.Data.ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture),
        Origem = origem,
    };
}
