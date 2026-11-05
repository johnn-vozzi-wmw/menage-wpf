using Menage.Core.Perfis;
using Menage.Core.Projetos;
using Menage.Core.Repos;

namespace Menage.Core.Beta;

/// <summary>Uma linha da grade do Gerador antes de rodar: onde o projeto está nesta base e em que branch.</summary>
public sealed record LinhaDoGerador(ProjetoDoGerador Projeto, string? Pasta, string Branch, string Grupo)
{
    public bool Encontrado => Pasta is not null;
}

/// <summary>
/// Resolve pastas e branches e transforma a seleção da tela em um <see cref="PedidoDeGeracao"/> validado
/// (pastas, JDK, perfil Tester sem deploy). Nada executa aqui.
/// </summary>
public sealed class PreparadorDeGeracao(RepositorioDoGerador repositorio, IProjetos projetos, ConsultasGit git, IPerfilAtual perfil, PastaDeDados dados)
{
    /// <summary>Base vazia = os projetos configurados no Menage (pelo nome).</summary>
    public const string BaseDoMenage = "";

    public bool DeployLiberado => perfil.Atual != PerfilDeUso.Tester;

    public string PastaDosLogs => Path.Combine(dados.Caminho, "gerador-beta", "logs");

    public string? Pasta(ProjetoDoGerador projeto, string pastaBase)
    {
        if (pastaBase == BaseDoMenage)
            return projetos.Todos.FirstOrDefault(p => p.Nome.Equals(projeto.Nome, StringComparison.OrdinalIgnoreCase) && TemPom(p.Caminho))?.Caminho;
        return projeto.Pastas
            .Select(rel => Combinar(pastaBase, rel))
            .FirstOrDefault(c => c is not null && TemPom(c));
    }

    public async Task<IReadOnlyList<LinhaDoGerador>> LerLinhasAsync(string pastaBase, CancellationToken cancelar = default)
    {
        var linhas = new List<LinhaDoGerador>();
        foreach (var p in repositorio.Configuracao.Projetos)
        {
            var pasta = Pasta(p, pastaBase);
            var branch = pasta is null ? "" : await git.BranchAtualAsync(pasta, cancelar);
            linhas.Add(new LinhaDoGerador(p, pasta, branch, p.Grupo));
        }

        return linhas;
    }

    /// <summary>Monta o pedido na ordem dada (a ordem de execução). Falha com a explicação do primeiro problema.</summary>
    public async Task<Resultado<PedidoDeGeracao>> MontarAsync(
        IReadOnlyList<string> ordem, bool deploy, string pastaBase, CancellationToken cancelar = default)
    {
        var config = repositorio.Configuracao;
        if (deploy && !DeployLiberado)
            return Resultado.Falha<PedidoDeGeracao>("O perfil Tester não faz deploy", "O deploy publica no Nexus.", "Use o modo install.");
        if (ordem.Count == 0)
            return Resultado.Falha<PedidoDeGeracao>("Nenhum projeto marcado", null, "Marque ao menos um projeto.");

        var planos = new List<PlanoDeProjeto>();
        foreach (var nome in ordem)
        {
            var projeto = config.Projetos.FirstOrDefault(p => p.Nome.Equals(nome, StringComparison.OrdinalIgnoreCase));
            if (projeto is null) return Resultado.Falha<PedidoDeGeracao>($"Projeto {nome} não está na configuração do Gerador");
            var pasta = Pasta(projeto, pastaBase);
            if (pasta is null) return Resultado.Falha<PedidoDeGeracao>($"Pasta não encontrada para {nome}", "Nenhum pom.xml nesta base.", "Escolha outra pasta base.");
            if (MontadorDeComando.Validar(projeto, config) is { } problema) return Resultado.Falha<PedidoDeGeracao>(problema);

            var branch = await git.BranchAtualAsync(pasta, cancelar);
            planos.Add(new PlanoDeProjeto(projeto, pasta, branch, MontadorDeComando.JavaHome(projeto, config)!,
                MontadorDeComando.Completo(projeto, config, deploy), config.LimparTarget.Vale(nome), MontadorDeComando.Personalizado(projeto, config)));
        }

        var logs = Path.Combine(PastaDosLogs, $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{(deploy ? "deploy" : "install")}");
        return Resultado.Ok(new PedidoDeGeracao(planos, deploy, logs, config.MavenBin, config.Java8Home));
    }

    static bool TemPom(string pasta) => File.Exists(Path.Combine(pasta, "pom.xml"));

    static string? Combinar(string baseDir, string relativo)
    {
        try { return Path.Combine(baseDir, relativo); }
        catch (ArgumentException) { return null; }
    }
}
