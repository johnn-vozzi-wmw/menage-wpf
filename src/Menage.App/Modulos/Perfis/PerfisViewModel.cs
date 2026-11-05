using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Menage.Core;
using Menage.Core.Interacao;
using Menage.Core.Perfis;

namespace Menage.App.Modulos.Perfis;

/// <summary>Linha da lista de perfis.</summary>
public sealed record ItemDePerfil(string Id, string Nome, string Detalhe, bool Ativo, bool Embutido);

/// <summary>Uma funcionalidade com a caixa de marcar do editor.</summary>
public sealed partial class ItemDeFuncionalidade(Funcionalidade funcionalidade) : ObservableObject
{
    public string Id => funcionalidade.Id;

    public string Nome => funcionalidade.Nome;

    public string Grupo => funcionalidade.Grupo;

    [ObservableProperty]
    bool _marcada;

    /// <summary>Melhorias, Diário e Beta automática só existem para perfis de base Desenvolvedor (a Beta também para Tester).</summary>
    [ObservableProperty]
    bool _habilitada = true;

    [ObservableProperty]
    string? _dica;
}

/// <summary>Tela de gerenciar perfis. Só orquestra o <see cref="ServicoDePerfis"/>: nenhuma regra de perfil mora aqui.</summary>
public sealed partial class PerfisViewModel : ObservableObject
{
    static readonly PerfilDeUso[] _bases = [PerfilDeUso.Geral, PerfilDeUso.Desenvolvedor, PerfilDeUso.Tester];

    readonly ServicoDePerfis _perfis;
    readonly IServicoDeDialogos _dialogos;
    bool _carregando;
    string _idEditando = "";

    public PerfisViewModel(ServicoDePerfis perfis, IServicoDeDialogos dialogos)
    {
        _perfis = perfis;
        _dialogos = dialogos;
        foreach (var f in Funcionalidades.Catalogo) Opcoes.Add(new ItemDeFuncionalidade(f));
        foreach (var f in Opcoes) f.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ItemDeFuncionalidade.Marcada)) Marcar(); };
        Recarregar(_perfis.Ativo.Id);
    }

    public ObservableCollection<ItemDePerfil> Itens { get; } = [];

    public ObservableCollection<ItemDeFuncionalidade> Opcoes { get; } = [];

    public static IReadOnlyList<PerfilDeUso> Bases => _bases;

    [ObservableProperty]
    ItemDePerfil? _selecionado;

    [ObservableProperty]
    string _nome = "";

    [ObservableProperty]
    string _descricao = "";

    [ObservableProperty]
    PerfilDeUso _base;

    [ObservableProperty]
    bool _alterado;

    [ObservableProperty]
    bool _somenteLeitura;

    [ObservableProperty]
    bool _ehNovo;

    [ObservableProperty]
    string _mensagem = "";

    [ObservableProperty]
    bool _mensagemEhErro;

    public bool PodeEditarBase => !SomenteLeitura && Selecionado is not { Embutido: true };

    public bool PodeExcluir => Selecionado is { Embutido: false };

    public bool PodeRestaurar => Selecionado is { Id: PerfilDefinido.IdDesenvolvedor or PerfilDefinido.IdTester };

    public bool PodeAtivar => Selecionado is { Ativo: false } && !EhNovo && !Alterado;

    public string Titulo => EhNovo ? "Novo perfil (ainda não salvo)" : Selecionado?.Nome ?? "";

    partial void OnNomeChanged(string value) => Marcar();

    partial void OnDescricaoChanged(string value) => Marcar();

    partial void OnBaseChanged(PerfilDeUso value)
    {
        AjustarHabilitadas();
        Marcar();
    }

    partial void OnAlteradoChanged(bool value) => OnPropertyChanged(nameof(PodeAtivar));

    partial void OnSomenteLeituraChanged(bool value) => OnPropertyChanged(nameof(PodeEditarBase));

    partial void OnSelecionadoChanged(ItemDePerfil? value)
    {
        OnPropertyChanged(nameof(PodeExcluir));
        OnPropertyChanged(nameof(PodeEditarBase));
        OnPropertyChanged(nameof(PodeRestaurar));
        OnPropertyChanged(nameof(PodeAtivar));
        OnPropertyChanged(nameof(Titulo));
    }

    /// <summary>Muda de perfil na lista. Devolve falso se havia edição e o usuário preferiu ficar.</summary>
    public async Task<bool> TrocarAsync(ItemDePerfil? destino)
    {
        if (destino is null || (Selecionado is not null && destino.Id == Selecionado.Id && !EhNovo)) return true;
        if (Alterado && !await _dialogos.ConfirmarAsync(new Previa("Descartar alterações?",
                "Você alterou este perfil e ainda não salvou.", ["Continuar descarta as alterações."]))) return false;
        Carregar(_perfis.Obter(destino.Id)!);
        return true;
    }

    [RelayCommand]
    void Novo()
    {
        _carregando = true;
        Selecionado = null;
        EhNovo = true;
        _idEditando = "";
        Nome = "";
        Descricao = "";
        Base = PerfilDeUso.Geral;
        SomenteLeitura = false;
        AjustarHabilitadas();
        foreach (var f in Opcoes) f.Marcada = f.Habilitada;
        _carregando = false;
        Alterado = true;
        OnPropertyChanged(nameof(Titulo));
        Avisar("Marque o que este perfil libera e salve.");
    }

    [RelayCommand]
    void Duplicar()
    {
        if (Selecionado is null) return;
        Concluir(_perfis.Duplicar(Selecionado.Id), "Perfil duplicado.");
    }

    [RelayCommand]
    async Task ExcluirAsync()
    {
        if (Selecionado is not { Embutido: false } alvo) return;
        var detalhes = alvo.Ativo ? new List<string> { "É o perfil ativo: o Menage volta para o perfil Geral." } : [];
        if (!await _dialogos.ConfirmarAsync(new Previa($"Excluir o perfil \"{alvo.Nome}\"?", "Esta ação não pode ser desfeita.", detalhes))) return;
        var r = _perfis.Excluir(alvo.Id);
        if (r.EhOk) Recarregar(_perfis.Ativo.Id);
        Avisar(r.EhOk ? "Perfil excluído." : r.Erro.ToString(), !r.EhOk);
    }

    [RelayCommand]
    void Ativar()
    {
        if (Selecionado is null) return;
        var r = _perfis.Ativar(Selecionado.Id);
        if (r.EhOk) Recarregar(Selecionado.Id);
        Avisar(r.EhOk ? $"Perfil ativo: {_perfis.Ativo.Nome}." : r.Erro.ToString(), !r.EhOk);
    }

    [RelayCommand]
    void Salvar()
    {
        if (SomenteLeitura) return;
        var existente = _perfis.Obter(_idEditando);
        var perfil = new PerfilDefinido(_idEditando, Nome, Descricao.Trim(), Base, MontarPermitidas());
        var r = _perfis.Salvar(perfil);
        if (r.EhOk)
        {
            Recarregar(r.Valor.Id);
            Avisar(existente is null ? "Perfil criado." : "Perfil salvo.");
        }
        else Avisar(r.Erro.ToString(), true);
    }

    [RelayCommand]
    void Restaurar()
    {
        if (Selecionado is null) return;
        var r = _perfis.RestaurarPadrao(Selecionado.Id);
        if (r.EhOk) Recarregar(Selecionado.Id);
        Avisar(r.EhOk ? "Lista restaurada para o padrão." : r.Erro.ToString(), !r.EhOk);
    }

    [RelayCommand]
    void MarcarTodas() => Definir(true);

    [RelayCommand]
    void MarcarNenhuma() => Definir(false);

    public void Exportar(string caminho)
    {
        var ids = Selecionado is { Embutido: false } ? [Selecionado.Id] : _perfis.Perfis.Where(p => p.Id != PerfilDefinido.IdGeral).Select(p => p.Id).ToArray();
        var r = _perfis.Exportar(ids, caminho);
        Avisar(r.EhOk ? $"{ids.Length} perfil(is) exportado(s) para {caminho}" : r.Erro.ToString(), !r.EhOk);
    }

    public void Importar(string caminho)
    {
        var r = _perfis.Importar(caminho);
        if (r.EhOk) Recarregar(_perfis.Ativo.Id);
        Avisar(r.EhOk ? $"{r.Valor} perfil(is) importado(s)." : r.Erro.ToString(), !r.EhOk);
    }

    void Concluir(Resultado<PerfilDefinido> r, string sucesso)
    {
        if (r.EhOk) Recarregar(r.Valor.Id);
        Avisar(r.EhOk ? sucesso : r.Erro.ToString(), !r.EhOk);
    }

    void Recarregar(string idParaSelecionar)
    {
        Itens.Clear();
        foreach (var p in _perfis.Perfis)
            Itens.Add(new ItemDePerfil(p.Id, p.Nome, p.Embutido ? "Faz parte do Menage" : "Criado por você", p.Id == _perfis.Ativo.Id, p.Embutido));
        var escolhido = Itens.FirstOrDefault(i => i.Id == idParaSelecionar) ?? Itens[0];
        Carregar(_perfis.Obter(escolhido.Id)!);
    }

    void Carregar(PerfilDefinido perfil)
    {
        _carregando = true;
        Selecionado = Itens.First(i => i.Id == perfil.Id);
        EhNovo = false;
        _idEditando = perfil.Id;
        Nome = perfil.Nome;
        Descricao = perfil.Descricao;
        Base = perfil.Base;
        SomenteLeitura = perfil.Id == PerfilDefinido.IdGeral;
        AjustarHabilitadas();
        foreach (var f in Opcoes) f.Marcada = perfil.Permite(f.Id);
        _carregando = false;
        Alterado = false;
        OnPropertyChanged(nameof(Titulo));
        OnPropertyChanged(nameof(PodeAtivar));
    }

    void AjustarHabilitadas()
    {
        foreach (var f in Opcoes)
        {
            var possivel = !Funcionalidades.ExigeBaseDesenvolvedor(f.Id) || Base == PerfilDeUso.Desenvolvedor || (f.Id == Funcionalidades.BetaAutomatica && Base == PerfilDeUso.Tester);
            f.Habilitada = possivel && !SomenteLeitura;
            f.Dica = possivel ? null : "Só em perfis com base Desenvolvedor.";
            if (!possivel) f.Marcada = false;
        }
    }

    // Tudo marcado (entre o que pode ser marcado) vira "*": novas funcionalidades futuras entram sozinhas.
    List<string> MontarPermitidas()
    {
        var possiveis = Opcoes.Where(f => f.Id is not (Funcionalidades.Melhorias or Funcionalidades.Diario or Funcionalidades.BetaAutomatica) || f.Dica is null).ToList();
        return possiveis.All(f => f.Marcada) ? ["*"] : Opcoes.Where(f => f.Marcada).Select(f => f.Id).ToList();
    }

    void Definir(bool marcada)
    {
        foreach (var f in Opcoes.Where(f => f.Habilitada)) f.Marcada = marcada;
    }

    void Marcar()
    {
        if (!_carregando && !SomenteLeitura) Alterado = true;
    }

    void Avisar(string texto, bool erro = false)
    {
        Mensagem = texto;
        MensagemEhErro = erro;
    }
}
