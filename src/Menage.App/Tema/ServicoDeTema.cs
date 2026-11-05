using CommunityToolkit.Mvvm.Messaging;
using Menage.Core.Armazenamento;

namespace Menage.App.Tema;

/// <summary>O id do tema escolhido, em <c>tema.json</c>.</summary>
public sealed record PreferenciasDeTema(string TemaId);

/// <summary>Avisa quem desenha com cor própria (gráficos, diff) que a paleta mudou.</summary>
public sealed record TemaMudou(DefinicaoDeTema Tema);

public interface IServicoDeTema
{
    /// <summary>O tema em uso, já com os ajustes do usuário (cor de destaque).</summary>
    DefinicaoDeTema Atual { get; }

    /// <summary>Aplica o tema salvo. Chamado uma vez, antes da primeira janela.</summary>
    void Iniciar();

    /// <summary>Troca o tema em todas as janelas abertas e salva a escolha.</summary>
    void Trocar(string id, bool animar = true);

    /// <summary>Ctrl+T: vai para o par claro/escuro do tema atual.</summary>
    void AlternarPar();

    /// <summary>Aplica de novo o tema em uso (depois de mudar um ajuste, como a cor de destaque). Não grava nada.</summary>
    void Reaplicar(bool animar = true);

    /// <summary>O tema do programa escolhido, sem ajustes (o que está em <c>tema.json</c>).</summary>
    DefinicaoDeTema Escolhido { get; }
}

/// <summary>O que toca o WPF. Separado para a regra de troca e de gravação ser testada sem janela.</summary>
public interface IAplicadorDeTema
{
    void Aplicar(DefinicaoDeTema tema, bool animar);
}

/// <summary>Ajuste por cima do tema escolhido (ex.: cor de destaque do usuário). Sem ajuste, devolve o próprio tema.</summary>
public interface IAjusteDeTema
{
    DefinicaoDeTema Ajustar(DefinicaoDeTema tema);
}

public sealed class ServicoDeTema(
    ICatalogoDeTemas catalogo,
    IArmazenamento<PreferenciasDeTema> armazenamento,
    IAplicadorDeTema aplicador,
    IMessenger mensageiro,
    IAjusteDeTema? ajuste = null) : IServicoDeTema
{
    DefinicaoDeTema? _escolhido;
    DefinicaoDeTema? _atual;

    public DefinicaoDeTema Atual => _atual ?? catalogo.BuscarOuPadrao(null);

    public DefinicaoDeTema Escolhido => _escolhido ?? catalogo.BuscarOuPadrao(null);

    public void Iniciar()
    {
        var salvo = armazenamento.Carregar().Mapear(p => p.TemaId).ValorOu(CatalogoDeTemas.IdEscuro);
        Mudar(catalogo.BuscarOuPadrao(salvo), animar: false);
    }

    public void Trocar(string id, bool animar = true)
    {
        if (catalogo.Buscar(id) is not { } tema || tema == _escolhido) return;
        Mudar(tema, animar);
        // Falha ao gravar não impede a troca: o tema só não volta na próxima abertura.
        armazenamento.Salvar(new PreferenciasDeTema(tema.Id));
    }

    public void AlternarPar() => Trocar(catalogo.ParDe(Escolhido).Id);

    public void Reaplicar(bool animar = true) => Mudar(Escolhido, animar);

    void Mudar(DefinicaoDeTema tema, bool animar)
    {
        _escolhido = tema;
        _atual = ajuste?.Ajustar(tema) ?? tema;
        aplicador.Aplicar(_atual, animar);
        mensageiro.Send(new TemaMudou(_atual));
    }
}
