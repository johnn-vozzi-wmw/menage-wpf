using Menage.Core.Beta;

namespace Menage.App.Modulos.GeradorBeta;

/// <summary>Cartão da faixa de favoritos; <c>Ativo</c> quando a seleção e o modo atuais são os do favorito.</summary>
public sealed record CartaoDeFavorito(FavoritoDoGerador Favorito, bool Ativo)
{
    public string Nome => Favorito.Nome;

    public string Resumo => Favorito.Resumo;
}

public sealed record OpcaoDeBase(string Caminho, string Texto);

/// <summary>O que o diálogo de uma entrega mostra: o pacote, o que será copiado e para onde.</summary>
public sealed record PedidoDeEntrega(string Pacote, string Subtitulo, string Pasta, bool Zip, bool CopiarZip, IReadOnlyList<string> Previa);

public sealed record PedidoDeTexto(string Titulo, string Rotulo, string Valor);

public sealed record PedidoDeLimparTarget(Menage.Core.Beta.LimparTargetConfig Atual, IReadOnlyList<ProjetoDoGerador> Projetos);

public sealed record PedidoDeEntregas(Menage.Core.Entregas.ConfiguracaoDeEntregas Config);

public sealed record RespostaDeEntregas(Menage.Core.Entregas.ConfiguracaoDeEntregas Config, string? PrepararAgora);

public sealed record EntregaPronta(string Pacote, Menage.Core.Entregas.ResultadoDaEntrega Resultado)
{
    public string Titulo => $"Entrega pronta: {Pacote}";

    public string Detalhe => string.Join("  •  ", Resultado.Copiados) + (Resultado.Problemas.Count > 0 ? "\nProblemas: " + string.Join("; ", Resultado.Problemas) : "");
}
