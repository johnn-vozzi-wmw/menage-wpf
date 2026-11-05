using System.Globalization;
using Menage.Core.Operacoes.Checkout;

namespace Menage.Core.Melhorias;

/// <summary>Uma linha da prévia: o que a ativação fará em cada projeto (somente leitura).</summary>
public sealed record LinhaDeAtivacao(string Projeto, string Atual, string Destino, string Base, string Situacao, int Pendencias, bool Problema, bool JaEsta);

/// <summary>
/// Ativa uma melhoria: troca cada projeto dela para a branch definida (pendências em auto-stash, que volta ao retornar à branch),
/// confere o resultado, aplica o perfil de banco, grava version.conf/sqlite se ligados e guarda o estado de antes para o Desvincular.
/// Projetos fora da melhoria ficam como estão.
/// </summary>
public sealed class AtivadorDeMelhorias(
    RepositorioDeMelhorias repositorio, IProjetos projetos, IGit git, ConsultasGit consultas, AutoStash autoStash,
    IContextoDeBanco banco, SqliteDeAtividade sqlite)
{
    Projeto? Achar(string nome) => projetos.Todos.FirstOrDefault(p => p.Nome.Equals(nome, StringComparison.OrdinalIgnoreCase));

    static string Agora() => DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    public async Task<IReadOnlyList<LinhaDeAtivacao>> PreviaAsync(Melhoria m, CancellationToken cancelar = default)
    {
        var linhas = new List<LinhaDeAtivacao>();
        foreach (var b in m.Branches)
        {
            if (Achar(b.Projeto) is not { } p)
            {
                linhas.Add(new(b.Projeto, "", b.Branch, b.Base, "projeto não encontrado na configuração", 0, true, false));
                continue;
            }

            var atual = await consultas.BranchAtualAsync(p.Caminho, cancelar);
            var pendencias = (await consultas.PendenciasAsync(p.Caminho, cancelar)).Count(l => !l.StartsWith("!!", StringComparison.Ordinal));
            var jaEsta = atual == b.Branch;
            var alvo = jaEsta ? null : await consultas.ResolverCheckoutAsync(p.Caminho, b.Branch, cancelar);
            var situacao = jaEsta ? "já está nela"
                : alvo is not null ? $"trocar ({alvo.Descricao})"
                : b.Base.Length > 0 ? $"não existe: será criada a partir de {b.Base}"
                : "não achada localmente: será buscada no origin";
            linhas.Add(new(b.Projeto, atual, b.Branch, b.Base, situacao, pendencias, false, jaEsta));
        }

        return linhas;
    }

    /// <summary>Cria as branches da melhoria que ainda não existem (a partir da base de cada projeto); as existentes são adotadas.</summary>
    public async Task<ResumoDeMelhoria> CriarBranchesAsync(Melhoria m, bool publicar, CancellationToken cancelar = default)
    {
        var res = new ResumoDeMelhoria($"Criar branches da melhoria {m.Id}");
        foreach (var b in m.Branches)
        {
            if (Achar(b.Projeto) is not { } p) { res.Erro($"{b.Projeto}: projeto não encontrado na configuração"); continue; }
            if (await consultas.BranchExisteAsync(p.Caminho, b.Branch, cancelar)) { res.Info($"{p.Nome}: a branch \"{b.Branch}\" já existe - adotada"); continue; }
            await CriarAsync(p, b, publicar, res, cancelar);
        }

        return res;
    }

    async Task<bool> CriarAsync(Projeto p, BranchDaMelhoria b, bool publicar, ResumoDeMelhoria res, CancellationToken cancelar)
    {
        var baseRef = b.Base.Length == 0 ? "HEAD" : (await consultas.ResolverBaseAsync(p.Caminho, b.Base, cancelar))?.Ref;
        if (baseRef is null) { res.Erro($"{p.Nome}: base \"{b.Base}\" não existe"); return false; }
        var criar = await git.ExecutarAsync(Git.Em(p.Caminho).Comando("checkout").Opcao("--no-track").Opcao("-b").Argumento(b.Branch).Argumento(baseRef), cancelar);
        if (!criar.EhOk) { res.Erro($"{p.Nome}: não foi possível criar \"{b.Branch}\": {criar.Erro}"); return false; }
        if (publicar)
        {
            var push = await git.ExecutarAsync(Git.Em(p.Caminho).Comando("push").Opcao("-u").Argumento("origin").Argumento(b.Branch).ComTimeout(ConsultasGit.TempoLongo), cancelar);
            if (!push.EhOk) { res.Aviso($"{p.Nome}: \"{b.Branch}\" criada, mas não publicada: {push.Erro}"); return true; }
        }

        res.Ok($"{p.Nome}: \"{b.Branch}\" criada a partir de {(b.Base.Length > 0 ? b.Base : "HEAD")}" + (publicar ? " e publicada" : ""));
        return true;
    }

    async Task<bool> TrocarAsync(Projeto p, string destino, string baseRef, ResumoDeMelhoria res, CancellationToken cancelar)
    {
        var guardou = await autoStash.GuardarAsync(p.Caminho, destino, cancelar);
        var alvo = await consultas.ResolverCheckoutAsync(p.Caminho, destino, cancelar);
        if (alvo is null)
        {
            await consultas.AtualizarRefsAsync(p.Caminho, cancelar);
            alvo = await consultas.ResolverCheckoutAsync(p.Caminho, destino, cancelar);
        }

        if (alvo is null && baseRef.Length > 0)
            return await CriarAsync(p, new BranchDaMelhoria { Projeto = p.Nome, Branch = destino, Base = baseRef }, false, res, cancelar) && await RestaurarAsync(p, destino, res, cancelar);
        if (alvo is null) { res.Erro($"{p.Nome}: \"{destino}\" não existe (branch local, origin ou tag) e a melhoria não define base"); return false; }

        var args = alvo.Tipo switch
        {
            TipoDeRef.Tag => Git.Em(p.Caminho).Comando("checkout").Argumento(alvo.Ref),
            TipoDeRef.BranchLocal => Git.Em(p.Caminho).Comando("switch").Argumento(destino),
            _ => Git.Em(p.Caminho).Comando("switch").Opcao("-c").Argumento(destino).Opcao("--track").Argumento(alvo.Ref),
        };
        var r = await git.ExecutarAsync(args, cancelar);
        if (!r.EhOk) { res.Erro($"{p.Nome}: checkout de \"{destino}\" falhou: {r.Erro}"); return false; }

        if (guardou.Nota is not null) res.Info($"{p.Nome}: {guardou.Nota}");
        res.Ok($"{p.Nome}: {destino} ({alvo.Descricao})");
        return await RestaurarAsync(p, destino, res, cancelar);
    }

    async Task<bool> RestaurarAsync(Projeto p, string branch, ResumoDeMelhoria res, CancellationToken cancelar)
    {
        var volta = await autoStash.RestaurarAsync(p.Caminho, branch, cancelar);
        if (volta.Nota is not null) (volta.Falhou ? (Action<string>)res.Aviso : res.Info).Invoke($"{p.Nome}: {volta.Nota}");
        return true;
    }

    public async Task<ResumoDeMelhoria> AtivarAsync(Melhoria m, bool aplicarBanco, CancellationToken cancelar = default)
    {
        var res = new ResumoDeMelhoria($"Melhoria {m.Id}" + (m.Titulo.Length > 0 ? " - " + m.Titulo : ""));
        var config = repositorio.Config;
        var antes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in projetos.Todos) antes[p.Nome] = await consultas.BranchAtualAsync(p.Caminho, cancelar);
        var bancoAntes = banco.AtivoNoContexto();

        foreach (var b in m.Branches)
        {
            if (cancelar.IsCancellationRequested) { res.Parado = true; res.Aviso("Parado pelo usuário."); break; }
            if (Achar(b.Projeto) is not { } p) { res.Erro($"{b.Projeto}: projeto não encontrado na configuração"); continue; }
            if (await consultas.BranchAtualAsync(p.Caminho, cancelar) == b.Branch) { res.Ok($"{p.Nome}: já estava em {b.Branch}"); continue; }
            await TrocarAsync(p, b.Branch, b.Base, res, cancelar);
        }

        if (res.Parado) return res;
        await ConferirAsync(m, res, cancelar);
        if (aplicarBanco) await AplicarBancoAsync(m, res, cancelar);
        await GravarEstadoAsync(m, res, config, antes, bancoAntes, cancelar);
        return res;
    }

    async Task ConferirAsync(Melhoria m, ResumoDeMelhoria res, CancellationToken cancelar)
    {
        var certos = 0;
        foreach (var b in m.Branches)
        {
            if (Achar(b.Projeto) is not { } p) continue;
            var atual = await consultas.BranchAtualAsync(p.Caminho, cancelar);
            if (atual == b.Branch) certos++;
            else res.Erro($"{p.Nome}: esperado \"{b.Branch}\", mas está em " + (atual.Length == 0 ? "detached HEAD" : $"\"{atual}\""));
        }

        res.Info($"Conferência: {certos} de {m.Branches.Count} projeto(s) na branch da melhoria.");
    }

    async Task AplicarBancoAsync(Melhoria m, ResumoDeMelhoria res, CancellationToken cancelar)
    {
        if (m.Perfis.Count == 0) { res.Info("Banco: nenhum perfil associado a esta melhoria - contexto do banco mantido."); return; }
        var id = m.PerfilParaAplicar(i => banco.Achar(i) is not null);
        if (id is null) { res.Erro("Banco: o perfil associado à melhoria não existe mais (foi excluído?)."); return; }
        var nome = banco.Achar(id)!.Nome;
        var r = await banco.AplicarAsync(id, cancelar);
        if (r.EhOk) res.Ok($"Banco: perfil \"{nome}\" aplicado");
        else res.Erro($"Banco: não foi possível aplicar \"{nome}\": {r.Erro}");
    }

    async Task GravarEstadoAsync(
        Melhoria m, ResumoDeMelhoria res, ConfigMelhorias config, Dictionary<string, string> antes, PerfilDeBancoRef? bancoAntes, CancellationToken cancelar)
    {
        if (config.Achar(m.Id) is { } guardada) guardada.UltimoUso = Agora();
        config.Anterior ??= new EstadoAnterior { Quando = Agora(), TinhaBanco = bancoAntes is not null, BancoId = bancoAntes?.Id ?? "", BancoNome = bancoAntes?.Nome ?? "" };
        config.Anterior.Melhoria = m.Id;

        var principal = VersaoConfDaMelhoria.NomeDoPrincipal(m.Branches.Select(b => b.Projeto));
        if (config.VersaoConf.Ativo)
        {
            if (principal is not null && Achar(principal) is { } pp) VersaoConfDaMelhoria.Gravar(config.VersaoConf, principal, pp.Caminho, config.Anterior, res);
            else res.Info("version.conf: a melhoria não tem projeto principal (lavenderepda, lavenderedados, lavendereweb ou lavenderecore) - mantido.");
        }

        if (config.Sqlite.Ativo)
        {
            var donoAntes = config.Sqlite.Dono.Length > 0 ? config.Sqlite.Dono
                : principal is not null && antes.TryGetValue(principal, out var br) ? SqliteDeAtividade.IdDaBranch(br) : "";
            if (!config.Anterior.GuardouSqlite) { config.Anterior.GuardouSqlite = true; config.Anterior.SqliteDonoAntes = donoAntes; }
            await sqlite.TrocarAsync(config.Sqlite, SqliteDeAtividade.IdDaAtividade(m.Id), donoAntes, res);
        }

        foreach (var p in projetos.Todos)
        {
            var anterior = antes[p.Nome];
            if (anterior.Length == 0 || anterior == await consultas.BranchAtualAsync(p.Caminho, cancelar)
                || config.Anterior.Branches.Any(x => x.Projeto.Equals(p.Nome, StringComparison.OrdinalIgnoreCase))) continue;
            config.Anterior.Branches.Add(new BranchDaMelhoria { Projeto = p.Nome, Branch = anterior });
        }

        config.Ativa = m.Id;
        repositorio.Salvar();
    }

    /// <summary>O que o Desvincular vai fazer (linhas para a confirmação).</summary>
    public async Task<IReadOnlyList<string>> DesvincularPreviaAsync(CancellationToken cancelar = default)
    {
        var l = new List<string>();
        var config = repositorio.Config;
        if (config.Anterior is not { } ant)
        {
            l.Add("Não há estado anterior guardado: só a vinculação será removida (branches e banco ficam como estão).");
            return l;
        }

        foreach (var b in ant.Branches)
        {
            if (Achar(b.Projeto) is not { } p) { l.Add($"{b.Projeto}: projeto não encontrado na configuração (ignorado)"); continue; }
            var atual = await consultas.BranchAtualAsync(p.Caminho, cancelar);
            l.Add($"{p.Nome}: {(atual.Length == 0 ? "detached HEAD" : atual)}  ->  {b.Branch}" + (atual == b.Branch ? "  (já está)" : ""));
        }

        if (ant.Branches.Count == 0) l.Add("Branches: nenhum projeto mudou de branch na ativação.");
        if (ant.GuardouVersaoConf) l.Add($"version.conf: voltar para \"{ant.VersaoConfAntes.Trim()}\"");
        if (ant.GuardouSqlite && config.Sqlite.Ativo)
            l.Add(ant.SqliteDonoAntes.Length > 0 ? $"Banco sqlite: voltar ao wmwvendas-{ant.SqliteDonoAntes}.sqlite3 (o atual é guardado com o id da atividade)" : "Banco sqlite: o dono de antes era desconhecido - wmwvendas.sqlite3 fica como está");
        l.Add(ant.TinhaBanco && ant.BancoNome.Length > 0 ? $"Banco: voltar ao perfil \"{ant.BancoNome}\"" : "Banco: antes não havia conexão reconhecida no context.xml; ele fica como está.");
        return l;
    }

    /// <summary>Volta cada projeto à branch de antes e o banco ao de antes. Só limpa a vinculação se tudo voltou; com erro, o estado fica para tentar de novo.</summary>
    public async Task<ResumoDeMelhoria> DesvincularAsync(bool restaurarBanco, CancellationToken cancelar = default)
    {
        var config = repositorio.Config;
        var res = new ResumoDeMelhoria("Desvincular a melhoria " + (config.Ativa.Length > 0 ? config.Ativa : "(nenhuma ativa)"));
        if (config.Anterior is not { } ant) res.Info("Não havia estado anterior guardado: só a vinculação foi removida (branches e banco ficam como estão).");
        else await RestaurarEstadoAsync(ant, config, restaurarBanco, res, cancelar);

        if (!res.Parado && res.Erros == 0)
        {
            config.Ativa = "";
            config.Anterior = null;
            res.Info("Melhoria desvinculada.");
        }
        else if (!res.Parado) res.Aviso("A melhoria continua vinculada porque houve erro; corrija e tente Desvincular de novo (o estado anterior foi mantido).");

        repositorio.Salvar();
        return res;
    }

    async Task RestaurarEstadoAsync(EstadoAnterior ant, ConfigMelhorias config, bool restaurarBanco, ResumoDeMelhoria res, CancellationToken cancelar)
    {
        foreach (var b in ant.Branches)
        {
            if (cancelar.IsCancellationRequested) { res.Parado = true; res.Aviso("Parado pelo usuário."); return; }
            if (Achar(b.Projeto) is not { } p) { res.Aviso($"{b.Projeto}: projeto não encontrado na configuração - ignorado"); continue; }
            if (await consultas.BranchAtualAsync(p.Caminho, cancelar) == b.Branch) { res.Ok($"{p.Nome}: já estava em {b.Branch}"); continue; }
            await TrocarAsync(p, b.Branch, "", res, cancelar);
        }

        var certos = 0;
        foreach (var b in ant.Branches)
            if (Achar(b.Projeto) is { } p && await consultas.BranchAtualAsync(p.Caminho, cancelar) == b.Branch) certos++;
        res.Info($"Conferência: {certos} de {ant.Branches.Count} projeto(s) de volta na branch de antes.");

        if (ant.GuardouVersaoConf) RestaurarVersaoConf(ant, res);
        if (ant.GuardouSqlite) await RestaurarSqliteAsync(ant, config, res);
        if (restaurarBanco) await RestaurarBancoAsync(ant, res, cancelar);
    }

    static void RestaurarVersaoConf(EstadoAnterior ant, ResumoDeMelhoria res)
    {
        try
        {
            File.WriteAllText(ant.VersaoConfCaminho, ant.VersaoConfAntes, new System.Text.UTF8Encoding(false));
            res.Ok($"version.conf: voltou para \"{ant.VersaoConfAntes.Trim()}\"");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { res.Erro("version.conf: não foi possível restaurar: " + ex.Message); }
    }

    async Task RestaurarSqliteAsync(EstadoAnterior ant, ConfigMelhorias config, ResumoDeMelhoria res)
    {
        if (!config.Sqlite.Ativo) res.Info("sqlite: a opção está desligada - wmwvendas.sqlite3 não foi tocado.");
        else if (ant.SqliteDonoAntes.Length == 0) res.Info("sqlite: o dono do banco de antes era desconhecido - wmwvendas.sqlite3 mantido como está.");
        else if (!await sqlite.TrocarAsync(config.Sqlite, ant.SqliteDonoAntes, "", res)) res.Erro($"sqlite: não foi possível voltar ao banco de {ant.SqliteDonoAntes} (veja acima).");
    }

    async Task RestaurarBancoAsync(EstadoAnterior ant, ResumoDeMelhoria res, CancellationToken cancelar)
    {
        if (!ant.TinhaBanco || ant.BancoId.Length == 0) { res.Info("Banco: antes da melhoria não havia conexão reconhecida no context.xml - mantido como está."); return; }
        if (banco.AtivoNoContexto()?.Id == ant.BancoId) { res.Ok($"Banco: o context.xml já está no perfil \"{ant.BancoNome}\""); return; }
        var r = await banco.AplicarAsync(ant.BancoId, cancelar);
        if (r.EhOk) res.Ok($"Banco: voltou ao perfil \"{ant.BancoNome}\"");
        else res.Erro($"Banco: não foi possível restaurar \"{ant.BancoNome}\": {r.Erro}");
    }
}
