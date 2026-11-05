using Menage.Core.Processos;

namespace Menage.Core.PullRequests;

/// <summary>Validar um PR e mesclar os validados na main local. Escrito como extensões do serviço para manter cada arquivo pequeno.</summary>
public static class ValidacaoEMescla
{
    /// <summary>
    /// Merge do PR com a origin/main numa cópia temporária (git worktree) + comando de compilação do resultado.
    /// Não mexe na pasta das fontes (só em refs temporárias). Devolve o PR com <c>Valido</c> preenchido.
    /// </summary>
    public static async Task<PullRequest> ValidarAsync(this ServicoDePullRequests servico, string fontes, PullRequest pr, IProgress<LinhaDoPr> log, CancellationToken cancelar = default)
    {
        var principal = servico.Configuracao.BranchPrincipal;
        var origemPrincipal = $"refs/remotes/origin/{principal}";
        pr = pr with { Valido = false, ShaValidado = "" };
        log.Report(new LinhaDoPr(TipoDeLinha.Titulo, $"PR #{pr.Numero}: {pr.Titulo}"));
        var baixou = await servico.Git_(fontes, cancelar, "fetch", "--quiet", "origin", $"+refs/heads/{principal}:{origemPrincipal}", $"+refs/pull/{pr.Numero}/head:{pr.Ref}");
        if (!baixou.EhOk) return Falhou(pr, "não baixou o PR: " + PrimeiraLinha(baixou.Erro.ToString()), log);
        var sha = (await servico.Git_(fontes, cancelar, "rev-parse", pr.Ref)).ValorOuTexto();
        if (pr.Sha.Length > 0 && sha != pr.Sha) log.Report(new LinhaDoPr(TipoDeLinha.Aviso, $"O PR recebeu commits novos; validando o último ({Curto(sha)})."));
        pr = pr with { Sha = sha };
        try
        {
            if ((await servico.Git_(fontes, cancelar, "merge-base", "--is-ancestor", sha, origemPrincipal)).EhOk)
            {
                pr = pr with { Validacao = $"já está na {principal}" };
                log.Report(new LinhaDoPr(TipoDeLinha.Aviso, pr.Validacao));
                return pr;
            }

            return await ValidarNaCopiaAsync(servico, fontes, pr, origemPrincipal, log, cancelar);
        }
        finally
        {
            await servico.ApagarRefsAsync(fontes, CancellationToken.None);
        }
    }

    static async Task<PullRequest> ValidarNaCopiaAsync(ServicoDePullRequests servico, string fontes, PullRequest pr, string origemPrincipal, IProgress<LinhaDoPr> log, CancellationToken cancelar)
    {
        var copia = Path.Combine(servico.PastaTemporaria ?? Path.GetTempPath(), "gpr" + pr.Numero); // caminho curto (MAX_PATH)
        await LimparCopiaAsync(servico, fontes, copia);
        try
        {
            var criou = await servico.Git_(fontes, cancelar, "worktree", "add", "--detach", copia, origemPrincipal);
            if (!criou.EhOk) return Falhou(pr, "não criou a cópia temporária: " + PrimeiraLinha(criou.Erro.ToString()), log);
            var resumo = await servico.Git_(fontes, cancelar, "diff", "--shortstat", $"{origemPrincipal}...{pr.Sha}");
            if (resumo.EhOk && resumo.Valor.Texto.Trim().Length > 0) log.Report(new LinhaDoPr(TipoDeLinha.Discreto, resumo.Valor.Texto.Trim()));

            var mescla = await servico.Git_(copia, cancelar, "merge", "--no-ff", "--no-edit", pr.Sha);
            if (!mescla.EhOk)
            {
                var conflitos = await servico.Git_(copia, cancelar, "diff", "--name-only", "--diff-filter=U");
                await servico.Git_(copia, CancellationToken.None, "merge", "--abort");
                var nomes = conflitos.EhOk ? conflitos.Valor.Texto.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];
                return Falhou(pr, nomes.Length > 0 ? $"conflito com a main: {string.Join(", ", nomes.Take(4))}{(nomes.Length > 4 ? "..." : "")}" : "merge falhou: " + PrimeiraLinha(mescla.Erro.ToString()), log);
            }

            log.Report(new LinhaDoPr(TipoDeLinha.Info, $"Merge com a main sem conflito. Compilando ({servico.Configuracao.ComandoDeValidacao})..."));
            var build = await CompilarAsync(servico, copia, log, cancelar);
            if (build is not null) return Falhou(pr, "não compila: " + build, log);
            pr = pr with { Valido = true, ShaValidado = pr.Sha, Validacao = "ok: mescla sem conflito e compila" };
            log.Report(new LinhaDoPr(TipoDeLinha.Sucesso, pr.Validacao));
            return pr;
        }
        finally
        {
            await LimparCopiaAsync(servico, fontes, copia);
        }
    }

    /// <summary>Roda o comando de validação na cópia. Devolve nulo se passou, ou a primeira mensagem de erro.</summary>
    static async Task<string?> CompilarAsync(ServicoDePullRequests servico, string copia, IProgress<LinhaDoPr> log, CancellationToken cancelar)
    {
        var partes = servico.Configuracao.ComandoDeValidacao.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length == 0) return "o comando de validação está vazio (ajuste em Configurar)";
        var saida = await servico.Runner.ExecutarAsync(new ComandoProcesso(partes[0], partes[1..], copia, Timeout: TimeSpan.FromMinutes(15)), cancelar);
        if (saida.FalhaAoIniciar is not null) return "não consegui rodar o comando: " + saida.FalhaAoIniciar;
        if (saida.Expirou) return "tempo esgotado na compilação";
        var linhas = (saida.Texto + "\n" + saida.TextoErro).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (saida.Sucesso) return null;
        var erros = linhas.Where(l => l.Contains(" error ", StringComparison.OrdinalIgnoreCase)).Distinct().ToList();
        foreach (var l in (erros.Count > 0 ? erros.Take(15) : linhas.TakeLast(8))) log.Report(new LinhaDoPr(TipoDeLinha.Erro, l));
        return erros.Count > 0 ? erros[0] : linhas.Length > 0 ? linhas[^1] : $"código de saída {saida.CodigoDeSaida}";
    }

    static async Task LimparCopiaAsync(ServicoDePullRequests servico, string fontes, string copia)
    {
        if (Directory.Exists(copia)) await servico.Git_(fontes, CancellationToken.None, "worktree", "remove", "--force", copia);
        try
        {
            if (Directory.Exists(copia))
            {
                foreach (var f in Directory.EnumerateFiles(copia, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(copia, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* o worktree prune abaixo limpa o registro */ }

        await servico.Git_(fontes, CancellationToken.None, "worktree", "prune");
    }

    /// <summary>
    /// Na pasta das fontes: main = origin/main + merge de cada PR validado (--no-ff). Qualquer falha antes do fim devolve a main como estava.
    /// Só envia ao GitHub com <paramref name="enviar"/>; sem isso fica na main local para o dono revisar.
    /// </summary>
    public static async Task<Resultado<ResultadoDaMescla>> MesclarAsync(this ServicoDePullRequests servico, string fontes, IReadOnlyList<PullRequest> prs, bool enviar, IProgress<LinhaDoPr> log, CancellationToken cancelar = default)
    {
        if (prs.Count == 0) return Resultado.Falha<ResultadoDaMescla>("Nenhum PR marcado");
        if (prs.FirstOrDefault(p => !p.ProntoParaMesclar) is { } pendente)
            return Resultado.Falha<ResultadoDaMescla>($"O PR #{pendente.Numero} não está validado", "Ele não passou na validação, ou recebeu commit depois dela.", "Valide de novo.");
        var principal = servico.Configuracao.BranchPrincipal;
        var origemPrincipal = $"refs/remotes/origin/{principal}";

        var sujo = await servico.Git_(fontes, cancelar, "status", "--porcelain", "--untracked-files=no");
        if (sujo.EhOk && sujo.Valor.Texto.Trim().Length > 0)
            return Resultado.Falha<ResultadoDaMescla>("A pasta das fontes tem arquivos alterados sem commit", PrimeiraLinha(sujo.Valor.Texto), "Faça commit ou stash antes de mesclar.");
        var original = (await servico.Git_(fontes, cancelar, "rev-parse", "--abbrev-ref", "HEAD")).ValorOuTexto();

        var specs = new List<string> { "fetch", "--quiet", "origin", $"+refs/heads/{principal}:{origemPrincipal}" };
        specs.AddRange(prs.Select(p => $"+refs/pull/{p.Numero}/head:{p.Ref}"));
        var baixou = await servico.Git_(fontes, cancelar, [.. specs]);
        if (!baixou.EhOk) return Resultado.Falha<ResultadoDaMescla>("O fetch falhou", baixou.Erro.ToString(), "Confira a rede.");
        foreach (var p in prs)
        {
            var agora = (await servico.Git_(fontes, cancelar, "rev-parse", p.Ref)).ValorOuTexto();
            if (agora != p.ShaValidado) return Resultado.Falha<ResultadoDaMescla>($"O PR #{p.Numero} recebeu commits depois da validação", null, "Valide de novo.");
        }

        var temMain = (await servico.Git_(fontes, cancelar, "rev-parse", "--verify", "--quiet", $"refs/heads/{principal}")).EhOk;
        var foiParaMain = temMain ? await servico.Git_(fontes, cancelar, "checkout", "-q", principal) : await servico.Git_(fontes, cancelar, "checkout", "-q", "-b", principal, origemPrincipal);
        if (!foiParaMain.EhOk) return Resultado.Falha<ResultadoDaMescla>($"Não consegui ir para a {principal}", foiParaMain.Erro.ToString());
        var antes = (await servico.Git_(fontes, cancelar, "rev-parse", "HEAD")).ValorOuTexto();
        var desfazer = false;
        try
        {
            if (!(await servico.Git_(fontes, cancelar, "merge", "--ff-only", origemPrincipal)).EhOk)
                return Resultado.Falha<ResultadoDaMescla>($"A {principal} local tem commits que não estão no origin", null, "Envie ou desfaça esses commits antes de mesclar.");
            desfazer = true;
            foreach (var p in prs)
            {
                var de = p.Autor.Length > 0 ? $" from {p.Autor}{(p.Branch.Length > 0 ? "/" + p.Branch : "")}" : "";
                log.Report(new LinhaDoPr(TipoDeLinha.Info, $"Mesclando o PR #{p.Numero} na {principal}..."));
                var m = await servico.Git_(fontes, cancelar, "merge", "--no-ff", "-m", $"Merge pull request #{p.Numero}{de}", "-m", p.Titulo, p.ShaValidado);
                if (m.EhOk) continue;
                await servico.Git_(fontes, CancellationToken.None, "merge", "--abort");
                return Resultado.Falha<ResultadoDaMescla>($"Conflito ao mesclar o PR #{p.Numero} junto com os anteriores", "Nada foi alterado.", "Mescle esse PR separado.");
            }

            var numeros = string.Join(", ", prs.Select(p => "#" + p.Numero));
            if (!enviar)
            {
                desfazer = false;
                return Resultado.Ok(new ResultadoDaMescla(false, $"{prs.Count} PR(s) ({numeros}) mesclados na {principal} local; nada foi enviado ao GitHub. Para publicar: git push origin {principal}."));
            }

            log.Report(new LinhaDoPr(TipoDeLinha.Info, $"Enviando a {principal} ao GitHub..."));
            var push = await servico.Git_(fontes, cancelar, "push", "origin", principal);
            if (!push.EhOk) return Resultado.Falha<ResultadoDaMescla>("O push foi recusado", push.Erro.ToString(), $"Nada foi enviado; a {principal} local voltou ao que era.");
            desfazer = false;
            return Resultado.Ok(new ResultadoDaMescla(true, $"{prs.Count} PR(s) ({numeros}) mesclados e enviados ao GitHub."));
        }
        finally
        {
            if (desfazer)
            {
                await servico.Git_(fontes, CancellationToken.None, "merge", "--abort");
                await servico.Git_(fontes, CancellationToken.None, "reset", "-q", "--hard", antes);
                log.Report(new LinhaDoPr(TipoDeLinha.Aviso, $"A {principal} local voltou para {Curto(antes)}."));
            }

            if (original.Length > 0 && original != principal && original != "HEAD") await servico.Git_(fontes, CancellationToken.None, "checkout", "-q", original);
            await servico.ApagarRefsAsync(fontes, CancellationToken.None);
        }
    }

    static PullRequest Falhou(PullRequest pr, string motivo, IProgress<LinhaDoPr> log)
    {
        log.Report(new LinhaDoPr(TipoDeLinha.Erro, motivo));
        return pr with { Valido = false, Validacao = motivo };
    }

    static string ValorOuTexto(this Resultado<SaidaProcesso> r) => r.EhOk ? r.Valor.Texto.Trim() : "";

    static string PrimeiraLinha(string texto) => texto.Split('\n', StringSplitOptions.TrimEntries)[0];

    static string Curto(string sha) => sha.Length > 9 ? sha[..9] : sha;
}
