using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Menage.Core.Configuracoes;
using Menage.Core.Interacao;

namespace Menage.App.Modulos.Configuracoes;

/// <summary>Um serviço com usuário e segredo (token ou senha). O segredo nunca é lido de volta para a tela.</summary>
public sealed partial class ItemDeCredencial(string servico, string nome, string rotuloDoSegredo) : ObservableObject
{
    public string Servico => servico;

    public string Nome => nome;

    public string RotuloDoSegredo => rotuloDoSegredo;

    [ObservableProperty]
    string _usuario = "";

    [ObservableProperty]
    bool _guardada;

    public string Estado => Guardada ? "Guardada, protegida pelo seu usuário do Windows" : "Não cadastrada";

    partial void OnGuardadaChanged(bool value) => OnPropertyChanged(nameof(Estado));
}

/// <summary>Seção "Jira e credenciais": uma credencial por serviço, guardada com DPAPI (só o mesmo usuário do Windows abre).</summary>
public sealed partial class ConfiguracoesViewModel
{
    readonly CofreDeCredenciais _cofre;

    public ObservableCollection<ItemDeCredencial> Credenciais { get; } = [];

    void CarregarCredenciais()
    {
        Credenciais.Clear();
        foreach (var (id, nome, rotulo) in ServicosDeCredencial.Todos)
            Credenciais.Add(new ItemDeCredencial(id, nome, rotulo) { Usuario = _cofre.Usuario(id), Guardada = _cofre.Tem(id) });
    }

    /// <summary>Guarda o segredo digitado (ou só troca o usuário se o segredo veio vazio e já havia um guardado).</summary>
    public void GuardarCredencial(ItemDeCredencial item, string segredo)
    {
        var r = string.IsNullOrEmpty(segredo) && item.Guardada
            ? _cofre.TrocarUsuario(item.Servico, item.Usuario)
            : _cofre.Guardar(item.Servico, item.Usuario, segredo);
        if (r.EhOk) item.Guardada = _cofre.Tem(item.Servico);
        Avisar(r.EhOk ? $"Credencial de {item.Nome} guardada." : r.Erro.ToString(), !r.EhOk);
    }

    [RelayCommand]
    async Task RemoverCredencialAsync(ItemDeCredencial? item)
    {
        if (item is not { Guardada: true }) return;
        if (!await _dialogos.ConfirmarAsync(new Previa($"Remover a credencial de {item.Nome}?", "O segredo guardado será apagado deste computador.", []))) return;
        var r = _cofre.Remover(item.Servico);
        if (r.EhOk)
        {
            item.Guardada = false;
            item.Usuario = "";
        }

        Avisar(r.EhOk ? $"Credencial de {item.Nome} removida." : r.Erro.ToString(), !r.EhOk);
    }
}
