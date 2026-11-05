using System.Globalization;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Menage.App.Controles;
using Menage.Core.LinhaDoTempo;
using Menage.Core.Projetos;

namespace Menage.App.Modulos.HistoricoReleases;

/// <summary>Uma linha da linha do tempo, pronta para a tela.</summary>
public sealed record LinhaDeRelease(EventoDeRelease Evento)
{
    public string Dia => Evento.Data.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    public string Hora => Evento.Data.ToString("HH:mm", CultureInfo.InvariantCulture);

    public string Titulo => Evento.Titulo;

    public string Detalhe => Evento.Detalhe;

    public string Ambiente => Evento.Ambiente;

    public bool TemAmbiente => Evento.Ambiente.Length > 0;

    public TomDeCor Tom => Evento.EhTag ? TomDeCor.Azul : Evento.Estado switch
    {
        EstadoDoEvento.Ok => TomDeCor.Verde,
        EstadoDoEvento.Erro => TomDeCor.Vermelho,
        _ => TomDeCor.Neutro,
    };
}

/// <summary>Estado da janela: carrega betas e tags uma vez e filtra na memória (ambiente, texto, tipo).</summary>
public sealed partial class JanelaDeReleasesViewModel(HistoricoDeReleases historico, IProjetos projetos) : ObservableObject
{
    public const string TodosOsAmbientes = "Todos os ambientes";

    IReadOnlyList<EventoDeRelease> _todos = [];

    [ObservableProperty]
    string _ambiente = TodosOsAmbientes;

    [ObservableProperty]
    string _texto = "";

    [ObservableProperty]
    bool _betas = true;

    [ObservableProperty]
    bool _tags = true;

    [ObservableProperty]
    bool _carregando;

    [ObservableProperty]
    string _contagem = "";

    [ObservableProperty]
    string _vazio = "";

    [ObservableProperty]
    bool _semLinhas;

    public ObservableCollection<string> Ambientes { get; } = [TodosOsAmbientes];

    public ObservableCollection<LinhaDeRelease> Linhas { get; } = [];

    public async Task CarregarAsync()
    {
        Carregando = true;
        try
        {
            _todos = await historico.CarregarAsync(projetos.Marcados);
        }
        finally
        {
            Carregando = false;
        }

        Mostrar(_todos);
    }

    /// <summary>Mostra estes eventos (usado depois de carregar e pela galeria, que não lê repositórios).</summary>
    public void Mostrar(IReadOnlyList<EventoDeRelease> eventos)
    {
        _todos = eventos;
        Ambientes.Clear();
        Ambientes.Add(TodosOsAmbientes);
        foreach (var a in HistoricoDeReleases.Ambientes(_todos)) Ambientes.Add(a);
        Ambiente = TodosOsAmbientes;
        Filtrar();
    }

    partial void OnAmbienteChanged(string value) => Filtrar();

    partial void OnTextoChanged(string value) => Filtrar();

    partial void OnBetasChanged(bool value) => Filtrar();

    partial void OnTagsChanged(bool value) => Filtrar();

    void Filtrar()
    {
        var ambiente = string.IsNullOrEmpty(Ambiente) || Ambiente == TodosOsAmbientes ? "" : Ambiente;
        var visiveis = new FiltroDeReleases(ambiente, Texto, Betas, Tags).Aplicar(_todos);
        Linhas.Clear();
        foreach (var e in visiveis) Linhas.Add(new LinhaDeRelease(e));
        Contagem = $"{visiveis.Count} de {_todos.Count} evento(s)";
        SemLinhas = visiveis.Count == 0;
        Vazio = _todos.Count == 0
            ? "Ainda não há betas geradas pelo Gerador de Beta nem tags nos projetos marcados."
            : "Nenhum evento combina com o filtro.";
    }
}
