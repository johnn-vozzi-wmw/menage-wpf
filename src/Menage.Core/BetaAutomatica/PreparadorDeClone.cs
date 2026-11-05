using System.Text;
using Menage.Core.Beta;

namespace Menage.Core.BetaAutomatica;

/// <summary>
/// Clone isolado de cada projeto (só na primeira vez) + fetch + checkout da branch (ou tag) + reset + clean.
/// Os projetos de trabalho do usuário nunca são tocados: só se lê o endereço do origin deles.
/// </summary>
public sealed class PreparadorDeClone(IGit git, ConsoleDaBeta console)
{
    public const string BranchLocal = "beta-auto";

    public static string PastaDoClone(string pastaDeTrabalho, ProjetoDoGerador projeto)
    {
        var relativa = projeto.Pastas.FirstOrDefault(p => p.Contains('/') || p.Contains('\\')) ?? projeto.Pastas.FirstOrDefault() ?? projeto.Nome;
        return Path.Combine(pastaDeTrabalho, relativa);
    }

    /// <param name="projetoLocal">Pasta do projeto de trabalho (só para saber o endereço do origin e usar como referência do clone).</param>
    /// <param name="porTag">Verdadeiro: <paramref name="alvo"/> é uma tag (projeto web sem branch da atividade).</param>
    public async Task<Resultado<string>> PrepararAsync(
        ProjetoDoGerador projeto, string? projetoLocal, string pastaDeTrabalho, string alvo, bool porTag, string atividade, TextWriter log, CancellationToken cancelar = default)
    {
        var dir = PastaDoClone(pastaDeTrabalho, projeto);
        if (projetoLocal is null)
            return Falha(log, atividade, projeto.Nome, "o projeto não está configurado neste computador (precisa do repositório local só para saber o endereço do origin)");

        var url = await git.ExecutarAsync(Git.Em(projetoLocal).Comando("remote").Argumento("get-url").Argumento("origin"), cancelar);
        var endereco = url.EhOk ? url.Valor.PrimeiraLinha().Trim() : "";
        if (endereco.Length == 0) return Falha(log, atividade, projeto.Nome, "o projeto local não tem origin");

        if (!Directory.Exists(Path.Combine(dir, ".git")))
        {
            var criou = await ClonarAsync(projeto.Nome, projetoLocal, endereco, dir, atividade, log, cancelar);
            if (criou is not null) return Falha(log, atividade, projeto.Nome, criou);
        }

        log.WriteLine($"== Atualizando e preparando {(porTag ? "a tag " : "a branch ")}{alvo} (clone isolado; seus projetos de trabalho não são tocados) ==");
        var referencia = porTag ? "refs/tags/" + alvo : "origin/" + alvo;
        var passos = new[]
        {
            porTag ? Git.Em(dir).Comando("fetch").Argumento("origin").Opcao("--prune").Opcao("--tags") : Git.Em(dir).Comando("fetch").Argumento("origin").Opcao("--prune"),
            Git.Em(dir).Comando("checkout").Opcao("--force").Opcao("-B").Argumento(BranchLocal).Argumento(referencia),
            Git.Em(dir).Comando("reset").Opcao("--hard").Argumento(referencia),
            Git.Em(dir).Comando("clean").Opcao("-fdx"),
        };
        foreach (var passo in passos)
        {
            console.Registrar(NivelDoConsole.Comando, "Beta automática", atividade, projeto.Nome, $"{passo}   (em {dir})");
            var r = await git.ExecutarAsync(passo.ComTimeout(ConsultasGit.TempoLongo), cancelar);
            log.WriteLine(passo);
            if (!r.EhOk) return Falha(log, atividade, projeto.Nome, $"git {passo.Argumentos[0]} falhou: {r.Erro}");
            var saida = (r.Valor.Texto + "\n" + r.Valor.TextoErro).Trim();
            if (saida.Length > 0) { log.WriteLine(saida); console.Registrar(NivelDoConsole.Saida, "Beta automática", atividade, projeto.Nome, saida); }
        }

        return Resultado.Ok(dir);
    }

    async Task<string?> ClonarAsync(string nome, string projetoLocal, string endereco, string dir, string atividade, TextWriter log, CancellationToken cancelar)
    {
        log.WriteLine($"== Clonando em {dir} (primeira vez; fica guardado para as próximas betas) ==");
        var pai = Path.GetDirectoryName(dir)!;
        Directory.CreateDirectory(pai);
        var comando = Git.Em(pai).Comando("clone").Opcao("--reference-if-able").Argumento(projetoLocal).Opcao("--dissociate")
            .Argumento(endereco).Argumento(Path.GetFileName(dir)).ComTimeout(ConsultasGit.TempoLongo);
        console.Registrar(NivelDoConsole.Comando, "Beta automática", atividade, nome, comando.ToString());
        var clone = await git.ExecutarAsync(comando, cancelar);
        if (!clone.EhOk) return "não consegui clonar: " + clone.Erro;
        console.Registrar(NivelDoConsole.Info, "Beta automática", atividade, nome, "clone criado em " + dir);
        return null;
    }

    Resultado<string> Falha(TextWriter log, string atividade, string projeto, string erro)
    {
        log.WriteLine("ERRO: " + erro);
        console.Registrar(NivelDoConsole.Erro, "Beta automática", atividade, projeto, erro);
        return Resultado.Falha<string>(erro);
    }

    public static StreamWriter AbrirLog(string arquivo)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(arquivo)!);
        return new StreamWriter(arquivo, false, new UTF8Encoding(true)) { AutoFlush = true };
    }
}
