using System.Globalization;
using Menage.Core.Operacoes.Decoradores;
using Menage.Core.Operacoes.Excluir;

namespace Menage.Core.Operacoes.Limpeza;

/// <param name="Base">Vazia = a branch padrão do origin (HEAD do origin, main, master ou develop).</param>
/// <param name="Dias">Inatividade mínima, em dias, para a branch contar como inativa.</param>
/// <param name="Selecionadas">Chaves (<see cref="ItemDeLimpeza.Chave"/>) marcadas na prévia; só elas são excluídas.</param>
public sealed record OpcoesDeLimpeza(
    string Base = "", bool Mescladas = true, bool Inativas = false, int Dias = 90, bool Local = true, bool Remoto = false,
    bool Backup = true, string PrefixoDoBackup = "backup/", IReadOnlyList<string>? Selecionadas = null, bool ConfirmouExtra = false);

/// <summary>Uma branch candidata. Mescladas vêm marcadas, exceto as de mesmo commit da base; só-inativas vêm desmarcadas.</summary>
public sealed record ItemDeLimpeza(Projeto Projeto, AlvoDeExclusao Alvo, string Motivo, DateTime Ultimo, string Autor, bool Mesclada, bool Inativa, bool MarcarPadrao)
{
    public string Chave => Projeto.Caminho + "|" + Alvo.Nome;

    public int Dias => (int)(DateTime.Now - Ultimo).TotalDays;
}

public sealed record PreviaDeLimpeza(IReadOnlyList<ItemDeLimpeza> Itens, IReadOnlyList<string> Avisos);

public sealed record ResultadoDeLimpeza(int Excluidas, IReadOnlyList<string> Backups);

/// <summary>
/// Acha branches já mescladas na base e/ou inativas há N dias, em cada projeto, para excluir em massa. Ignora a base, a branch
/// atual, as protegidas e as de backup. A exclusão é a do Excluir (backup, exceto para as mescladas, cujo conteúdo continua existindo).
/// </summary>
public sealed class LimpezaEmLote(
    IProjetos projetos, IServicoDeDialogos dialogos, IProgresso progresso, IGit git, ConsultasGit consultas, ExclusorDeRefs exclusor, IRegrasDeProtecao regras)
    : OperacaoEmLote<OpcoesDeLimpeza, ResultadoDeLimpeza>(projetos, dialogos, progresso)
{
    static readonly string[] BasesPadrao = ["main", "master", "develop"];

    sealed record InfoRef(string Sha, DateTime Data, string Autor);

    sealed class InfoRamo
    {
        public InfoRef? Local { get; set; }

        public InfoRef? Remoto { get; set; }
    }

    public override string Id => "git.limpeza";

    public override string Nome => "Limpeza de branches";

    public override IReadOnlyList<Projeto> ProjetosAlvo(OpcoesDeLimpeza opcoes)
    {
        var marcadas = (opcoes.Selecionadas ?? []).Select(c => c.Split('|')[0]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. base.ProjetosAlvo(opcoes).Where(p => marcadas.Contains(p.Caminho))];
    }

    protected override Erro? Validar(OpcoesDeLimpeza opcoes, IReadOnlyList<Projeto> alvo) =>
        (opcoes.Selecionadas ?? []).Count == 0 ? new Erro("Nenhuma branch marcada", null, "Marque ao menos uma branch na prévia.")
        : !opcoes.Backup && !opcoes.ConfirmouExtra ? new Erro("Confirmação reforçada necessária", "A exclusão será feita sem backup.", "Confirme no diálogo.")
        : base.Validar(opcoes, alvo);

    public async Task<PreviaDeLimpeza> ListarAsync(IEnumerable<Projeto> alvo, OpcoesDeLimpeza opcoes, CancellationToken cancelar = default)
    {
        var itens = new List<ItemDeLimpeza>();
        var avisos = new List<string>();
        foreach (var projeto in alvo)
        {
            if (!ConsultasGit.EhRepositorio(projeto.Caminho)) { avisos.Add($"{projeto.Nome}: pasta do projeto não encontrada"); continue; }
            if (opcoes.Remoto && !(await git.ExecutarBrutoAsync(Git.Em(projeto.Caminho).Comando("fetch").Opcao("--prune").Argumento("origin").ComTimeout(ConsultasGit.TempoLongo), cancelar)).Sucesso)
                avisos.Add($"{projeto.Nome}: não foi possível atualizar com o origin; usei as referências que já estavam aqui");
            itens.AddRange(await ListarProjetoAsync(projeto, opcoes, avisos, cancelar));
        }

        return new(itens, avisos);
    }

    protected override async Task<ResultadoDoProjeto<ResultadoDeLimpeza>> ExecutarEmProjeto(Projeto projeto, OpcoesDeLimpeza opcoes, CancellationToken cancelar)
    {
        var marcadas = (opcoes.Selecionadas ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var itens = (await ListarAsync([projeto], opcoes, cancelar)).Itens.Where(i => marcadas.Contains(i.Chave)).ToList();
        if (itens.Count == 0) return ResultadoDoProjeto<ResultadoDeLimpeza>.Aviso("nenhuma das branches marcadas está mais disponível");

        var backup = new OpcoesDeBackup(opcoes.Backup, opcoes.PrefixoDoBackup, DateTime.Now);
        var (feitas, falhas, backups, partes) = (0, 0, new List<string>(), new List<string>());
        foreach (var item in itens)
        {
            var r = await exclusor.ExcluirAsync(projeto.Caminho, item.Alvo, backup, cancelar);
            backups.AddRange(r.Backups);
            if (r.Falhou) { falhas++; partes.Add($"{item.Alvo.Nome} - {(r.Detalhe.Length > 0 ? r.Detalhe : "falhou")}"); }
            else { feitas++; partes.Add($"{item.Alvo.Nome} ({string.Join(" + ", r.Feito)})"); }
        }

        var dados = new ResultadoDeLimpeza(feitas, backups);
        var texto = $"{feitas} branch(es) excluída(s): " + string.Join(", ", partes) + (backups.Count > 0 ? $" | backups: {string.Join(", ", backups)}" : "");
        return falhas > 0 ? ResultadoDoProjeto<ResultadoDeLimpeza>.Falha(texto, null, "Veja a Saída.") with { Dados = dados } : ResultadoDoProjeto<ResultadoDeLimpeza>.Ok(dados, texto);
    }

    async Task<List<ItemDeLimpeza>> ListarProjetoAsync(Projeto projeto, OpcoesDeLimpeza opcoes, List<string> avisos, CancellationToken cancelar)
    {
        var repo = projeto.Caminho;
        var (baseRef, baseSha) = await EscolherBaseAsync(repo, opcoes.Base.Trim(), cancelar);
        var baseNome = baseRef.Replace("refs/remotes/origin/", "", StringComparison.Ordinal).Replace("refs/heads/", "", StringComparison.Ordinal);
        if (opcoes.Mescladas && baseRef.Length == 0)
            avisos.Add($"{projeto.Nome}: branch base {(opcoes.Base.Length > 0 ? $"\"{opcoes.Base}\" " : "")}não encontrada; não dá para saber o que está mesclado" + (opcoes.Inativas ? " (só as inativas serão listadas)" : ""));

        var ramos = await LerRamosAsync(repo, cancelar);
        var mescladas = opcoes.Mescladas && baseSha.Length > 0 ? await LerMescladasAsync(repo, baseSha, cancelar) : new HashSet<string>();
        var atual = await consultas.BranchAtualAsync(repo, cancelar);
        var itens = new List<ItemDeLimpeza>();
        foreach (var (nome, ramo) in ramos.OrderBy(r => r.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (nome == baseNome || nome == atual || regras.Protegida(nome)) continue;
            if (opcoes.PrefixoDoBackup.Trim().Length > 0 && nome.StartsWith(opcoes.PrefixoDoBackup.Trim(), StringComparison.Ordinal)) continue;
            if (Candidata(projeto, nome, ramo, opcoes, baseSha, baseNome, mescladas) is { } item) itens.Add(item);
        }

        return itens;
    }

    static ItemDeLimpeza? Candidata(Projeto projeto, string nome, InfoRamo ramo, OpcoesDeLimpeza opcoes, string baseSha, string baseNome, HashSet<string> mescladasRefs)
    {
        bool noLocal = opcoes.Local && ramo.Local is not null, noRemoto = opcoes.Remoto && ramo.Remoto is not null;
        if (!noLocal && !noRemoto) return null;

        var mescl = opcoes.Mescladas && baseSha.Length > 0
            && (!noLocal || mescladasRefs.Contains("refs/heads/" + nome)) && (!noRemoto || mescladasRefs.Contains("refs/remotes/origin/" + nome));
        var mesmoCommit = mescl && ((noLocal && ramo.Local!.Sha == baseSha) || (noRemoto && ramo.Remoto!.Sha == baseSha));
        var recentes = new[] { noLocal ? ramo.Local : null, noRemoto ? ramo.Remoto : null }.OfType<InfoRef>().OrderByDescending(i => i.Data).First();
        var inativa = opcoes.Inativas && (DateTime.Now - recentes.Data).TotalDays >= opcoes.Dias;
        if (!mescl && !inativa) return null;

        var alvo = new AlvoDeExclusao(nome)
        {
            Tipo = TipoDeRefAlvo.Branch, ExcluirLocal = noLocal, ExcluirRemoto = noRemoto,
            ShaLocal = ramo.Local?.Sha ?? "", ShaRemoto = ramo.Remoto?.Sha ?? "", DispensaBackup = mescl,
        };
        var motivos = new List<string>();
        if (mescl) motivos.Add(mesmoCommit ? $"mesclada em {baseNome} (mesmo commit da base)" : $"mesclada em {baseNome}");
        if (inativa) motivos.Add($"sem commits há {(int)(DateTime.Now - recentes.Data).TotalDays} dias");
        return new(projeto, alvo, string.Join(" + ", motivos), recentes.Data, recentes.Autor, mescl, inativa, mescl && !mesmoCommit);
    }

    async Task<(string Ref, string Sha)> EscolherBaseAsync(string repo, string pedida, CancellationToken cancelar)
    {
        var candidatas = new List<string>();
        if (pedida.Length > 0) candidatas.AddRange(["refs/remotes/origin/" + pedida, "refs/heads/" + pedida]);
        else
        {
            var padrao = (await git.ExecutarBrutoAsync(Git.Em(repo).Comando("symbolic-ref").Opcao("--quiet").Argumento("refs/remotes/origin/HEAD"), cancelar)).PrimeiraLinha().Trim();
            if (padrao.Length > 0) candidatas.Add(padrao);
            foreach (var n in BasesPadrao) candidatas.Add("refs/remotes/origin/" + n);
        }

        foreach (var candidata in candidatas)
        {
            var sha = await git.ExecutarBrutoAsync(Git.Em(repo).Comando("rev-parse").Opcao("--verify").Opcao("--quiet").Argumento(candidata + "^{commit}"), cancelar);
            if (sha.Sucesso && sha.PrimeiraLinha().Trim() is { Length: > 0 } hash) return (candidata, hash);
        }

        return ("", "");
    }

    async Task<Dictionary<string, InfoRamo>> LerRamosAsync(string repo, CancellationToken cancelar)
    {
        var ramos = new Dictionary<string, InfoRamo>(StringComparer.Ordinal);
        var lista = await git.ExecutarBrutoAsync(Git.Em(repo).Comando("for-each-ref").Opcao("--format=%(refname)%09%(objectname)%09%(committerdate:unix)%09%(authorname)").Argumento("refs/heads").Argumento("refs/remotes/origin"), cancelar);
        foreach (var p in lista.Linhas().Select(l => l.Split('\t')).Where(p => p.Length >= 3))
        {
            var ehLocal = p[0].StartsWith("refs/heads/", StringComparison.Ordinal);
            var nome = ehLocal ? p[0][11..] : p[0].StartsWith("refs/remotes/origin/", StringComparison.Ordinal) ? p[0][20..] : "";
            if (nome.Length == 0 || nome == "HEAD") continue;

            _ = long.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix);
            var info = new InfoRef(p[1], DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime, p.Length > 3 ? p[3] : "");
            if (!ramos.TryGetValue(nome, out var ramo)) ramos[nome] = ramo = new InfoRamo();
            if (ehLocal) ramo.Local = info; else ramo.Remoto = info;
        }

        return ramos;
    }

    async Task<HashSet<string>> LerMescladasAsync(string repo, string baseSha, CancellationToken cancelar)
    {
        var lista = await git.ExecutarBrutoAsync(Git.Em(repo).Comando("for-each-ref").Opcao("--merged=" + baseSha).Opcao("--format=%(refname)").Argumento("refs/heads").Argumento("refs/remotes/origin"), cancelar);
        return [.. lista.Linhas().Select(l => l.Trim())];
    }
}
