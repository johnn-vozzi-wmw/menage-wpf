namespace Menage.Core.Operacoes.Diagnostico;

/// <param name="Filtro">Trecho do caminho (sem diferenciar maiúsculas); vazio = todos.</param>
/// <param name="Situacoes">Letras do status (M, A, D, R, ?); vazio = todas.</param>
public sealed record OpcoesDeArquivosAlterados(string Filtro = "", string Situacoes = "");

/// <summary>Arquivos alterados de cada projeto (<c>status --porcelain -uall</c>), com filtro por situação e texto. Só leitura.</summary>
public sealed class ArquivosAlteradosEmLote(IProjetos projetos, IServicoDeDialogos dialogos, IProgresso progresso, IGit git)
    : OperacaoEmLote<OpcoesDeArquivosAlterados, ResultadoDeConsulta>(projetos, dialogos, progresso)
{
    public override string Id => "mais.arquivos-alterados";

    public override string Nome => "Arquivos alterados";

    protected override async Task<ResultadoDoProjeto<ResultadoDeConsulta>> ExecutarEmProjeto(Projeto projeto, OpcoesDeArquivosAlterados opcoes, CancellationToken cancelar)
    {
        var status = await git.ExecutarAsync(Git.Em(projeto.Caminho).Comando("status").Opcao("--porcelain").Opcao("-uall"), cancelar);
        if (!status.EhOk) return ResultadoDoProjeto<ResultadoDeConsulta>.Falha(status.Erro);

        var filtro = opcoes.Filtro.Trim();
        var linhas = status.Valor.Linhas()
            .Where(l => l.Length > 3 && (opcoes.Situacoes.Length == 0 || l[..2].Trim().IndexOfAny(opcoes.Situacoes.ToCharArray()) >= 0))
            .Where(l => filtro.Length == 0 || l[3..].Contains(filtro, StringComparison.OrdinalIgnoreCase))
            .Select(l => $"{l[..2].Trim(),-2} {l[3..]}")
            .ToList();
        return linhas.Count == 0
            ? ResultadoDoProjeto<ResultadoDeConsulta>.Ok(new([]), "sem alterações")
            : ResultadoDoProjeto<ResultadoDeConsulta>.Aviso($"{linhas.Count} arquivo(s) alterado(s)", new(linhas));
    }
}
