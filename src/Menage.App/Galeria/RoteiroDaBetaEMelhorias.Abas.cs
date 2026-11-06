using Menage.App.Modulos.BetaAutomatica;
using Microsoft.Extensions.DependencyInjection;

namespace Menage.App.Galeria;

/// <summary>Parte do roteiro da galeria que captura a janela da Beta automática: cada aba e o console ancorado (expandido e recolhido).</summary>
public sealed partial class RoteiroDaBetaEMelhorias
{
    static readonly string[] DiagnosticoDeExemplo =
    [
        "=== DIAGNÓSTICO DA BUSCA NO JIRA (simulação: nada é alterado) - 06/11/2026 09:41:12 ===",
        "Jira: https://jira2.wmw.com.br   token (JIRA_TOKEN): definido",
        "Usuário do token: voce",
        "Papel: Desenvolvedor (minhas subtasks Coding, comentários meus com PROJETOS ALTERADOS/BRANCH, ou passagem minha de \"In Development\" para \"Waiting for Code Review\")",
        "",
        "--- VENDAS-45120  \"Corrige o desconto do pedido em lote\"",
        "    tipo: Story   status: Waiting for Testing   responsável: voce",
        "    DECISÃO: NOVA - comentário seu com PROJETOS ALTERADOS/BRANCH",
        "    escopo: ENTRA - comentário seu com PROJETOS ALTERADOS/BRANCH",
        "      [ok] lavenderecore (citado) - branch local",
        "      [--] lavendereweb (citado) - a branch não existe local nem em origin",
        "      [tag] lavenderedados (complemento da beta web) - tag 9.060",
        "    verificação (sem alterar nada): a branch existe em 1 projeto(s) -> ficaria \"Pronta para gerar\" e alertaria",
        "",
        "--- VENDAS-45101  \"Nova coluna de margem na grade de produtos\"",
        "    tipo: Sub-task (subtask)   status: Waiting for Testing   responsável: voce",
        "    DECISÃO: JATRATADA - já está na fila, sem nova passagem",
        "    escopo: ENTRA - subtask Coding atribuída a você",
        "",
        "--- VENDAS-45130  \"Revisão do texto do e-mail de cobrança\"",
        "    tipo: Story   status: Waiting for Testing   responsável: outra pessoa",
        "    DECISÃO: FORADOESCOPO - não é sua e não tem comentário seu",
        "    escopo: FORA - atribuída a outra pessoa e sem comentário PROJETOS ALTERADOS seu",
        "",
        "=== RESUMO: 3 atividade(s) devolvida(s) pelo Jira, simulação em 1204 ms: 1 NOVA; 1 JATRATADA; 1 FORADOESCOPO ===",
    ];

    async Task CapturarAbasAsync(JanelaBetaAutomatica painel, string id)
    {
        var vm = servicos.GetRequiredService<BetaAutomaticaViewModel>();
        vm.SelecionarAba(AbaDaBeta.Betas);
        vm.Atualizar();
        await Esperar(500);

        Conferir($"{id}: aba Betas com 6 cartões, 1 ignorada e 1 finalizada", vm.Cartoes.Count == 6 && vm.Ignoradas.Count == 1 && vm.Finalizadas.Count == 1);
        Conferir($"{id}: contador da aba Betas = 6 e em alerta (há beta com problema)", vm.Abas[0] is { Contador: 6, Alerta: true });
        Conferir($"{id}: cartão pronto oferece Gerar beta", vm.Cartoes.First(c => c.Chave == "VENDAS-45120").PodeGerar);
        Conferir($"{id}: cartão gerando oferece Cancelar e não Gerar", vm.Cartoes.First(c => c.Chave == "VENDAS-45101") is { PodeCancelar: true, PodeGerar: false });
        Conferir($"{id}: console ancorado tem 1 erro", vm.Console.Erros == 1);

        vm.SelecionarBeta("VENDAS-45120");
        vm.Console.ExpandirDock();
        await Esperar(400);
        Salvar(painel, $"beta-abas-1-betas-{id}");

        vm.SelecionarBeta("VENDAS-45101");
        await Esperar(300);
        Salvar(painel, $"beta-abas-1-betas-gerando-{id}");

        vm.SelecionarBeta("VENDAS-45077");
        await Esperar(300);
        Salvar(painel, $"beta-abas-1-betas-com-problema-{id}");
        vm.SelecionarBeta("VENDAS-45120");

        vm.Console.Expandido = false;
        await Esperar(300);
        Salvar(painel, $"beta-abas-1-betas-console-recolhido-{id}");
        painel.TelaDeBetas.AbrirGavetas();
        await Esperar(300);
        Salvar(painel, $"beta-abas-1-betas-gavetas-abertas-{id}");
        painel.TelaDeBetas.FecharGavetas();
        vm.Console.Expandido = true;

        vm.SelecionarAba(AbaDaBeta.Configurar);
        await Esperar(700);
        Conferir($"{id}: Configurar abre sem alteração pendente", !vm.Configurar.Alterado);
        Salvar(painel, $"beta-abas-2-configurar-{id}");
        vm.Configurar.StatusAlvo = "Ready for Beta";
        vm.Configurar.Chaves.Add("PDA");
        await Esperar(300);
        Conferir($"{id}: Configurar editado habilita Salvar e Cancelar", vm.Configurar.Alterado);
        Salvar(painel, $"beta-abas-2-configurar-alterado-{id}");
        vm.Configurar.CancelarCommand.Execute(null);
        Conferir($"{id}: Cancelar descarta a alteração", !vm.Configurar.Alterado && vm.Configurar.Chaves.Count == 1);

        vm.SelecionarAba(AbaDaBeta.Jql);
        await Esperar(700);
        Conferir($"{id}: JQL lista as consultas padrão do perfil", vm.Jql.Linhas.Count >= 1);
        Salvar(painel, $"beta-abas-3-jql-{id}");

        vm.SelecionarAba(AbaDaBeta.Diagnostico);
        await Esperar(500);
        Salvar(painel, $"beta-abas-4-diagnostico-vazio-{id}");
        vm.Diagnostico.Receber(DiagnosticoDeExemplo);
        await Esperar(500);
        Conferir($"{id}: Diagnóstico mostra 3 atividades, 2 entram e 1 fica de fora", vm.Diagnostico.Decisoes.Count == 3 && vm.Diagnostico.Relatorio is { Entram: 2, FicamDeFora: 1 });
        Salvar(painel, $"beta-abas-4-diagnostico-{id}");

        vm.SelecionarAba(AbaDaBeta.Betas);
        await Esperar(500);
    }
}
