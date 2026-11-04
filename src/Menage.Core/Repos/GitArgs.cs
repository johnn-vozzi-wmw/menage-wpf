using System.Collections.Immutable;

namespace Menage.Core.Repos;

/// <summary>Ponto de entrada fluente: <c>Git.Em(repo).Comando("push").Opcao("-u").Argumento("origin")</c>.</summary>
public static class Git
{
    public static GitArgs Em(string repositorio) => new(repositorio);
}

/// <summary>
/// Argumentos imutáveis de uma chamada ao git. Cada valor vai como argumento separado
/// (nunca concatenado numa string), então não há problema de aspas nem injeção.
/// </summary>
public sealed record GitArgs
{
    ImmutableArray<string> _config { get; init; } = [];
    ImmutableArray<string> _args { get; init; } = [];

    public GitArgs(string repositorio) => Repositorio = repositorio;

    public string Repositorio { get; }

    public TimeSpan? Timeout { get; private init; }

    /// <summary>Linha de comando completa, na ordem: <c>-c k=v</c> ..., comando, opções e argumentos.</summary>
    public IReadOnlyList<string> Argumentos => [.. _config, .. _args];

    public GitArgs Comando(string comando) => Com(_args.Add(comando));

    public GitArgs Opcao(string opcao) => Com(_args.Add(opcao));

    public GitArgs Opcao(string nome, string valor) => Com(_args.Add(nome).Add(valor));

    public GitArgs Argumento(string valor) => Com(_args.Add(valor));

    /// <summary>Adiciona <c>--</c> e os caminhos: tudo depois dele é arquivo, nunca opção.</summary>
    public GitArgs Caminhos(IEnumerable<string> caminhos) => Com(_args.Add("--").AddRange(caminhos));

    /// <summary>Equivalente a <c>git -c chave=valor</c> (vai antes do comando).</summary>
    public GitArgs Config(string chave, string valor) => this with { _config = _config.Add("-c").Add($"{chave}={valor}") };

    public GitArgs ComTimeout(TimeSpan timeout) => this with { Timeout = timeout };

    public override string ToString() => "git " + string.Join(' ', Argumentos.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));

    GitArgs Com(ImmutableArray<string> args) => this with { _args = args };
}
