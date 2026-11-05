using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Menage.Core.BetaAutomatica;
using Menage.Core.Interacao;

namespace Menage.App.Modulos.BetaAutomatica;

/// <summary>Estado da tela "Betas habilitadas". Toda regra está no <see cref="ServicoDeBetaAutomatica"/>; aqui só se apresenta e se pede confirmação.</summary>
public sealed partial class BetaAutomaticaViewModel : ObservableObject, IDisposable
{
    readonly ServicoDeBetaAutomatica _servico;
    readonly IServicoDeDialogos _dialogos;
    bool _pendente;

    [ObservableProperty]
    bool _monitorAtivo;

    [ObservableProperty]
    string _status = "";

    [ObservableProperty]
    string _perfilTexto = "";

    public BetaAutomaticaViewModel(ServicoDeBetaAutomatica servico, IServicoDeDialogos dialogos)
    {
        (_servico, _dialogos) = (servico, dialogos);
        _servico.Mudou += AoMudar;
        _servico.Mensagem += _ => AoMudar();
        Atualizar();
    }

    public ObservableCollection<CartaoDeBeta> Cartoes { get; } = [];

    public ObservableCollection<CartaoDeBeta> Ignoradas { get; } = [];

    public ObservableCollection<CartaoDeBeta> Finalizadas { get; } = [];

    public ServicoDeBetaAutomatica Servico => _servico;

    public bool Vazio => Cartoes.Count == 0;

    public string TextoDoMonitor => MonitorAtivo ? "Pausar monitor" : "Ativar monitor";

    public string TituloIgnoradas => $"Ignoradas ({Ignoradas.Count})";

    public string TituloFinalizadas => $"Finalizadas ({Finalizadas.Count})";

    /// <summary>Pode chegar de qualquer thread (monitor, geração): junta várias mudanças numa atualização só.</summary>
    void AoMudar()
    {
        if (_pendente) return;
        _pendente = true;
        Application.Current?.Dispatcher.BeginInvoke(() => { _pendente = false; Atualizar(); });
    }

    public void Atualizar()
    {
        MonitorAtivo = _servico.Config.Ativo;
        PerfilTexto = _servico.ModoTester ? "Perfil Tester" : "Perfil Desenvolvedor";
        Preencher(Cartoes, _servico.Itens());
        Preencher(Ignoradas, _servico.Ignoradas());
        Preencher(Finalizadas, _servico.Finalizadas());
        var c = _servico.Config;
        Status = (c.Ativo ? "Monitor ativo" : "Monitor pausado") + $" · a cada {c.IntervaloMin} min"
            + (c.UltimaConsulta.Length > 0 ? $" · última consulta {c.UltimaConsulta}" : " · nenhuma consulta ainda")
            + (c.UltimoErro.Length > 0 ? $" · ERRO: {c.UltimoErro}" : _servico.UltimoResumo.Length > 0 ? " · " + _servico.UltimoResumo : "");
        OnPropertyChanged(nameof(Vazio));
        OnPropertyChanged(nameof(TextoDoMonitor));
        OnPropertyChanged(nameof(TituloIgnoradas));
        OnPropertyChanged(nameof(TituloFinalizadas));
    }

    void Preencher(ObservableCollection<CartaoDeBeta> destino, IReadOnlyList<ItemBeta> itens)
    {
        destino.Clear();
        foreach (var i in itens) destino.Add(new CartaoDeBeta(i, Acao));
    }

    void Acao(string nome, string chave) => _ = ExecutarAsync(nome, chave);

    async Task ExecutarAsync(string nome, string chave)
    {
        switch (nome)
        {
            case "gerar": _servico.Gerar(chave); break;
            case "cancelar": _servico.CancelarGeracao(chave); break;
            case "ignorar": _servico.Ignorar(chave); break;
            case "reverter": await _servico.ReverterIgnoradaAsync(chave); break;
            case "reabrir": await _servico.ReabrirFinalizadaAsync(chave); break;
            case "remover" when await _dialogos.ConfirmarAsync(new Previa("Remover da lista", $"Tirar {chave} da lista?", ["Ela volta na próxima verificação se ainda estiver no status monitorado."])):
                _servico.Remover(chave);
                break;
            case "finalizar" when await _dialogos.ConfirmarAsync(new Previa("Finalizar atividade", $"Marcar {chave} como finalizada?", ["Sai da lista principal e fica em Finalizadas, com data e motivo. Dá para reabrir."])):
                _servico.Finalizar(chave);
                break;
        }
    }

    public void AlternarMonitor()
    {
        var ligar = !_servico.Config.Ativo;
        _servico.Alterar(c => c.Ativo = ligar);
        if (ligar) { _servico.Iniciar(); _servico.VerificarAgora(); }
        Atualizar();
    }

    public void VerificarAgora()
    {
        _servico.Iniciar();
        _servico.VerificarAgora();
        if (!_servico.Config.Ativo) _ = _servico.CicloAsync(false, CancellationToken.None);
    }

    public async Task LimparListaAsync()
    {
        if (await _dialogos.ConfirmarAsync(new Previa("Limpar lista", "Remover todas as atividades da lista?", ["Ficam só as que estão na fila ou gerando; as finalizadas continuam. As removidas voltam na próxima verificação se ainda estiverem no status."])))
            _servico.Limpar();
    }

    public void CancelarTodas() => _servico.CancelarTodas();

    public void Dispose() => _servico.Mudou -= AoMudar;
}
