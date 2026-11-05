# Janela principal

Código em `src/Menage.App/Modulos/JanelaPrincipal/`; motor em `Menage.Core` (`Estado/`, `Saida/`, `Operacoes/CoordenadorDeOperacoes`, `Banco/`).

## Peças

| Peça | Papel |
|---|---|
| `CoordenadorDeOperacoes` (Core) | Uma operação por vez; sinais "Parar após o projeto atual"; é o `IProgresso` das operações e escreve na Saída |
| `DialogosComResumoNaSaida` (Core) | O resumo do lote vai para a Saída e vira aviso no canto; confirmações e erros seguem como diálogos |
| `LeitorDeSituacao` / `ApresentacaoDoEstado` (Core) | Estado do git + versão do pom; regras de branch, situação, origin e arquivos |
| `GradeDeProjetosViewModel` | Linhas, marcação (ligada a `IProjetos`), busca, leitura (F5, fim de operação, foco com mínimo de 5 s) |
| `BarraLateralViewModel` | Grupos (clique troca a seleção, Shift soma), modo beta, banco (falso), recolher |
| `BarraDeAcoesViewModel` | Pílulas lidas do catálogo; apagadas com o motivo (perfil, "Em breve", sem seleção, operação em curso) |
| `SaidaViewModel` + `CalculoDeAlturas` | Saída preta, recolher, abre sozinha em erro, divisória; a grade ocupa só as linhas e a Saída fica com o resto |
| `AcoesGit` | Fetch, Pull, Sync, Checkout (diálogo de branch/tag + auto-stash), Stash (nome por projeto), Stash pop |
| `ComandosDaJanelaModulo` | Registra os comandos; Commit, Push, Cherry-pick, Branch, Diagnóstico e Gerar beta ficam como "Em breve" até o módulo deles |

Acrescentado ao design system: `Bolinha`, `BarraFina` (em `Controles/`), conversores `VisivelSe`/`OcultoSe`/`Inverter` e `AnimadorBarraLateral.DefinirSemAnimar`.

## Testar

```bash
dotnet run --project src/Menage.App -- --projetos <pasta>
```

A pasta tem repositórios direto nela ou em subpastas `web`, `framework`, `app`/`pda` (viram os grupos). A configuração dessa execução fica em `%TEMP%\menage-projetos-*`, nunca em `%LOCALAPPDATA%\Menage\projetos.json`. Use repositórios descartáveis.

Galeria automática (dados de exemplo, sem ler repositórios): `dotnet run --project src/Menage.App -- --galeria-principal docs/galeria/janela-principal` (a flag `--galeria` continua sendo a do design system).

Roteiro manual: marcar um grupo na barra lateral; Fetch; Pull; Stash (dar nome) e Stash pop; Checkout de outra branch com auto-stash; Ctrl+B recolhe a barra; Ctrl+K abre a paleta; Ctrl+F busca; F5 atualiza; clicar no cabeçalho da Saída recolhe; arrastar a divisória muda a altura.
