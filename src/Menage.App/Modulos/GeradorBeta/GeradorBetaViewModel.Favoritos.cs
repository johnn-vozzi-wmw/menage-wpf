using Menage.Core;
using Menage.Core.Interacao;

namespace Menage.App.Modulos.GeradorBeta;

/// <summary>Favoritos: seleção + modo com nome. Clique aplica; o menu do cartão faz o resto.</summary>
public sealed partial class GeradorBetaViewModel
{
    public void RecarregarFavoritos()
    {
        var cartoes = Configuracao.Favoritos.Select(f => new CartaoDeFavorito(f, f.Igual(Deploy, Ordem))).ToList();
        if (Favoritos.Count == cartoes.Count && Favoritos.Zip(cartoes).All(p => p.First == p.Second)) return;
        Favoritos.Clear();
        foreach (var c in cartoes) Favoritos.Add(c);
    }

    public async Task AplicarFavoritoAsync(string nome, bool gerar)
    {
        if (Executando || _repositorio.Achar(nome) is not { } f) return;
        if (f.Deploy && !DeployLiberado)
        {
            await _dialogos.InformarAsync("Perfil Tester", "O perfil Tester não faz deploy. O favorito foi aplicado em install.");
            f = f with { Deploy = false };
        }

        Deploy = f.Deploy;
        var existentes = f.Projetos.Where(n => Linhas.Any(l => l.Nome == n && l.Encontrado)).ToList();
        foreach (var l in Linhas) l.Marcado = existentes.Contains(l.Nome);
        DefinirOrdem(existentes);
        Mensagem = $"Favorito \"{f.Nome}\" aplicado ({existentes.Count} projeto(s), {(Deploy ? "deploy" : "install")}).";
        if (gerar) await GerarAsync();
    }

    public async Task SalvarFavoritoAsync()
    {
        if (Executando) return;
        var nome = await _janelas.AbrirDialogoAsync(JanelaDeTexto.Id, new PedidoDeTexto("Novo favorito", "Nome do favorito", "")) as string;
        if (!string.IsNullOrWhiteSpace(nome)) await Resposta(_repositorio.SalvarFavorito(nome, Deploy, Ordem), $"Favorito \"{nome.Trim()}\" salvo.");
    }

    public async Task AtualizarFavoritoAsync(string nome) =>
        await Resposta(_repositorio.AtualizarFavorito(nome, Deploy, Ordem), $"Favorito \"{nome}\" atualizado com a seleção atual.");

    public async Task RenomearFavoritoAsync(string nome)
    {
        var novo = await _janelas.AbrirDialogoAsync(JanelaDeTexto.Id, new PedidoDeTexto("Renomear favorito", "Novo nome", nome)) as string;
        if (!string.IsNullOrWhiteSpace(novo)) await Resposta(_repositorio.RenomearFavorito(nome, novo), $"Favorito renomeado para \"{novo.Trim()}\".");
    }

    public async Task ExcluirFavoritoAsync(string nome)
    {
        var confirmou = await _dialogos.ConfirmarAsync(new Previa("Excluir favorito", $"Excluir o favorito \"{nome}\"?", []));
        if (confirmou) await Resposta(_repositorio.ExcluirFavorito(nome), $"Favorito \"{nome}\" excluído.");
    }

    async Task Resposta(Resultado<Nada> resultado, string sucesso)
    {
        if (resultado.EhOk) Mensagem = sucesso;
        else await _dialogos.MostrarErroAsync(resultado.Erro);
        Favoritos.Clear();
        RecarregarFavoritos();
    }
}
