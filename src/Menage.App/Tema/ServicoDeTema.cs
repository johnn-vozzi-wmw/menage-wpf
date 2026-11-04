using CommunityToolkit.Mvvm.Messaging;
using Menage.Core.Armazenamento;

namespace Menage.App.Tema;

/// <summary>O id do tema escolhido, em <c>tema.json</c>.</summary>
public sealed record PreferenciasDeTema(string TemaId);

/// <summary>Avisa quem desenha com cor própria (gráficos, diff) que a paleta mudou.</summary>
public sealed record TemaMudou(DefinicaoDeTema Tema);

public interface IServicoDeTema
{
    DefinicaoDeTema Atual { get; }

    /// <summary>Aplica o tema salvo. Chamado uma vez, antes da primeira janela.</summary>
    void Iniciar();

    /// <summary>Troca o tema em todas as janelas abertas e salva a escolha.</summary>
    void Trocar(string id, bool animar = true);

    /// <summary>Ctrl+T: vai para o par claro/escuro do tema atual.</summary>
    void AlternarPar();
}

/// <summary>O que toca o WPF. Separado para a regra de troca e de gravação ser testada sem janela.</summary>
public interface IAplicadorDeTema
{
    void Aplicar(DefinicaoDeTema tema, bool animar);
}

public sealed class ServicoDeTema(
    ICatalogoDeTemas catalogo,
    IArmazenamento<PreferenciasDeTema> armazenamento,
    IAplicadorDeTema aplicador,
    IMessenger mensageiro) : IServicoDeTema
{
    DefinicaoDeTema? _atual;

    public DefinicaoDeTema Atual => _atual ?? catalogo.BuscarOuPadrao(null);

    public void Iniciar()
    {
        var salvo = armazenamento.Carregar().Mapear(p => p.TemaId).ValorOu(CatalogoDeTemas.IdEscuro);
        Mudar(catalogo.BuscarOuPadrao(salvo), animar: false);
    }

    public void Trocar(string id, bool animar = true)
    {
        if (catalogo.Buscar(id) is not { } tema || tema == _atual) return;
        Mudar(tema, animar);
        // Falha ao gravar não impede a troca: o tema só não volta na próxima abertura.
        armazenamento.Salvar(new PreferenciasDeTema(tema.Id));
    }

    public void AlternarPar() => Trocar(catalogo.ParDe(Atual).Id);

    void Mudar(DefinicaoDeTema tema, bool animar)
    {
        _atual = tema;
        aplicador.Aplicar(tema, animar);
        mensageiro.Send(new TemaMudou(tema));
    }
}
