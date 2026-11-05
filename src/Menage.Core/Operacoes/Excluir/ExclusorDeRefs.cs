using Menage.Core.Operacoes.Decoradores;

namespace Menage.Core.Operacoes.Excluir;

public enum TipoDeRefAlvo { Nenhum, Branch, Tag }

/// <summary>Um nome em um projeto: o que é (branch ou tag), onde existe e o que será excluído.</summary>
public sealed record AlvoDeExclusao(string Nome)
{
    public TipoDeRefAlvo Tipo { get; init; }

    public bool ExcluirLocal { get; init; }

    public bool ExcluirRemoto { get; init; }

    public string ShaLocal { get; init; } = "";

    public string ShaRemoto { get; init; } = "";

    /// <summary>Já está mesclada em outra branch (limpeza): o conteúdo continua existindo, sem backup.</summary>
    public bool DispensaBackup { get; init; }

    public string Aviso { get; init; } = "";

    public string Erro { get; init; } = "";

    public bool Nada => !ExcluirLocal && !ExcluirRemoto;

    public string Rotulo => (Tipo == TipoDeRefAlvo.Tag ? "tag " : "branch ") + Nome;
}

/// <summary>Branches e tags do origin (consulta ao vivo com <c>ls-remote</c>); <see cref="Erro"/> preenchido se não foi possível consultar.</summary>
public sealed record RefsDoOrigin(IReadOnlyDictionary<string, string> Branches, IReadOnlyDictionary<string, string> Tags, string Erro = "");

/// <param name="Backup">Antes de apagar, copia o conteúdo para uma branch nova; se o backup falhar nada é apagado.</param>
/// <param name="Prefixo">Prefixo da branch de backup (ex.: <c>backup/</c>).</param>
public sealed record OpcoesDeBackup(bool Backup, string Prefixo, DateTime Data);

public sealed record ResultadoDaExclusao(IReadOnlyList<string> Feito, IReadOnlyList<string> Backups, bool Falhou, string Detalhe = "");

/// <summary>Avalia e exclui branches e tags (local e/ou origin) com backup. Usado por Excluir e pela Limpeza.</summary>
public sealed class ExclusorDeRefs(IGit git, ConsultasGit consultas, IRegrasDeProtecao regras)
{
    public async Task<RefsDoOrigin> LerOriginAsync(string repo, CancellationToken cancelar)
    {
        var saida = await git.ExecutarBrutoAsync(Git.Em(repo).Comando("ls-remote").Argumento("origin").ComTimeout(ConsultasGit.TempoLongo), cancelar);
        if (!saida.Sucesso)
        {
            var motivo = saida.TextoErro.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
            return new(new Dictionary<string, string>(), new Dictionary<string, string>(), "não foi possível consultar o origin" + (motivo is null ? "" : ": " + motivo));
        }

        var branches = new Dictionary<string, string>(StringComparer.Ordinal);
        var tags = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var partes in saida.Linhas().Select(l => l.Split('\t')).Where(p => p.Length >= 2))
        {
            if (partes[1].StartsWith("refs/heads/", StringComparison.Ordinal)) branches[partes[1][11..]] = partes[0];
            else if (partes[1].StartsWith("refs/tags/", StringComparison.Ordinal)) GuardarTag(tags, partes[1][10..], partes[0]);
        }

        return new(branches, tags);
    }

    /// <summary>Se o nome não existe como branch nos modos escolhidos, procura uma tag (branch tem prioridade).</summary>
    public async Task<AlvoDeExclusao> AvaliarAsync(string repo, string nome, bool local, bool remoto, RefsDoOrigin? origin, string atual, CancellationToken cancelar)
    {
        var alvo = new AlvoDeExclusao(nome);
        var comOrigin = remoto && origin is not null;
        if (remoto && origin?.Erro.Length > 0 && !local) return alvo with { Erro = origin.Erro };

        bool bl = await consultas.RefExisteAsync(repo, "refs/heads/" + nome, cancelar), tl = await consultas.RefExisteAsync(repo, "refs/tags/" + nome, cancelar);
        bool br = comOrigin && origin!.Branches.ContainsKey(nome), tr = comOrigin && origin!.Tags.ContainsKey(nome);
        var tipo = (local && bl) || br ? TipoDeRefAlvo.Branch : (local && tl) || tr ? TipoDeRefAlvo.Tag : TipoDeRefAlvo.Nenhum;
        if (tipo == TipoDeRefAlvo.Nenhum) return alvo with { Aviso = await NaoEncontradoAsync(repo, nome, local, remoto, bl || tl, origin, cancelar) };

        var ehBranch = tipo == TipoDeRefAlvo.Branch;
        var (existeLocal, existeRemoto) = ehBranch ? (bl, br) : (tl, tr);
        alvo = alvo with { Tipo = tipo, ExcluirLocal = local && existeLocal, ExcluirRemoto = remoto && existeRemoto };
        alvo = alvo with
        {
            ShaLocal = existeLocal ? (await Bruto(repo, cancelar, "rev-parse", "--verify", "--quiet", (ehBranch ? "refs/heads/" : "refs/tags/") + nome + "^{commit}")).Trim() : "",
            ShaRemoto = existeRemoto ? (ehBranch ? origin!.Branches[nome] : origin!.Tags[nome]) : "",
        };
        return Restringir(alvo, ehBranch, atual, origin);
    }

    /// <summary>Apaga o alvo. Com backup, o conteúdo vai antes para uma branch nova; se o backup falhar nada é apagado.</summary>
    public async Task<ResultadoDaExclusao> ExcluirAsync(string repo, AlvoDeExclusao alvo, OpcoesDeBackup backup, CancellationToken cancelar)
    {
        var backups = new List<string>();
        if (backup.Backup && !alvo.DispensaBackup)
        {
            var erro = await FazerBackupAsync(repo, alvo, backup, backups, cancelar);
            if (erro is not null) return new([], backups, true, erro);
        }

        var feito = new List<string>();
        var falhou = false;
        var detalhe = "";
        var ehTag = alvo.Tipo == TipoDeRefAlvo.Tag;
        if (alvo.ExcluirRemoto)
        {
            var push = await git.ExecutarBrutoAsync(Git.Em(repo).Comando("push").Argumento("origin").Argumento(":refs/" + (ehTag ? "tags/" : "heads/") + alvo.Nome).ComTimeout(ConsultasGit.TempoLongo), cancelar);
            if (push.Sucesso)
            {
                feito.Add("remoto");
                if (!ehTag) await Bruto(repo, cancelar, "update-ref", "-d", "refs/remotes/origin/" + alvo.Nome);
            }
            else (falhou, detalhe) = (true, $"o origin recusou a exclusão de {alvo.Rotulo} (branch protegida ou sem permissão?)");
        }

        if (alvo.ExcluirLocal)
        {
            var apagar = await git.ExecutarBrutoAsync(ehTag ? Git.Em(repo).Comando("tag").Opcao("-d").Argumento(alvo.Nome) : Git.Em(repo).Comando("branch").Opcao("-D").Argumento(alvo.Nome), cancelar);
            if (apagar.Sucesso) feito.Add("local");
            else (falhou, detalhe) = (true, detalhe.Length > 0 ? detalhe : apagar.TextoErro.Trim());
        }

        return new(feito, backups, falhou, detalhe);
    }

    async Task<string?> FazerBackupAsync(string repo, AlvoDeExclusao alvo, OpcoesDeBackup opcoes, List<string> criados, CancellationToken cancelar)
    {
        var ehTag = alvo.Tipo == TipoDeRefAlvo.Tag;
        var nomeBase = opcoes.Prefixo.Trim() + (ehTag ? "tag-" : "") + alvo.Nome.Replace('/', '-') + "-" + opcoes.Data.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        string shaLocal = alvo.ExcluirLocal ? alvo.ShaLocal : "", shaRemoto = alvo.ExcluirRemoto ? alvo.ShaRemoto : "";
        if (shaRemoto.Length > 0 && !(await git.ExecutarBrutoAsync(Git.Em(repo).Comando("cat-file").Opcao("-e").Argumento(shaRemoto + "^{commit}"), cancelar)).Sucesso)
            await Bruto(repo, cancelar, "fetch", "origin", (ehTag ? "refs/tags/" : "refs/heads/") + alvo.Nome);

        var copias = new List<(string Nome, string Sha)>();
        if (shaLocal.Length > 0) copias.Add((nomeBase, shaLocal));
        if (shaRemoto.Length > 0 && shaRemoto != shaLocal) copias.Add((shaLocal.Length > 0 ? nomeBase + "-origin" : nomeBase, shaRemoto));
        if (copias.Count == 0) return "não foi possível criar/enviar o backup; nada foi excluído";

        foreach (var (nome, sha) in copias)
        {
            if (!await consultas.NomeDeBranchValidoAsync(nome, cancelar)) return $"nome de backup inválido ({nome}); nada foi excluído";
            if (!(await git.ExecutarBrutoAsync(Git.Em(repo).Comando("branch").Argumento(nome).Argumento(sha), cancelar)).Sucesso)
                return "não foi possível criar o backup; nada foi excluído";
            criados.Add(nome);
        }

        return alvo.ExcluirRemoto ? await EnviarBackupsAsync(repo, criados, cancelar) : null;
    }

    async Task<string?> EnviarBackupsAsync(string repo, List<string> backups, CancellationToken cancelar)
    {
        foreach (var nome in backups)
        {
            var push = await git.ExecutarBrutoAsync(Git.Em(repo).Comando("push").Argumento("origin").Argumento($"refs/heads/{nome}:refs/heads/{nome}").ComTimeout(ConsultasGit.TempoLongo), cancelar);
            if (!push.Sucesso) return "não foi possível enviar o backup ao origin; nada foi excluído";
        }

        return null;
    }

    AlvoDeExclusao Restringir(AlvoDeExclusao alvo, bool ehBranch, string atual, RefsDoOrigin? origin)
    {
        if (ehBranch && regras.ProtegidaParaExclusao(alvo.Nome) && !alvo.Nada)
            alvo = alvo with { ExcluirLocal = false, ExcluirRemoto = false, Aviso = "BLOQUEADA: branch protegida (Configurações > Proteção de branches)" };
        if (ehBranch && alvo.ExcluirLocal && atual == alvo.Nome)
            alvo = alvo with { ExcluirLocal = false, Aviso = "é a branch atual: faça checkout em outra para excluir localmente" };
        if (origin?.Erro.Length > 0 && alvo.ExcluirRemoto is false)
            alvo = alvo with { Aviso = alvo.Aviso + (alvo.Aviso.Length > 0 ? "; " : "") + origin.Erro };
        return alvo.Nada && alvo.Aviso.Length == 0 ? alvo with { Aviso = "nada para excluir nos modos escolhidos" } : alvo;
    }

    async Task<string> NaoEncontradoAsync(string repo, string nome, bool local, bool remoto, bool existeLocal, RefsDoOrigin? origin, CancellationToken cancelar)
    {
        const string baseTexto = "não encontrado como branch nem como tag";
        if (remoto && !local && existeLocal) return baseTexto + " no origin (existe só neste computador: marque Local)";
        if (local && !remoto && await consultas.RefExisteAsync(repo, "refs/remotes/origin/" + nome, cancelar)) return baseTexto + " neste computador (existe no origin: marque Remoto)";
        return remoto && origin?.Erro.Length > 0 ? $"{baseTexto} ({origin.Erro})" : baseTexto;
    }

    // Tag anotada: o commit apontado vem na linha com ^{}.
    static void GuardarTag(Dictionary<string, string> tags, string nome, string sha)
    {
        if (nome.EndsWith("^{}", StringComparison.Ordinal)) tags[nome[..^3]] = sha;
        else tags.TryAdd(nome, sha);
    }

    async Task<string> Bruto(string repo, CancellationToken cancelar, params string[] args)
    {
        var comando = Git.Em(repo).Comando(args[0]);
        foreach (var a in args.Skip(1)) comando = comando.Argumento(a);
        return (await git.ExecutarBrutoAsync(comando, cancelar)).PrimeiraLinha();
    }
}
