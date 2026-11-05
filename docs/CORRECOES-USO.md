# Correções de uso (Menage 1.2.0)

Lista guiada pelo usuário após uso real do app instalado. Branch: `feature/correcoes-uso`.
Status: `pendente` | `em andamento` | `corrigido` | `não reproduzido` | `adiado`.

| Id | Descrição | Módulo | Gravidade | Status |
|---|---|---|---|---|
| C1 | Personalização de ícones como no Git Manager: ícone por tema, trocar o ícone de cada tema, usar GIF no lugar do ícone e escolher onde cada ícone é aplicado. Prioridade: velocidade e otimização (cache de imagens, decodificação única, GIF animado leve). | Personalização / Temas | Alta (funcionalidade ausente) | pendente |
| C2 | Lentidão em alguns processos e consumo alto de CPU/GPU em todo o app. Aplicar cache e algoritmos conhecidos (debounce, virtualização, memoização, cache de resultados do Git, animações só quando visíveis). | Transversal | Alta | pendente |
| C3 | Autocomplete de busca lento (ex.: campo de branch com sugestões "VENDAS-24803-re…"). Deve ser instantâneo a cada letra, com lista de resultados abaixo, estilo moderno (capturas 1 e 2). | Controles (autocomplete) | Alta | pendente |
| C4 | Todos os componentes devem ser ágeis e fluidos (revisão geral de resposta da interface). | Transversal | Média | pendente |
| C5 | Menu lateral recolhido (Ctrl+B) fica feio: ícones cortados, itens sem rótulo, controles (Modo beta, Fetch automático, Banco de dados) truncados. Redesenhar de forma moderna; possibilidade de layouts predefinidos escolhidos nas configurações do usuário (capturas 3 e 4). | Janela principal | Média | pendente |

Observações do usuário: C4 e C2 se sobrepõem; tratar C2 por medição (itens concretos achados) e C4 como revisão de componentes após C3.
