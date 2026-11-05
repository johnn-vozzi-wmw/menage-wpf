namespace Menage.Core.Entregas;

/// <summary>O que só o Windows sabe fazer; o App implementa. Nada aqui envia arquivo a serviço externo.</summary>
public interface IAcoesDeEntrega
{
    /// <summary>Copia o arquivo como "arquivo copiado" (Ctrl+V no Slack ou no Explorer cola o arquivo). Falso se não deu.</summary>
    bool CopiarParaAreaDeTransferencia(string arquivo);

    /// <summary>Abre o Explorer com o item selecionado (arquivo) ou a pasta aberta.</summary>
    void MostrarNoExplorer(string caminho);
}

/// <summary>Prepara a entrega e faz o que vem depois: área de transferência (se ligada) e Explorer.</summary>
public sealed class ServicoDeEntrega(IAcoesDeEntrega acoes)
{
    public ResultadoDaEntrega Entregar(ConfiguracaoDeEntregas config, PacoteDeEntrega pacote, Func<string, string?> pastaDe)
    {
        var resultado = PreparadorDeEntrega.Preparar(config, pacote, pastaDe);
        if (resultado.Zip is { } zip && config.CopiarZip && !acoes.CopiarParaAreaDeTransferencia(zip))
            resultado = resultado with { Problemas = [.. resultado.Problemas, "não foi possível copiar o .zip para a área de transferência"] };
        if (resultado.Copiados.Count > 0) acoes.MostrarNoExplorer(resultado.Principal);
        return resultado;
    }
}
