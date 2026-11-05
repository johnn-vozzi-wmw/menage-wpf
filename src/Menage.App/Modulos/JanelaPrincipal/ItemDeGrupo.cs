using CommunityToolkit.Mvvm.ComponentModel;
using Menage.App.Controles;
using Menage.Core.Projetos;

namespace Menage.App.Modulos.JanelaPrincipal;

/// <summary>Um grupo da barra lateral: bolinha na cor do grupo, "marcados/total" e a barra de progresso da seleção.</summary>
public sealed partial class ItemDeGrupo(string id, string nome, TomDeCor tom) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Texto), nameof(Fracao), nameof(Completo), nameof(Parcial))]
    int _marcados;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Texto), nameof(Fracao), nameof(Completo), nameof(Parcial))]
    int _total;

    public string Id { get; } = id;

    public string Nome { get; } = nome;

    public TomDeCor Tom { get; } = tom;

    public bool EhTodos => Id == GruposEspeciais.Todos;

    public string Texto => $"{Marcados}/{Total}";

    public double Fracao => Total == 0 ? 0 : (double)Marcados / Total;

    public bool Completo => Total > 0 && Marcados == Total;

    public bool Parcial => Marcados > 0 && Marcados < Total;

    public string Dica => $"{Nome}: clique marca só este grupo; Shift+clique soma ou tira o grupo da seleção";
}
