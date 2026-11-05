using Menage.Core.Armazenamento;

namespace Menage.Core.Personalizacao;

public interface IPreferenciasDeAparencia
{
    PreferenciasDeAparencia Valor { get; }

    /// <summary>Muda, grava e avisa. Falha ao gravar não desfaz a mudança: ela vale até fechar o programa.</summary>
    Resultado<Nada> Alterar(Func<PreferenciasDeAparencia, PreferenciasDeAparencia> mudanca);

    event Action<PreferenciasDeAparencia>? Alterado;
}

/// <summary>Guarda as preferências de aparência em <c>aparencia.json</c> por <see cref="Armazenamento{T}"/>.</summary>
public sealed class ServicoDePreferencias(IArmazenamento<PreferenciasDeAparencia> armazenamento) : IPreferenciasDeAparencia
{
    PreferenciasDeAparencia? _valor;

    public PreferenciasDeAparencia Valor => _valor ??= armazenamento.Carregar().ValorOu(new PreferenciasDeAparencia());

    public event Action<PreferenciasDeAparencia>? Alterado;

    public Resultado<Nada> Alterar(Func<PreferenciasDeAparencia, PreferenciasDeAparencia> mudanca)
    {
        var novo = mudanca(Valor);
        if (novo == Valor && novo.Atalhos.SequenceEqual(Valor.Atalhos)) return Resultado.Ok();
        _valor = novo;
        var gravado = armazenamento.Salvar(novo);
        Alterado?.Invoke(novo);
        return gravado;
    }
}
