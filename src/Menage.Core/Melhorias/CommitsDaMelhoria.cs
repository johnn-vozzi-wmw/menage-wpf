using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Menage.Core.Melhorias;

public sealed record CommitDaMelhoria(string Projeto, string Hash, string Autor, DateTime Quando, string Assunto, string Refs);

/// <summary>Commits (de todas as branches) cujas mensagens citam o id da melhoria, nos projetos dela. Só leitura.</summary>
public sealed class CommitsDaMelhoria(IProjetos projetos, IGit git)
{
    public async Task<IReadOnlyList<CommitDaMelhoria>> ListarAsync(Melhoria m, int maximoPorProjeto = 200, CancellationToken cancelar = default)
    {
        var lista = new List<CommitDaMelhoria>();
        var padrao = Melhoria.PadraoDeCommit(m.Id);
        foreach (var b in m.Branches)
        {
            if (projetos.Todos.FirstOrDefault(p => p.Nome.Equals(b.Projeto, StringComparison.OrdinalIgnoreCase)) is not { } p) continue;
            var args = Git.Em(p.Caminho).Comando("log").Opcao("--all").Opcao("-i").Argumento("--grep=" + m.Id)
                .Argumento("--format=%h%x1f%an%x1f%aI%x1f%s%x1f%D").Opcao("-n", maximoPorProjeto.ToString(CultureInfo.InvariantCulture));
            var saida = await git.ExecutarBrutoAsync(args, cancelar);
            foreach (var linha in saida.Linhas())
            {
                var c = linha.Split('\u001f');
                if (c.Length < 4 || !padrao.IsMatch(c[3])) continue;
                _ = DateTimeOffset.TryParse(c[2], CultureInfo.InvariantCulture, DateTimeStyles.None, out var quando);
                lista.Add(new CommitDaMelhoria(p.Nome, c[0], c[1], quando.LocalDateTime, c[3], c.Length > 4 ? c[4] : ""));
            }
        }

        return [.. lista.OrderByDescending(c => c.Quando)];
    }
}

public static class MelhoriasExtensions
{
    /// <summary>Motor das Melhorias. O módulo de banco pode registrar antes o seu <see cref="IContextoDeBanco"/>.</summary>
    public static IServiceCollection AddMelhorias(this IServiceCollection servicos)
    {
        servicos.TryAddSingleton<IContextoDeBanco, SemContextoDeBanco>();
        servicos.AddSingleton<IArmazenamento<ConfigMelhorias>>(sp =>
            new Armazenamento<ConfigMelhorias>(sp.GetRequiredService<PastaDeDados>(), ConfigMelhorias.NomeDoArquivo, padrao: ConfigMelhorias.Padrao));
        servicos.AddSingleton<RepositorioDeMelhorias>();
        servicos.AddSingleton<SqliteDeAtividade>();
        servicos.AddSingleton<AtivadorDeMelhorias>();
        servicos.AddSingleton<CommitsDaMelhoria>();
        return servicos;
    }
}
