using Menage.Core.Estado;
using Menage.Core.Operacoes.Decoradores;

namespace Menage.Core.Operacoes.Push;

/// <param name="Forcar"><c>--force-with-lease</c>: pede confirmação antes e nunca atropela commits novos do origin sem o lease.</param>
/// <param name="PublicarNova">Publica branches que ainda não existem no origin.</param>
public sealed record OpcoesDePush(bool Forcar = false, bool PublicarNova = true);

public sealed record ResultadoDePush(int Enviados, bool Publicada);

/// <summary>Situação de um projeto diante do push; <see cref="Aviso"/> explica por que foi ignorado ou o que acontecerá.</summary>
public sealed record PreviaDePush(Projeto Projeto, string Branch, bool TemUpstream, int Ahead, int Behind, bool Ignorado, string Aviso);

/// <summary>
/// Push em lote: consulta o origin (fetch) para saber o que há a enviar, publica branch nova, não envia se o origin tem
/// commits que o projeto não tem e respeita a proteção de branches projeto a projeto.
/// </summary>
public sealed class PushEmLote(
    IProjetos projetos, IServicoDeDialogos dialogos, IProgresso progresso, IGit git, LeitorDeEstado estados, IRegrasDeProtecao regras, EnvioParaOrigin envio)
    : OperacaoEmLote<OpcoesDePush, ResultadoDePush>(projetos, dialogos, progresso)
{
    public override string Id => "git.push";

    public override string Nome => "Push";

    public async Task<IReadOnlyList<PreviaDePush>> ListarAsync(IEnumerable<Projeto> alvo, CancellationToken cancelar = default)
    {
        var lista = new List<PreviaDePush>();
        foreach (var projeto in alvo) lista.Add(await AvaliarAsync(projeto, cancelar));
        return lista;
    }

    protected override Task<Previa?> MontarPreviaAsync(OpcoesDePush opcoes, IReadOnlyList<Projeto> alvo, CancellationToken cancelar) =>
        Task.FromResult<Previa?>(opcoes.Forcar
            ? new Previa("Push forçado", "Enviar com --force-with-lease? O histórico do origin pode ser sobrescrito se ninguém mais tiver publicado nele.", [.. alvo.Select(p => p.Nome)])
            : null);

    protected override async Task<ResultadoDoProjeto<ResultadoDePush>> ExecutarEmProjeto(Projeto projeto, OpcoesDePush opcoes, CancellationToken cancelar)
    {
        var previa = await AvaliarAsync(projeto, cancelar);
        if (previa.Ignorado) return ResultadoDoProjeto<ResultadoDePush>.Aviso($"ignorado - {previa.Aviso}");
        if (previa.TemUpstream && previa.Ahead == 0 && !opcoes.Forcar) return ResultadoDoProjeto<ResultadoDePush>.Aviso($"{previa.Branch} - nada para enviar");
        if (previa.TemUpstream && previa.Behind > 0 && !opcoes.Forcar)
            return ResultadoDoProjeto<ResultadoDePush>.Falha($"{previa.Branch} - o origin tem {previa.Behind} commit(s) que você não tem", null, "Faça pull ou rebase antes (nada foi enviado).");

        var enviado = await envio.EnviarAsync(projeto.Caminho, previa.Branch, opcoes.Forcar, opcoes.PublicarNova, cancelar);
        if (!enviado.EhOk) return ResultadoDoProjeto<ResultadoDePush>.Falha($"{previa.Branch} - {enviado.Erro.Causa}");

        var nova = !previa.TemUpstream;
        return ResultadoDoProjeto<ResultadoDePush>.Ok(new(previa.Ahead, nova), nova ? $"{previa.Branch} - {enviado.Valor}" : $"{previa.Branch} - {previa.Ahead} commit(s) enviado(s)");
    }

    async Task<PreviaDePush> AvaliarAsync(Projeto projeto, CancellationToken cancelar)
    {
        if (!ConsultasGit.EhRepositorio(projeto.Caminho)) return new(projeto, "", false, 0, 0, true, "pasta não encontrada");
        var temOrigin = (await git.ExecutarBrutoAsync(Git.Em(projeto.Caminho).Comando("remote").Argumento("get-url").Argumento("origin"), cancelar)).Sucesso;
        if (temOrigin)
            await git.ExecutarBrutoAsync(Git.Em(projeto.Caminho).Comando("fetch").Opcao("--quiet").Argumento("origin").ComTimeout(ConsultasGit.TempoLongo), cancelar);

        var e = await estados.LerAsync(projeto, cancelar);
        var previa = new PreviaDePush(projeto, e.Branch, e.TemUpstream, e.Ahead, e.Behind, false, "");
        if (e.Operacao.Length > 0) return previa with { Ignorado = true, Aviso = $"{e.Operacao} - conclua ou aborte antes" };
        if (e.Detached) return previa with { Ignorado = true, Aviso = "detached HEAD - faça checkout de uma branch" };
        if (regras.ProtegidaParaPush(e.Branch)) return previa with { Ignorado = true, Aviso = "BLOQUEADO: branch protegida (Configurações > Proteção de branches)" };
        if (!temOrigin) return previa with { Ignorado = true, Aviso = "o projeto não tem remote origin" };
        if (!e.TemUpstream) return await ContarNovosAsync(previa, cancelar);
        if (e.Behind > 0) return previa with { Aviso = $"o origin tem {e.Behind} commit(s) que você não tem" };
        return e.Ahead == 0 ? previa with { Aviso = "nada para enviar" } : previa;
    }

    async Task<PreviaDePush> ContarNovosAsync(PreviaDePush previa, CancellationToken cancelar)
    {
        var contagem = await git.ExecutarBrutoAsync(Git.Em(previa.Projeto.Caminho).Comando("rev-list").Opcao("--count").Argumento("HEAD").Opcao("--not").Opcao("--remotes=origin"), cancelar);
        return previa with { Ahead = int.TryParse(contagem.PrimeiraLinha().Trim(), out var n) ? n : 0, Aviso = "branch nova no origin (será publicada)" };
    }
}
