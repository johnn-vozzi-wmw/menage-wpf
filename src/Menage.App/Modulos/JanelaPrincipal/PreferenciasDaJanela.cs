using Menage.Core.Armazenamento;

namespace Menage.App.Modulos.JanelaPrincipal;

public sealed record PreferenciasDaJanela(
    bool BarraRecolhida = false, bool SaidaRecolhida = false, double AlturaDaSaida = 190, bool FetchAutomatico = false)
{
    public const string Nome = "janela-principal";
}

/// <summary>Estado da janela que sobrevive ao fechamento (barra lateral, Saída, fetch automático), por <see cref="Armazenamento{T}"/>.</summary>
public sealed class PreferenciasDaJanelaServico(IArmazenamento<PreferenciasDaJanela> armazenamento)
{
    PreferenciasDaJanela? _valor;

    public PreferenciasDaJanela Valor => _valor ??= armazenamento.Carregar().ValorOu(new PreferenciasDaJanela());

    public void Alterar(Func<PreferenciasDaJanela, PreferenciasDaJanela> mudanca)
    {
        _valor = mudanca(Valor);
        armazenamento.Salvar(_valor);
    }
}
