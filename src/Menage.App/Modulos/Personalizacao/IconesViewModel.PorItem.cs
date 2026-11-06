using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Menage.App.Modulos.JanelaPrincipal;
using Menage.Core.Personalizacao;

namespace Menage.App.Modulos.Personalizacao;

/// <summary>Uma linha da lista "por item": o alvo (comando, grupo ou projeto), a chave que o identifica e o arquivo escolhido.</summary>
public sealed partial class LinhaDeIcone(AlvoDeIcone alvo, string chave, string nome, string? caminho) : ObservableObject
{
    public AlvoDeIcone Alvo { get; } = alvo;

    public string Chave { get; } = chave;

    public string Nome { get; } = nome;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Resumo), nameof(TemIcone))]
    string? _caminho = caminho;

    public bool TemIcone => Caminho is not null;

    public string Resumo => Caminho is null ? "padrão" : Path.GetFileName(Caminho);
}

// Ícone por comando (menu e pílulas), por grupo e por projeto: só valem com as caixas "Onde aparece" marcadas.
public sealed partial class IconesViewModel
{
    public IReadOnlyList<LinhaDeIcone> Comandos { get; private set; } = [];

    public ObservableCollection<LinhaDeIcone> Grupos { get; private set; } = [];

    public IReadOnlyList<LinhaDeIcone> Projetos { get; private set; } = [];

    void CarregarPorItem(Menage.Core.Comandos.ICatalogoDeComandos comandos, Menage.Core.Projetos.IConfiguracaoDeProjetos configuracao)
    {
        var icones = _preferencias.Valor.Icones;
        var menuSoDePilula = BarraDeAcoesViewModel.Definicoes.Where(d => d.IdPrincipal is null).Select(d => ("pilula:" + d.Texto, $"Pílula {d.Texto}"));
        Comandos = [.. comandos.Todos.Select(c => (c.Id, c.Texto)).Concat(menuSoDePilula)
            .Select(c => new LinhaDeIcone(AlvoDeIcone.Comando, c.Item1, c.Item2, Ou(icones.Comandos, c.Item1)))];

        var catalogo = configuracao.Catalogo;
        Grupos = [.. catalogo.Grupos.Select(g => new LinhaDeIcone(AlvoDeIcone.Grupo, g.Id, catalogo.NomeDoGrupo(g.Id), Ou(icones.Grupos, g.Id)))];
        Projetos = [.. configuracao.Projetos.Select(p => new LinhaDeIcone(AlvoDeIcone.Projeto, p.Caminho, p.Nome, Ou(icones.Projetos, p.Caminho)))];
    }

    [RelayCommand]
    void Escolher(LinhaDeIcone linha)
    {
        if (Selecionar() is { } caminho) DefinirItem(linha, caminho);
    }

    [RelayCommand]
    void RemoverItem(LinhaDeIcone linha) => DefinirItem(linha, null);

    void DefinirItem(LinhaDeIcone linha, string? caminho)
    {
        _preferencias.Alterar(p => p with { Icones = p.Icones.ComIconeDe(linha.Alvo, linha.Chave, caminho) });
        linha.Caminho = caminho;
    }

    static string? Ou(Dictionary<string, string> mapa, string chave) => mapa.TryGetValue(chave, out var c) && c.Length > 0 ? c : null;
}
