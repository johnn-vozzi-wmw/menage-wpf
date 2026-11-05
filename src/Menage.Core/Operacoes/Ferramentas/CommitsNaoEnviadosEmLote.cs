using Menage.Core.Operacoes.Diagnostico;

namespace Menage.Core.Operacoes.Ferramentas;

public sealed record OpcoesDeNaoEnviados(bool Fetch = false);

/// <summary>Por projeto, as branches locais à frente do origin e as nunca publicadas (commits que só existem neste computador). Só leitura.</summary>
public sealed class CommitsNaoEnviadosEmLote(IProjetos projetos, IServicoDeDialogos dialogos, IProgresso progresso, IGit git)
    : OperacaoEmLote<OpcoesDeNaoEnviados, ResultadoDeConsulta>(projetos, dialogos, progresso)
{
    public override string Id => "ferramentas.nao-enviados";

    public override string Nome => "Commits não enviados";

    protected override async Task<ResultadoDoProjeto<ResultadoDeConsulta>> ExecutarEmProjeto(Projeto projeto, OpcoesDeNaoEnviados opcoes, CancellationToken cancelar)
    {
        var repo = projeto.Caminho;
        if (opcoes.Fetch) await git.ExecutarBrutoAsync(Git.Em(repo).Comando("fetch").Opcao("--quiet").Opcao("--all").ComTimeout(ConsultasGit.TempoLongo), cancelar);

        var lista = await git.ExecutarBrutoAsync(Git.Em(repo).Comando("for-each-ref").Opcao("--format=%(refname:short)%09%(upstream:short)").Argumento("refs/heads"), cancelar);
        var linhas = new List<string>();
        foreach (var partes in lista.Linhas().Select(l => l.Split('\t')))
        {
            var branch = partes[0];
            var temUpstream = partes.Length > 1 && partes[1].Length > 0;
            var faixa = temUpstream ? $"{partes[1]}..{branch}" : null;
            var contagem = temUpstream
                ? await git.ExecutarBrutoAsync(Git.Em(repo).Comando("rev-list").Opcao("--count").Argumento(faixa!), cancelar)
                : await git.ExecutarBrutoAsync(Git.Em(repo).Comando("rev-list").Opcao("--count").Argumento(branch).Opcao("--not").Opcao("--remotes"), cancelar);
            if (int.TryParse(contagem.PrimeiraLinha().Trim(), out var n) && n > 0)
                linhas.Add(temUpstream ? $"{branch}: {n} commit(s) à frente de {partes[1]}" : $"{branch}: {n} commit(s) só aqui (branch nunca publicada)");
        }

        return linhas.Count == 0
            ? ResultadoDoProjeto<ResultadoDeConsulta>.Ok(new([]), "tudo enviado")
            : ResultadoDoProjeto<ResultadoDeConsulta>.Aviso($"{linhas.Count} branch(es) com commits não enviados", new(linhas));
    }
}
