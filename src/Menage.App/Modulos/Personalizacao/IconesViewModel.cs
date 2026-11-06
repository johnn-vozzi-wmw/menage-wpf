using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Menage.App.Icones;
using Menage.App.Modulos.JanelaPrincipal;
using Menage.App.Tema;
using Menage.Core.Personalizacao;

namespace Menage.App.Modulos.Personalizacao;

/// <summary>Uma linha da aba Ícones: o alvo (tema, comando, grupo ou projeto), a chave que o identifica e o arquivo escolhido.</summary>
public sealed partial class LinhaDeIcone(AlvoDeIcone? alvo, string chave, string nome, string? caminho) : ObservableObject
{
    /// <summary>Nulo = ícone do app (por tema).</summary>
    public AlvoDeIcone? Alvo { get; } = alvo;

    public string Chave { get; } = chave;

    public string Nome { get; } = nome;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Resumo), nameof(TemIcone))]
    string? _caminho = caminho;

    public bool TemIcone => Caminho is not null;

    public string Resumo => Caminho is null ? "padrão" : Path.GetFileName(Caminho);
}

/// <summary>
/// Aba Ícones da Personalização: ícone do app por tema (e um geral), e, se as caixas estiverem marcadas, ícone por comando
/// (menu e pílulas), por grupo e por projeto. Cada escolha vale na hora (grava em aparencia.json). O arquivo é validado
/// (extensão e decodificação) antes de ser aceito, pelo mesmo cache que mostra o ícone depois.
/// </summary>
public sealed partial class IconesViewModel : ObservableObject
{
    readonly IPreferenciasDeAparencia _preferencias;
    readonly ServicoDeIcones _servico;
    readonly ISeletorDeImagem _seletor;

    [ObservableProperty]
    string _mensagem = "";

    public IconesViewModel(
        IPreferenciasDeAparencia preferencias, ServicoDeIcones servico, ICatalogoDeTemas temas, Menage.Core.Comandos.ICatalogoDeComandos comandos,
        Menage.Core.Projetos.IConfiguracaoDeProjetos configuracao, ISeletorDeImagem seletor)
    {
        (_preferencias, _servico, _seletor) = (preferencias, servico, seletor);
        var icones = preferencias.Valor.Icones;
        Temas = [new(null, IconesPersonalizados.TodosOsTemas, "Todos os temas (padrão)", Ou(icones.PorTema, IconesPersonalizados.TodosOsTemas)),
            .. temas.Todos.Select(t => new LinhaDeIcone(null, t.Id, t.Nome, Ou(icones.PorTema, t.Id)))];

        var menuSoDePilula = BarraDeAcoesViewModel.Definicoes.Where(d => d.IdPrincipal is null).Select(d => ("pilula:" + d.Texto, $"Pílula {d.Texto}"));
        Comandos = [.. comandos.Todos.Select(c => (c.Id, c.Texto)).Concat(menuSoDePilula)
            .Select(c => new LinhaDeIcone(AlvoDeIcone.Comando, c.Item1, c.Item2, Ou(icones.Comandos, c.Item1)))];

        var catalogo = configuracao.Catalogo;
        Grupos = [.. catalogo.Grupos.Select(g => new LinhaDeIcone(AlvoDeIcone.Grupo, g.Id, catalogo.NomeDoGrupo(g.Id), Ou(icones.Grupos, g.Id)))];
        Projetos = [.. configuracao.Projetos.Select(p => new LinhaDeIcone(AlvoDeIcone.Projeto, p.Caminho, p.Nome, Ou(icones.Projetos, p.Caminho)))];
    }

    public IReadOnlyList<LinhaDeIcone> Temas { get; }

    public IReadOnlyList<LinhaDeIcone> Comandos { get; }

    public ObservableCollection<LinhaDeIcone> Grupos { get; }

    public IReadOnlyList<LinhaDeIcone> Projetos { get; }

    public bool AplicarNoMenuEPilulas
    {
        get => _preferencias.Valor.Icones.AplicarNoMenuEPilulas;
        set { _preferencias.Alterar(p => p with { Icones = p.Icones with { AplicarNoMenuEPilulas = value } }); OnPropertyChanged(); }
    }

    public bool AplicarEmGruposEProjetos
    {
        get => _preferencias.Valor.Icones.AplicarEmGruposEProjetos;
        set { _preferencias.Alterar(p => p with { Icones = p.Icones with { AplicarEmGruposEProjetos = value } }); OnPropertyChanged(); }
    }

    [RelayCommand]
    void Escolher(LinhaDeIcone linha)
    {
        string? escolhido;
        try { escolhido = _seletor.Escolher(); }
        catch (Exception ex) { Mensagem = "Não foi possível abrir a seleção de arquivo: " + ex.Message; return; }
        if (escolhido is not { } caminho) return;
        if (!IconesPersonalizados.ExtensaoValida(caminho) || _servico.Imagem(caminho) is null)
        {
            Mensagem = "Esse arquivo não é uma imagem válida (use PNG, JPG, BMP, ICO ou GIF).";
            return;
        }

        Mensagem = "";
        Definir(linha, caminho);
    }

    [RelayCommand]
    void Remover(LinhaDeIcone linha) => Definir(linha, null);

    void Definir(LinhaDeIcone linha, string? caminho)
    {
        _preferencias.Alterar(p => p with
        {
            Icones = linha.Alvo is { } alvo ? p.Icones.ComIconeDe(alvo, linha.Chave, caminho) : p.Icones.ComIconeDoApp(linha.Chave, caminho),
        });
        linha.Caminho = caminho;
    }

    static string? Ou(Dictionary<string, string> mapa, string chave) => mapa.TryGetValue(chave, out var c) && c.Length > 0 ? c : null;
}
