using Menage.Core.Operacoes.Decoradores;

namespace Menage.Core.Operacoes.Push;

/// <summary>Um <c>git push</c> de uma branch, com as regras de segurança e a tradução das recusas do origin.</summary>
public sealed class EnvioParaOrigin(IGit git, IRegrasDeProtecao regras)
{
    /// <param name="PublicarNova">Se a branch não tem upstream, publica com <c>push -u origin</c>; senão recusa.</param>
    public async Task<Resultado<string>> EnviarAsync(string repo, string branch, bool forcar, bool publicarNova, CancellationToken cancelar)
    {
        if (regras.ProtegidaParaPush(branch))
            return Resultado.Falha<string>("branch protegida pelo bloqueio local (Configurações > Proteção de branches)");

        var temUpstream = (await git.ExecutarBrutoAsync(Git.Em(repo).Comando("rev-parse").Opcao("--abbrev-ref").Opcao("--symbolic-full-name").Argumento("@{u}"), cancelar)).Sucesso;
        var args = Git.Em(repo).Comando("push");
        if (forcar) args = args.Opcao("--force-with-lease");
        if (!temUpstream)
        {
            if (!publicarNova) return Resultado.Falha<string>("a branch não tem upstream (marque \"publicar branch nova\")");
            args = args.Opcao("-u").Argumento("origin").Argumento(branch);
        }

        var saida = await git.ExecutarBrutoAsync(args.ComTimeout(ConsultasGit.TempoLongo), cancelar);
        return saida.Sucesso
            ? Resultado.Ok(temUpstream ? "enviado" : "branch publicada no origin (upstream definido)")
            : Resultado.Falha<string>(Traduzir(saida.TextoErro + "\n" + saida.Texto, forcar));
    }

    static string Traduzir(string texto, bool forcou)
    {
        bool Tem(string trecho) => texto.Contains(trecho, StringComparison.OrdinalIgnoreCase);
        if (Tem("rejected") || Tem("non-fast-forward") || Tem("stale info"))
            return "o origin recusou: há commits novos lá - faça pull ou rebase antes" +
                (forcou ? " (o --force-with-lease protegeu o trabalho dos outros)" : " (ou use force-with-lease se tiver certeza)");
        if (Tem("could not read from remote") || Tem("permission denied") || Tem("does not appear to be a git repository"))
            return "sem acesso ao origin (chave SSH/credenciais ou rede)";
        if (Tem("protected branch")) return "branch protegida no origin";
        return texto.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "falha desconhecida no push";
    }
}
